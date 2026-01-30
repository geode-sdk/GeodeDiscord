using System.Collections.Concurrent;
using System.Text;

using Discord;
using Discord.WebSocket;
using GeodeDiscord.Database.Entities;
using Serilog;
using Serilog.Events;

namespace GeodeDiscord;

public static class Util {
    private static List<IAttachment> GetEmbeddableAttachments(IMessage message) => message.Attachments
        .Where(att => !att.IsSpoiler() &&
            (att.ContentType.StartsWith("image/", StringComparison.Ordinal) ||
                att.ContentType.StartsWith("video/", StringComparison.Ordinal)))
        .Take(10)
        .ToList();

    public static async Task<IMessage?> GetReplyAsync(IMessage message) {
        if (message.Channel is null || message.Reference is null ||
            message.Reference.ChannelId != message.Channel.Id ||
            !message.Reference.MessageId.IsSpecified)
            return null;
        ulong refMessageId = message.Reference.MessageId.Value;
        IMessage? refMessage = await message.Channel.GetMessageAsync(refMessageId);
        return refMessage ?? null;
    }

    private static async Task<IMessage?> GetForwardedAsync(IMessage message) {
        if (message.Channel is null || message.Reference is null ||
            message.Reference.ChannelId != message.Channel.Id ||
            !message.Reference.MessageId.IsSpecified ||
            message.Reference.ReferenceType.GetValueOrDefault() != MessageReferenceType.Forward)
            return null;
        ulong refMessageId = message.Reference.MessageId.Value;
        IMessage? refMessage = await message.Channel.GetMessageAsync(refMessageId);
        return refMessage ?? null;
    }

    public static Task<Quote> MessageToQuote(ulong quoterId, int id, IMessage message, Quote? original = null) =>
        MessageToQuote(quoterId, id, message, DateTimeOffset.Now, original);

    public static async Task<Quote> MessageToQuote(ulong quoterId, int id, IMessage message,
        DateTimeOffset timestamp, Quote? original = null) {
        while (true) {
            // if we're just quoting a forwarded message, quote the forwarded message instead
            IMessage? forwarded = await GetForwardedAsync(message);
            if (forwarded is not null) {
                message = forwarded;
                continue;
            }

            List<IAttachment> attachments = GetEmbeddableAttachments(message);
            int extraAttachments = message.Attachments.Count - attachments.Count;
            IMessage? reply = await GetReplyAsync(message);
            return new Quote {
                id = id,
                name = original?.name ?? "",
                messageId = message.Id,
                channelId = message.Channel?.Id ?? 0,
                createdAt = original?.createdAt ?? timestamp,
                lastEditedAt = timestamp,
                quoterId = quoterId,
                authorId = message.Author.Id,
                jumpUrl = message.Channel is null ? null : message.GetJumpUrl(),
                images = string.Join('|', attachments.Where(x => x.ContentType.StartsWith("image/", StringComparison.Ordinal)).Select(x => x.Url)),
                videos = string.Join('|', attachments.Where(x => x.ContentType.StartsWith("video/", StringComparison.Ordinal)).Select(x => x.Url)),
                extraAttachments = extraAttachments,
                content = message.Content,
                replyAuthorId = reply?.Author.Id ?? 0,
                replyMessageId = reply?.Id ?? 0,
                replyContent = reply?.Content ?? ""
            };
        }
    }

    public static async IAsyncEnumerable<Embed> QuoteToEmbeds(DiscordSocketClient client, Quote quote) {
        IChannel? channel = await GetChannelAsync(client, quote.channelId);
        IUser? quoter = await GetUserAsync(client, quote.quoterId);

        StringBuilder description = new();
        if (quote.replyAuthorId != 0 && quote.replyMessageId != 0 && !string.IsNullOrEmpty(quote.replyContent)) {
            string[] replyContent = quote.replyContent.Split(["\n", "\r\n"], StringSplitOptions.None);
            description.AppendLine($"> <@{quote.replyAuthorId}>: {replyContent[0]}");
            for (int i = 1; i < replyContent.Length; i++)
                description.AppendLine($"> {replyContent[i]}");
        }
        description.AppendLine(quote.content);
        description.AppendLine();
        description.Append($"\\- <@{quote.authorId}>");
        description.Append($" in `#{channel?.Name ?? "<unknown>"}`");
        if (!string.IsNullOrWhiteSpace(quote.jumpUrl)) {
            description.Append($" [>>]({quote.jumpUrl})");
        }

        string[] images = quote.images.Split('|');

        List<string> extraAttachments = [];
        if (!string.IsNullOrEmpty(quote.videos)) {
            string[] videos = quote.videos.Split('|');
            extraAttachments.Add($"{videos.Length:+0;-#} video{(videos.Length != 1 ? "s" : "")}");
        }
        if (quote.extraAttachments != 0) {
            extraAttachments.Add($"{quote.extraAttachments:+0;-#} attachment{(quote.extraAttachments != 1 ? "s" : "")}");
        }

        StringBuilder footer = new();
        if (extraAttachments.Count != 0)
            footer.AppendLine(string.Join(", ", extraAttachments));
        footer.Append(quoter?.GlobalName ?? "<unknown>");

        yield return new EmbedBuilder()
            .WithAuthor(quote.GetFullName())
            .WithDescription(description.ToString())
            .WithImageUrl(images.Length > 0 ? images[0] : null)
            .WithTimestamp(quote.createdAt)
            .WithFooter(footer.ToString())
            .Build();

        foreach (string image in images.Skip(1))
            yield return new EmbedBuilder()
                .WithImageUrl(image)
                .Build();
    }

    public static MessageReference QuoteToForward(Quote quote) => new(
        quote.messageId, quote.channelId, null, false, MessageReferenceType.Forward
    );

    public static IEnumerable<Embed> QuoteToCensoredEmbeds(Quote quote) {
        StringBuilder description = new();
        if (quote.replyAuthorId != 0 && quote.replyMessageId != 0 && !string.IsNullOrEmpty(quote.replyContent)) {
            string[] replyContent = quote.replyContent.Split(["\n", "\r\n"], StringSplitOptions.None);
            description.AppendLine($"> ?????: {replyContent[0]}");
            for (int i = 1; i < replyContent.Length; i++)
                description.AppendLine($"> {replyContent[i]}");
        }
        description.AppendLine(quote.content);
        description.AppendLine();
        description.AppendLine("\\- ????? in `#?????`");

        string[] images = quote.images.Split('|');

        StringBuilder footer = new();
        if (quote.extraAttachments != 0) {
            footer.Append($"{quote.extraAttachments.ToString("+0;-#")} attachment");
            if (quote.extraAttachments != 1)
                footer.Append('s');
            footer.AppendLine();
        }
        footer.Append("?????");

        yield return new EmbedBuilder()
            .WithAuthor("?????")
            .WithDescription(description.ToString())
            .WithImageUrl(images.Length > 0 ? images[0] : null)
            .WithTimestamp(DateTimeOffset.FromUnixTimeSeconds(694201337))
            .WithFooter(footer.ToString())
            .Build();

        foreach (string image in images.Skip(1))
            yield return new EmbedBuilder()
                .WithImageUrl(image)
                .Build();
    }

    public static LogEventLevel DiscordToSerilogLevel(LogSeverity x) => x switch {
        LogSeverity.Critical => LogEventLevel.Fatal,
        LogSeverity.Error => LogEventLevel.Error,
        LogSeverity.Warning => LogEventLevel.Warning,
        LogSeverity.Info => LogEventLevel.Information,
        LogSeverity.Verbose => LogEventLevel.Verbose,
        LogSeverity.Debug => LogEventLevel.Debug,
        _ => LogEventLevel.Information
    };

    private static readonly ConcurrentDictionary<ulong, IUser?> extraUserCache = [];
    public static async Task<IUser?> GetUserAsync(DiscordSocketClient client, ulong id) {
        if (id == 0)
            return null;
        IUser? user = client.GetUser(id);
        if (user is not null || extraUserCache.TryGetValue(id, out user))
            return user;
        user = await client.GetUserAsync(id);
        extraUserCache[id] = user;
        Log.Information("Added user {User} ({Username}, {Id}) to extra cache", user?.GlobalName, user?.Username, id);
        return user;
    }

    private static readonly ConcurrentDictionary<ulong, IChannel?> extraChannelCache = [];
    public static async Task<IChannel?> GetChannelAsync(DiscordSocketClient client, ulong id) {
        if (id == 0)
            return null;
        IChannel? channel = client.GetChannel(id);
        if (channel is not null || extraChannelCache.TryGetValue(id, out channel))
            return channel;
        channel = await client.GetChannelAsync(id);
        extraChannelCache[id] = channel;
        Log.Information("Added channel {Channel} ({Id}) to extra cache", channel?.Name, id);
        return channel;
    }

    public static string FormatTimeSpan(TimeSpan span) {
        StringBuilder str = new();
        bool negative = span.Ticks < 0;
        span = span.Duration();
        if (negative) {
            str.Append('-');
        }
        if (span.Days > 0) {
            str.Append(span.Days);
            str.Append("d ");
        }
        if (span.Days > 0 || span.Hours > 0) {
            str.Append(span.Hours);
            str.Append("h ");
        }
        if (span.Days > 0 || span.Hours > 0 || span.Minutes > 0) {
            str.Append(span.Minutes);
            str.Append("m ");
        }
        str.Append($"{span.TotalSeconds - Math.Truncate(span.TotalSeconds):F1}");
        str.Append('s');
        return str.ToString();
    }
}
