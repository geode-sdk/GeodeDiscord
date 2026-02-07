using System.IO.Hashing;
using System.Text;
using Discord;
using Discord.Net.Converters;
using Discord.Rest;
using Discord.WebSocket;
using GeodeDiscord.Database.Entities;
using NetVips;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Image = Discord.Image;
using Log = Serilog.Log;

namespace GeodeDiscord;

public class QuoteRenderer(DiscordSocketClient client) {
    public async IAsyncEnumerable<IMessageComponentBuilder> Render(Quote quote) {
        IAsyncEnumerable<IMessageComponentBuilder> components =
            RenderQuote(await QuoteComponentData.Default(client, quote));
        await foreach (IMessageComponentBuilder component in components) {
            yield return component;
        }
    }

    public IAsyncEnumerable<IMessageComponentBuilder> RenderCensored(Quote quote) =>
        RenderQuote(QuoteComponentData.Censored(quote));

    private readonly record struct QuoteComponentData(
        string fullName,
        QuoteComponentData.Reply? reply,
        string? content,
        ICollection<Quote.Attachment> attachments,
        ICollection<Quote.Embed> embeds,
        byte[] components,
        string author,
        string channel,
        string quoter,
        string createdAt,
        string? jumpUrl
    ) {
        public readonly record struct Reply(string author, string content);

        public static async Task<QuoteComponentData> Default(DiscordSocketClient client, Quote quote) {
            IChannel? channel = await Util.GetChannelAsync(client, quote.channelId);

            bool hasReply = quote.replyAuthorId != 0 && quote.replyMessageId != 0 &&
                !string.IsNullOrEmpty(quote.replyContent);

            return new QuoteComponentData {
                fullName = quote.GetFullName(),
                reply = hasReply ? new Reply($"<@{quote.replyAuthorId}>", quote.replyContent) : null,
                content = string.IsNullOrWhiteSpace(quote.content) ? null : quote.content,
                attachments = quote.attachments,
                embeds = quote.embeds,
                components = quote.components,
                author = $"<@{quote.authorId}>",
                channel = channel?.Name ?? "<unknown>",
                quoter = quote.quoterId == 0 ? "<unknown>" : $"<@{quote.quoterId}>",
                createdAt = $"<t:{quote.createdAt.ToUnixTimeSeconds()}:s>",
                jumpUrl = string.IsNullOrWhiteSpace(quote.jumpUrl) ? null : quote.jumpUrl
            };
        }

        public static QuoteComponentData Censored(Quote quote) {
            bool hasReply = quote.replyAuthorId != 0 && quote.replyMessageId != 0 &&
                !string.IsNullOrEmpty(quote.replyContent);

            return new QuoteComponentData {
                fullName = "?????",
                reply = hasReply ? new Reply("?????", quote.replyContent) : null,
                content = !string.IsNullOrWhiteSpace(quote.content) ? quote.content : null,
                attachments = quote.attachments,
                embeds = quote.embeds,
                components = quote.components,
                author = "?????",
                channel = "?????",
                quoter = "?????",
                createdAt = "?????",
                jumpUrl = null
            };
        }
    }

    private async IAsyncEnumerable<IMessageComponentBuilder> RenderQuote(QuoteComponentData data) {
        yield return new ContainerBuilder()
            .WithAccentColor(new Color(0xf19060))
            .WithTextDisplay($"**{data.fullName}**")
            .AddComponents(await RenderQuoteContents(data).ToArrayAsync());
        yield return RenderQuoteFooter(data);
    }

    private async IAsyncEnumerable<IMessageComponentBuilder> RenderQuoteContents(QuoteComponentData data) {
        StringBuilder description = new();
        if (data.reply is not null) {
            string[] replyContent = data.reply.Value.content.Split(["\n", "\r\n"], StringSplitOptions.None);
            description.AppendLine($"> -# {data.reply.Value.author}: {replyContent[0]}");
            for (int i = 1; i < replyContent.Length; i++)
                description.AppendLine($"> -# {replyContent[i]}");
        }
        if (data.content is not null)
            description.AppendLine(data.content);
        if (description.Length > 0)
            yield return new TextDisplayBuilder(description.ToString());

        List<MediaGalleryItemProperties> gallery = RenderQuoteGallery(data);
        if (gallery.Count > 0)
            yield return new MediaGalleryBuilder(gallery);

        string attachmentsText = RenderQuoteAttachments(data);
        if (attachmentsText.Length > 0)
            yield return new TextDisplayBuilder(attachmentsText);

        await foreach (IMessageComponentBuilder component in RenderQuoteEmbeds(data))
            yield return component;

        await foreach (IMessageComponentBuilder component in RenderQuoteComponents(data))
            yield return component;

        yield return new TextDisplayBuilder($"\\- {data.author} in `#{data.channel}`");
    }

    private static List<MediaGalleryItemProperties> RenderQuoteGallery(QuoteComponentData data) {
        List<MediaGalleryItemProperties> gallery = data.attachments
            .Where(x => x.contentType is not null &&
                (x.contentType.StartsWith("image/") || x.contentType.StartsWith("video/")))
            .Select(x => new MediaGalleryItemProperties {
                Media = new UnfurledMediaItemProperties(x.url),
                Description = x.description,
                IsSpoiler = x.isSpoiler
            })
            .ToList();
        return gallery;
    }

    private static string RenderQuoteAttachments(QuoteComponentData data) {
        List<Quote.Attachment> attachments = data.attachments
            .Where(x => x.contentType is null ||
                !x.contentType.StartsWith("image/") && !x.contentType.StartsWith("video/"))
            .ToList();
        StringBuilder attachmentsText = new();
        foreach (Quote.Attachment attachment in attachments) {
            if (attachment.isSpoiler)
                attachmentsText.Append("||");
            attachmentsText.Append($"`{attachment.name}`:");
            attachmentsText.Append($" `{Util.FormatSize(attachment.size)}`");
            attachmentsText.Append($" [download]({attachment.url})");
            if (attachment.description is not null)
                attachmentsText.Append($" (`{attachment.description}`)");
            if (attachment.isSpoiler)
                attachmentsText.Append("||");
            attachmentsText.AppendLine();
        }
        return attachmentsText.ToString();
    }

    private async IAsyncEnumerable<IMessageComponentBuilder> RenderQuoteEmbeds(QuoteComponentData data) {
        Dictionary<string, ICollection<string>> galleries = [];
        List<EmbedComponentData> embeds = await data.embeds
            .ToAsyncEnumerable()
            .SelectAwait(async x => await EmbedComponentData.From(x, galleries))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToListAsync();
        foreach (EmbedComponentData embed in embeds) {
            yield return new SeparatorBuilder();
            await foreach (IMessageComponentBuilder? component in RenderEmbed(embed)) {
                if (component is not null)
                    yield return component;
            }
        }
        if (embeds.Count > 0)
            yield return new SeparatorBuilder();
    }

    private readonly record struct EmbedComponentData(
        ICollection<string> gallery,
        (string name, string? url)? provider = null,
        string? authorIcon = null,
        (string name, string? url)? author = null,
        string? title = null,
        string? url = null,
        string? thumbnail = null,
        string? description = null,
        string? footerIcon = null,
        string? footerText = null,
        DateTimeOffset? timestamp = null
    ) {
        public static async Task<EmbedComponentData?> From(Quote.Embed embed,
            Dictionary<string, ICollection<string>> galleries) {
            string? videoUrl = embed.videoUrl is not null && await IsVideoFile(embed.videoUrl) ? embed.videoUrl : null;
            if (embed.url is not null && galleries.TryGetValue(embed.url, out ICollection<string>? gallery)) {
                string? media = videoUrl ?? embed.imageUrl;
                if (media is not null)
                    gallery.Add(media);
                return null;
            }
            EmbedComponentData data = embed.type switch {
                EmbedType.Gifv => new EmbedComponentData {
                    gallery = videoUrl is null ? [] : [videoUrl]
                },
                EmbedType.Rich => new EmbedComponentData {
                    provider = embed.providerName is null ? null : (embed.providerName, embed.providerUrl),
                    authorIcon = embed.authorIconUrl,
                    author = embed.authorName is null ? null : (embed.authorName, embed.authorUrl),
                    title = embed.title,
                    url = embed.url,
                    thumbnail = embed.thumbnailUrl,
                    description = embed.description,
                    gallery = videoUrl is not null ? [videoUrl] :
                        embed.imageUrl is not null ? [embed.imageUrl] : [],
                    footerIcon = embed.footerIconUrl,
                    footerText = embed.footerText,
                    timestamp = embed.timestamp
                },
                _ => new EmbedComponentData {
                    provider = embed.providerName is null ? null : (embed.providerName, embed.providerUrl),
                    authorIcon = embed.authorIconUrl,
                    author = embed.authorName is null ? null : (embed.authorName, embed.authorUrl),
                    title = embed.title,
                    url = embed.url,
                    description = embed.description,
                    gallery = videoUrl is not null ? [videoUrl] :
                        embed.imageUrl is not null ? [embed.imageUrl] :
                        embed.thumbnailUrl is not null ? [embed.thumbnailUrl] : [],
                    footerIcon = embed.footerIconUrl,
                    footerText = embed.footerText,
                    timestamp = embed.timestamp
                }
            };
            if (data.url is not null)
                galleries.Add(data.url, data.gallery);
            return data;
        }

        private static async Task<bool> IsVideoFile(string url) {
            if (url.StartsWith("https://cdn.discordapp.com") ||
                url.StartsWith("https://media.discordapp.net"))
                return true;
            try {
                using HttpClient http = new();
                HttpResponseMessage response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return false;
                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is null || !mediaType.StartsWith("video/"))
                    return false;
            }
            catch {
                return false;
            }
            return true;
        }
    }

    private async IAsyncEnumerable<IMessageComponentBuilder?> RenderEmbed(EmbedComponentData data) {
        StringBuilder text = new();
        RenderEmbedProvider(data, text);
        await RenderEmbedAuthor(data, text);
        RenderEmbedTitle(data, text);
        RenderEmbedDescription(data, text);
        yield return RenderEmbedThumbnail(data, text.Length > 0 ? new TextDisplayBuilder(text.ToString()) : null);
        // TODO: fields
        yield return RenderEmbedGallery(data);
        yield return await RenderEmbedFooter(data);
    }

    private static void RenderEmbedProvider(EmbedComponentData data, StringBuilder text) {
        if (data.provider is null)
            return;
        if (data.provider.Value.url is null)
            text.AppendLine($"-# {data.provider.Value.name}");
        else
            text.AppendLine($"-# [{data.provider.Value.name}]({data.provider.Value.url})");
    }

    private async Task RenderEmbedAuthor(EmbedComponentData data, StringBuilder text) {
        if (data.authorIcon is not null) {
            Emote? emote = await GetOrCreateIconEmote(data.authorIcon);
            if (emote is not null)
                text.Append($"<:{emote.Name}:{emote.Id}>");
            if (data.author is not null)
                text.Append("  ");
        }
        if (data.author is null)
            return;
        if (data.author.Value.url is null)
            text.AppendLine($"**{data.author.Value.name}**");
        else
            text.AppendLine($"[**{data.author.Value.name}**]({data.author.Value.url})");
    }

    private static void RenderEmbedTitle(EmbedComponentData data, StringBuilder text) {
        if (data.title is null)
            return;
        if (data.url is null)
            text.AppendLine($"### {data.title}");
        else
            text.AppendLine($"### [{data.title}]({data.url})");
    }

    private static void RenderEmbedDescription(EmbedComponentData data, StringBuilder text) {
        if (data.description is null)
            return;
        text.AppendLine(data.description);
    }

    private static IMessageComponentBuilder? RenderEmbedThumbnail(EmbedComponentData data, TextDisplayBuilder? text) {
        if (data.thumbnail is null)
            return text;
        text ??= new TextDisplayBuilder("_ _");
        return new SectionBuilder(
            new ThumbnailBuilder(new UnfurledMediaItemProperties(data.thumbnail)),
            [text]
        );
    }

    private static MediaGalleryBuilder? RenderEmbedGallery(EmbedComponentData data) {
        if (data.gallery.Count == 0)
            return null;
        return new MediaGalleryBuilder(
            data.gallery.Select(x => new MediaGalleryItemProperties(new UnfurledMediaItemProperties(x)))
        );
    }

    private async Task<TextDisplayBuilder?> RenderEmbedFooter(EmbedComponentData data) {
        StringBuilder footer = new();

        if (data.footerIcon is not null) {
            Emote? emote = await GetOrCreateIconEmote(data.footerIcon);
            if (emote is not null)
                footer.Append($"<:{emote.Name}:{emote.Id}>");
            if (data.footerText is not null || data.timestamp is not null)
                footer.Append("  ");
        }

        if (data.footerText is not null) {
            footer.Append(data.footerText);
            if (data.timestamp is not null)
                footer.Append("  •  ");
        }

        if (data.timestamp is not null) {
            footer.Append($"<t:{data.timestamp.Value.ToUnixTimeSeconds()}:s>");
        }

        return footer.Length > 0 ? new TextDisplayBuilder(footer.ToString()) : null;
    }

    private static async IAsyncEnumerable<IMessageComponentBuilder> RenderQuoteComponents(QuoteComponentData data) {
        if (data.components.Length == 0)
            yield break;
        IEnumerable<IMessageComponent>? original;
        try {
            using MemoryStream stream = new(data.components);
            await using BsonDataReader bson = new(stream);
            original = JsonSerializer.Create(new JsonSerializerSettings {
                ContractResolver = new DiscordContractResolver()
            }).Deserialize<Quote.FakeMessage>(bson)?.components.Select(x => x.ToEntity());
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to render components");
            original = null;
        }
        if (original is null) {
            yield return new TextDisplayBuilder("-# `❌ Failed to render components!`");
            yield break;
        }
        foreach (IMessageComponentBuilder component in RenderQuoteComponents(original))
            yield return component;
    }

    private static IEnumerable<IMessageComponentBuilder> RenderQuoteComponents(IEnumerable<IMessageComponent> orig) {
        bool lastWasContainer = false;
        foreach (IMessageComponent component in orig) {
            IMessageComponentBuilder builder = component.ToBuilder();
            if (builder is not ContainerBuilder container) {
                yield return FilterQuoteComponent(builder);
                lastWasContainer = false;
                continue;
            }
            if (!lastWasContainer)
                yield return new SeparatorBuilder();
            foreach (IMessageComponentBuilder child in container.Components)
                yield return FilterQuoteComponent(child);
            yield return new SeparatorBuilder();
            lastWasContainer = true;
        }
    }

    private static IMessageComponentBuilder FilterQuoteComponent(IMessageComponentBuilder component) {
        switch (component) {
            case IComponentContainer container:
                foreach (IMessageComponentBuilder child in container.Components)
                    FilterQuoteComponent(child);
                break;
            // prevent buttons with custom id from triggering non-existent interactions
            case ButtonBuilder button:
                if (!string.IsNullOrEmpty(button.CustomId))
                    button.IsDisabled = true;
                break;
            case SelectMenuBuilder selectMenu:
                if (!string.IsNullOrEmpty(selectMenu.CustomId))
                    selectMenu.IsDisabled = true;
                break;
        }
        if (component is not IInteractableComponentBuilder interactable || string.IsNullOrEmpty(interactable.CustomId))
            return component;
        try {
            Span<byte> bytes = stackalloc byte[50];
            Random.Shared.NextBytes(bytes);
            interactable.CustomId = Convert.ToHexString(bytes);
            return component;
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to render component");
            return new TextDisplayBuilder("-# `❌ Failed to render component!`");
        }
    }

    private static TextDisplayBuilder RenderQuoteFooter(QuoteComponentData data) {
        StringBuilder footer = new();
        footer.Append("-# ");
        footer.Append(data.quoter);
        footer.Append("  •  ");
        footer.Append(data.createdAt);
        if (data.jumpUrl is not null) {
            footer.Append("  •  ");
            footer.Append($"[jump]({data.jumpUrl})");
        }
        return new TextDisplayBuilder(footer.ToString());
    }

    private async Task<Emote?> GetOrCreateIconEmote(string url) {
        try {
            IReadOnlyCollection<Emote> emotes = await client.GetApplicationEmotesAsync();
            string hash = Convert.ToHexString(Crc64.Hash(Encoding.Default.GetBytes(url)));
            string name = $"__embed_icon_{hash}";
            Emote? emote = emotes.FirstOrDefault(x => x.Name == name);
            if (emote is not null)
                return emote;

            byte[] image;
            using (HttpClient http = new()) {
                using NetVips.Image thumbnail = NetVips.Image.ThumbnailStream(
                    await http.GetStreamAsync(url),
                    width: 128, height: 128,
                    size: Enums.Size.Both,
                    failOn: Enums.FailOn.Error
                );
                using NetVips.Image thumbnailAlpha = thumbnail.AddAlpha();
                using NetVips.Image black = NetVips.Image.Black(128, 128);
                using NetVips.Image circle = black.Mutate(x => x.DrawCircle([255.0], 64, 64, 64, true));
                using NetVips.Image final = thumbnailAlpha.Boolean(circle, Enums.OperationBoolean.And);
                image = final.PngsaveBuffer();
            }

            // delete first emote if we're at the limit
            const int discordApplicationEmoteLimit = 2000;
            if (emotes.Count >= discordApplicationEmoteLimit) {
                Emote? toDelete = emotes.FirstOrDefault(x => x.Name.StartsWith("__embed_icon_"));
                if (toDelete is null)
                    return null;
                await client.DeleteApplicationEmoteAsync(toDelete.Id);
            }

            using MemoryStream stream = new(image);
            return await client.CreateApplicationEmoteAsync(name, new Image(stream));
        }
        catch (Exception ex) {
            Log.Error(ex, "Failed to get icon emote");
            return null;
        }
    }
}
