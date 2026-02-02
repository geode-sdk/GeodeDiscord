using System.Collections.Specialized;
using System.Text;
using System.Web;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;
using Serilog;
using Attachment = GeodeDiscord.Database.Entities.Attachment;

namespace GeodeDiscord;

public class QuoteRenderer(ApplicationDbContext db, SocketInteractionContext context) {
    public delegate Task<IUserMessage> EmbedRenderer(Embed[] embeds, IEnumerable<FileAttachment> attachments);
    private delegate Task<IUserMessage> FollowupRenderer(string text, MessageReference reply);

    public async Task<List<IUserMessage>> Render(Quote quote, EmbedRenderer renderer) =>
        await Render(await PrepareRender(quote), renderer);
    public async Task<List<IUserMessage>> Render(Quote quote, EmbedRenderer renderer, List<IUserMessage> previous) =>
        await Render(await PrepareRender(quote), renderer, previous);

    public async Task<List<IUserMessage>> RenderCensored(Quote quote, EmbedRenderer renderer) =>
        await Render(PrepareRenderCensored(quote), renderer);
    public async Task<List<IUserMessage>> RenderCensored(Quote quote, EmbedRenderer renderer, List<IUserMessage> previous) =>
        await Render(PrepareRenderCensored(quote), renderer, previous);

    private async Task<QuoteRenderData> PrepareRender(Quote quote) {
        IChannel? channel = await Util.GetChannelAsync(context.Client, quote.channelId);
        IUser? quoter = await Util.GetUserAsync(context.Client, quote.quoterId);

        bool hasReply = quote.replyAuthorId != 0 && quote.replyMessageId != 0 &&
            !string.IsNullOrEmpty(quote.replyContent);
        bool hasContent = !string.IsNullOrWhiteSpace(quote.content);
        bool hasJumpUrl = !string.IsNullOrWhiteSpace(quote.jumpUrl);

        return new QuoteRenderData {
            fullName = quote.GetFullName(),
            reply = hasReply ? new QuoteRenderData.Reply($"<@{quote.replyAuthorId}>", quote.replyContent) : null,
            content = hasContent ? quote.content : null,
            author = $"<@{quote.authorId}>",
            channel = $"`#{channel?.Name ?? "<unknown>"}`",
            jumpUrl = hasJumpUrl ? $"[>>]({quote.jumpUrl})" : null,
            files = quote.files,
            embeds = quote.embeds,
            quoter = quoter?.GlobalName ?? "<unknown>",
            createdAt = quote.createdAt
        };
    }

    private static QuoteRenderData PrepareRenderCensored(Quote quote) {
        bool hasReply = quote.replyAuthorId != 0 && quote.replyMessageId != 0 &&
            !string.IsNullOrEmpty(quote.replyContent);
        bool hasContent = !string.IsNullOrWhiteSpace(quote.content);

        return new QuoteRenderData {
            fullName = "?????",
            reply = hasReply ? new QuoteRenderData.Reply("?????", quote.replyContent) : null,
            content = hasContent ? quote.content : null,
            author = "?????",
            channel = "`#?????`",
            jumpUrl = null,
            files = quote.files,
            embeds = quote.embeds,
            quoter = "?????",
            createdAt = DateTimeOffset.FromUnixTimeSeconds(694201337)
        };
    }

    private Task<List<IUserMessage>> Render(QuoteRenderData data, EmbedRenderer renderer) => Render(
        data, renderer, async (text, reply) => await context.Channel.SendMessageAsync(
            text,
            allowedMentions: AllowedMentions.None,
            messageReference: reply
        )
    );

    private async Task<List<IUserMessage>> Render(QuoteRenderData data, EmbedRenderer renderer,
        List<IUserMessage> previous) {
        using List<IUserMessage>.Enumerator messages = previous.GetEnumerator();
        messages.MoveNext(); // guaranteed to have at least one element
        List<IUserMessage> newMessages = await Render(
            data, renderer, async (text, reply) => {
                if (!messages.MoveNext()) {
                    return await context.Channel.SendMessageAsync(
                        text,
                        allowedMentions: AllowedMentions.None,
                        messageReference: reply
                    );
                }
                await ModifyMessageAsync(messages.Current, msg => {
                    msg.Content = text;
                });
                return messages.Current;
            });
        while (messages.MoveNext()) {
            await messages.Current.DeleteAsync();
        }
        return newMessages;
    }

    private async Task<List<IUserMessage>> Render(QuoteRenderData data, EmbedRenderer embedRenderer,
        FollowupRenderer followupRenderer) {
        StringBuilder description = new();
        bool hasContent = false;
        if (data.reply is not null) {
            string[] replyContent = data.reply.Value.content.Split(["\n", "\r\n"], StringSplitOptions.None);
            description.AppendLine($"> {data.reply.Value.author}: {replyContent[0]}");
            for (int i = 1; i < replyContent.Length; i++)
                description.AppendLine($"> {replyContent[i]}");
            hasContent = true;
        }
        if (data.content is not null) {
            description.AppendLine(data.content);
            hasContent = true;
        }
        if (hasContent) {
            description.AppendLine();
        }
        description.Append($"\\- {data.author}");
        description.Append($" in {data.channel}");
        if (data.jumpUrl is not null) {
            description.Append($" {data.jumpUrl}");
        }

        List<(Attachment attachment, MimeTypeType mediaTypeType)> files = data.files
            .Select(x => (
                x,
                x.contentType is null ? MimeTypeType.None :
                x.contentType.StartsWith("image/") ? MimeTypeType.Image :
                x.contentType.StartsWith("video/") ? MimeTypeType.Video :
                MimeTypeType.Other
            ))
            .ToList();

        List<string> gallery = [];
        List<FileAttachment> galleryUploads = [];
        Dictionary<string, (Attachment, string)> attachmentsToUpdate = [];

        using (HttpClient http = new()) {
            IEnumerable<(Attachment, MimeTypeType)> galleryFiles = files
                .Where(x => x.mediaTypeType is MimeTypeType.Image or MimeTypeType.Video);
            foreach ((Attachment attachment, MimeTypeType mediaTypeType) in galleryFiles) {
                if (mediaTypeType == MimeTypeType.Image) {
                    gallery.Add(attachment.url);
                    continue;
                }

                string thumbnailName = $"thumbnail{gallery.Count}.webp";
                gallery.Add($"attachment://{thumbnailName}");
                try {
                    // TODO: uncomment after testing
                    //HttpResponseMessage response = await http.GetAsync(GetThumbnailUrl(attachment.url));
                    //if (response.IsSuccessStatusCode) {
                    //    Stream stream = await response.Content.ReadAsStreamAsync();
                    //    galleryUploads.Add(new FileAttachment(stream, thumbnailName));
                    //}
                    //else {
                        attachmentsToUpdate.Add(attachment.url, (attachment, thumbnailName));
                    //}
                }
                catch (Exception ex) {
                    Log.Warning(ex, "Failed to fetch video thumbnail from Discord");
                }
            }
        }

        EmbedBuilder[] embeds = new EmbedBuilder[1 + Math.Max(0, gallery.Count - 1)];

        for (int i = 0; i < embeds.Length; i++)
            embeds[i] = new EmbedBuilder();

        string[][] chunkedGallery = gallery.Chunk(4).ToArray(); // can only have 4 images per embed
        for (int i = 0; i < chunkedGallery.Length; i++) {
            for (int j = 0; j < chunkedGallery[i].Length; j++) {
                // have to set a url for the gallery thing to work
                embeds[i * 4 + j] = embeds[i * 4 + j]
                    .WithUrl($"https://geode-sdk.org/#gallery-chunk-{i}")
                    .WithImageUrl(chunkedGallery[i][j]);
            }
        }

        embeds[0] = embeds[0]
            .WithAuthor(data.fullName)
            .WithDescription(description.ToString())
            .WithFooter(data.quoter)
            .WithTimestamp(data.createdAt);

        Embed[] finalEmbeds = embeds.Select(x => x.Build()).ToArray();

        List<IUserMessage> messages = [];

        IUserMessage quoteMsg = await embedRenderer(finalEmbeds, galleryUploads);
        messages.Add(quoteMsg);

        bool dbNeedsSave = false;
        List<FileAttachment> updatedUploads = [];

        IEnumerable<string> embedLinks = files
            .Where(x => x.mediaTypeType == MimeTypeType.Video)
            .Select(x => x.attachment.url)
            .Concat(
                files
                    .Where(x => x.mediaTypeType is not MimeTypeType.Video and not MimeTypeType.Image)
                    .Select(x => x.attachment.url)
            )
            .Concat(data.embeds.Select(x => x.url))
            .Chunk(5) // can only have 5 link embeds per message
            .Select(x => string.Join('\n', x));

        foreach (string text in embedLinks) {
            IUserMessage followup = await followupRenderer(text, new MessageReference(quoteMsg.Id));
            messages.Add(followup);

            if (attachmentsToUpdate.Count == 0)
                continue;
            using HttpClient http = new();
            foreach (IEmbed embed in followup.Embeds) {
                if (embed.Video is null)
                    continue;
                if (!attachmentsToUpdate.TryGetValue(embed.Url, out (Attachment, string) toUpdate))
                    continue;
                (Attachment attachment, string thumbnailName) = toUpdate;

                try {
                    Uri thumbnail = embed.Thumbnail.HasValue ?
                        new Uri(embed.Thumbnail.Value.Url) :
                        GetThumbnailUrl(embed.Video.Value.Url);
                    HttpResponseMessage response = await http.GetAsync(thumbnail);
                    if (response.IsSuccessStatusCode) {
                        Stream stream = await response.Content.ReadAsStreamAsync();
                        updatedUploads.Add(new FileAttachment(stream, thumbnailName));
                    }
                    else {
                        Log.Warning(
                            "Failed to update video thumbnail: {Status} {Reason}",
                            response.StatusCode, response.ReasonPhrase
                        );
                    }
                }
                catch (Exception ex) {
                    Log.Warning(ex, "Failed to fetch video thumbnail from Discord");
                }

                db.Remove(attachment);
                db.Update(attachment with { url = embed.Video.Value.Url });
                dbNeedsSave = true;
            }
        }

        await ModifyMessageAsync(quoteMsg, msg => {
            msg.Attachments = new Optional<IEnumerable<FileAttachment>>(galleryUploads.Concat(updatedUploads));
            msg.Embeds = finalEmbeds;
        });

        if (dbNeedsSave) {
            try { await db.SaveChangesAsync(); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to save updated attachments");
            }
        }

        return messages;
    }

    private static async Task ModifyMessageAsync(IUserMessage message, Action<MessageProperties> func,
        RequestOptions? options = null) {
        // RestInteractionMessage hides IUserMessage.ModifyAsync :/
        if (message is RestInteractionMessage interactionMsg) {
            await interactionMsg.ModifyAsync(func, options);
        }
        else {
            await message.ModifyAsync(func, options);
        }
    }

    private static Uri GetThumbnailUrl(string url) {
        Uri thumbnail = new(url);
        NameValueCollection query = HttpUtility.ParseQueryString(thumbnail.Query);
        query.Add("format", "webp");
        return new UriBuilder(thumbnail) {
            Host = "media.discordapp.net",
            Query = string.Join('&', query.AllKeys.Select(key => $"{key}={query[key]}"))
        }.Uri;
    }

    private enum MimeTypeType { None, Image, Video, Other }

    private readonly record struct QuoteRenderData(
        string fullName,
        QuoteRenderData.Reply? reply,
        string? content,
        string author,
        string channel,
        string? jumpUrl,
        ICollection<Attachment> files,
        ICollection<Attachment> embeds,
        string quoter,
        DateTimeOffset createdAt
    ) {
        public readonly record struct Reply(string author, string content);
    }
}
