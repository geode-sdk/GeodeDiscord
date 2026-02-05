using System.IO.Hashing;
using System.Text;
using Discord;
using Discord.WebSocket;
using GeodeDiscord.Database.Entities;
using NetVips;
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
                author = "?????",
                channel = "?????",
                quoter = "?????",
                createdAt = "?????",
                jumpUrl = null
            };
        }
    }

    private async IAsyncEnumerable<IMessageComponentBuilder> RenderQuote(QuoteComponentData data) {
        ContainerBuilder container = new ContainerBuilder()
            .WithAccentColor(new Color(0xf19060))
            .WithTextDisplay($"**{data.fullName}**");

        StringBuilder description = new();
        if (data.reply is not null) {
            string[] replyContent = data.reply.Value.content.Split(["\n", "\r\n"], StringSplitOptions.None);
            description.AppendLine($"> -# {data.reply.Value.author}: {replyContent[0]}");
            for (int i = 1; i < replyContent.Length; i++)
                description.AppendLine($"> -# {replyContent[i]}");
        }
        if (data.content is not null) {
            description.AppendLine(data.content);
        }
        if (description.Length > 0)
            container.WithTextDisplay(description.ToString());

        List<MediaGalleryItemProperties> gallery = data.attachments
            .Where(x => x.contentType is not null &&
                (x.contentType.StartsWith("image/") || x.contentType.StartsWith("video/")))
            .Select(x => new MediaGalleryItemProperties {
                Media = new UnfurledMediaItemProperties(x.url),
                Description = x.description,
                IsSpoiler = x.isSpoiler
            })
            .ToList();
        if (gallery.Count > 0) {
            container.WithMediaGallery(gallery);
        }

        List<Quote.Attachment> attachments = data.attachments
            .Where(x => x.contentType is null ||
                x.contentType.StartsWith("image/") && x.contentType.StartsWith("video/"))
            .ToList();
        StringBuilder attachmentsText = new();
        foreach (Quote.Attachment attachment in attachments) {
            if (attachment.isSpoiler)
                attachmentsText.Append("||");
            attachmentsText.Append($"`{attachment.name}`:");
            attachmentsText.Append($" `{FormatSize(attachment.size)}`");
            attachmentsText.Append($" [`download`]({attachment.url})");
            if (attachment.description is not null)
                attachmentsText.Append($" (`{attachment.description}`)");
            if (attachment.isSpoiler)
                attachmentsText.Append("||");
            attachmentsText.AppendLine();
        }
        if (attachmentsText.Length > 0) {
            container.WithTextDisplay(attachmentsText.ToString());
        }

        // TODO: combine images
        foreach (Quote.Embed embed in data.embeds) {
            container.WithSeparator();
            await foreach (IMessageComponentBuilder? component in RenderEmbed(embed)) {
                if (component is not null)
                    container.AddComponent(component);
            }
        }
        if (data.embeds.Count > 0) {
            container.WithSeparator();
        }

        container.WithTextDisplay($"\\- {data.author} in `#{data.channel}`");

        yield return container;

        StringBuilder footer = new();
        footer.Append("-# ");
        footer.Append(data.quoter);
        footer.Append("  •  ");
        footer.Append(data.createdAt);
        if (data.jumpUrl is not null) {
            footer.Append("  •  ");
            footer.Append($"[jump]({data.jumpUrl})");
        }
        yield return new TextDisplayBuilder(footer.ToString());
    }

    private async IAsyncEnumerable<IMessageComponentBuilder?> RenderEmbed(Quote.Embed embed) {
        switch (embed.type) {
            case EmbedType.Image:
                yield return new MediaGalleryBuilder([
                    new MediaGalleryItemProperties(new UnfurledMediaItemProperties(embed.url))
                ]);
                yield break;
            case EmbedType.Video or EmbedType.Gifv:
                yield return new MediaGalleryBuilder([
                    new MediaGalleryItemProperties(new UnfurledMediaItemProperties(embed.videoUrl))
                ]);
                yield break;
        }

        StringBuilder text = new();
        RenderEmbedProvider(embed, text);
        await RenderEmbedAuthor(embed, text);
        RenderEmbedTitle(embed, text);
        RenderEmbedDescription(embed, text);
        yield return RenderEmbedThumbnail(embed, text.Length > 0 ? new TextDisplayBuilder(text.ToString()) : null);
        // TODO: fields
        yield return RenderEmbedMedia(embed);
        yield return await RenderEmbedFooter(embed);
    }

    private static void RenderEmbedProvider(Quote.Embed embed, StringBuilder text) {
        if (embed.providerName is null)
            return;
        if (embed.providerUrl is null)
            text.AppendLine($"-# {embed.providerName}");
        else
            text.AppendLine($"-# [{embed.providerName}]({embed.providerUrl})");
    }

    private async Task RenderEmbedAuthor(Quote.Embed embed, StringBuilder text) {
        if (embed.authorIconUrl is not null) {
            Emote? emote = await GetOrCreateIconEmote(embed.authorIconUrl);
            if (emote is not null)
                text.Append($"<:{emote.Name}:{emote.Id}>");
            if (embed.authorName is not null)
                text.Append("  ");
        }
        if (embed.authorName is null)
            return;
        if (embed.authorUrl is null)
            text.AppendLine($"**{embed.authorName}**");
        else
            text.AppendLine($"[**{embed.authorName}**]({embed.authorUrl})");
    }

    private static void RenderEmbedTitle(Quote.Embed embed, StringBuilder text) {
        if (embed.title is null)
            return;
        if (embed.url is null)
            text.AppendLine($"### {embed.url}");
        else
            text.AppendLine($"### [{embed.title}]({embed.url})");
    }

    private static void RenderEmbedDescription(Quote.Embed embed, StringBuilder text) {
        if (embed.description is null)
            return;
        text.AppendLine(embed.description);
    }

    private static IMessageComponentBuilder? RenderEmbedThumbnail(Quote.Embed embed, TextDisplayBuilder? text) {
        if (embed.thumbnailUrl is null) {
            return text;
        }
        text ??= new TextDisplayBuilder(" ");
        return new SectionBuilder(
            new ThumbnailBuilder(new UnfurledMediaItemProperties(embed.thumbnailUrl)),
            [text]
        );
    }

    private static MediaGalleryBuilder? RenderEmbedMedia(Quote.Embed embed) {
        if (embed.videoUrl is not null) {
            return new MediaGalleryBuilder([
                new MediaGalleryItemProperties(new UnfurledMediaItemProperties(embed.videoUrl))
            ]);
        }
        if (embed.imageUrl is not null) {
            return new MediaGalleryBuilder([
                new MediaGalleryItemProperties(new UnfurledMediaItemProperties(embed.imageUrl))
            ]);
        }
        return null;
    }

    private async Task<TextDisplayBuilder?> RenderEmbedFooter(Quote.Embed embed) {
        StringBuilder footer = new();

        if (embed.footerIconUrl is not null) {
            Emote? emote = await GetOrCreateIconEmote(embed.footerIconUrl);
            if (emote is not null)
                footer.Append($"<:{emote.Name}:{emote.Id}>");
            if (embed.footerText is not null || embed.timestamp is not null)
                footer.Append("  ");
        }

        if (embed.footerText is not null) {
            footer.Append(embed.footerText);
            if (embed.timestamp is not null)
                footer.Append("  •  ");
        }

        if (embed.timestamp is not null) {
            footer.Append($"<t:{embed.timestamp.Value.ToUnixTimeSeconds()}:s>");
        }

        return footer.Length > 0 ? new TextDisplayBuilder(footer.ToString()) : null;
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

    // https://stackoverflow.com/a/4967106
    private static readonly string[] sizeSuffixes = [ "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB" ];
    private static string FormatSize(int size) {
        if (size == 0) {
            return $"{0:0.#} {sizeSuffixes[0]}";
        }

        int absSize = Math.Abs(size);
        int power = (int)Math.Log(absSize, 1024.0);
        int unit = power >= sizeSuffixes.Length ? sizeSuffixes.Length - 1 : power;
        double normSize = absSize / Math.Pow(1024, unit);

        return $"{(size < 0 ? "-" : null)}{normSize:0.#} {sizeSuffixes[unit]}";
    }
}
