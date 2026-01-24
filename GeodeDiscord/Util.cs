using System.Collections.Concurrent;
using System.Text;

using Discord;
using Discord.WebSocket;
using GeodeDiscord.Database.Entities;
using Serilog;
using Serilog.Events;

namespace GeodeDiscord;

public static class Util {
    private static List<string> GetMessageImages(IMessage message) => message.Attachments
        .Where(att => !att.IsSpoiler() && att.ContentType.StartsWith("image/", StringComparison.Ordinal))
        .Take(10)
        .Select(att => att.Url)
        .ToList();

    private static async Task<IMessage?> GetReplyAsync(IMessage message) {
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

            List<string> images = GetMessageImages(message);
            int extraAttachments = message.Attachments.Count - images.Count;
            ulong replyAuthorId = (await GetReplyAsync(message))?.Author.Id ?? 0;
            return new Quote {
                id = id,
                name = original?.name ?? "",
                messageId = message.Id,
                channelId = message.Channel?.Id ?? 0,
                createdAt = original?.createdAt ?? timestamp,
                lastEditedAt = timestamp,
                quoterId = quoterId,
                authorId = message.Author.Id,
                replyAuthorId = replyAuthorId,
                jumpUrl = message.Channel is null ? null : message.GetJumpUrl(),
                images = string.Join('|', images),
                extraAttachments = extraAttachments,
                content = message.Content
            };
        }
    }

    public static async IAsyncEnumerable<Embed> QuoteToEmbeds(DiscordSocketClient client, Quote quote) {
        IChannel? channel = await GetChannelAsync(client, quote.channelId);
        IUser? quoter = await GetUserAsync(client, quote.quoterId);

        StringBuilder description = new();
        description.AppendLine(quote.content);
        description.AppendLine();
        description.Append($"\\- <@{quote.authorId}>");
        description.Append($" in `#{channel?.Name ?? "<unknown>"}`");
        if (!string.IsNullOrWhiteSpace(quote.jumpUrl)) {
            description.Append($" [>>]({quote.jumpUrl})");
        }

        string[] images = quote.images.Split('|');

        StringBuilder footer = new();
        if (quote.extraAttachments != 0) {
            footer.Append($"{quote.extraAttachments.ToString("+0;-#")} attachment");
            if (quote.extraAttachments != 1)
                footer.Append('s');
            footer.AppendLine();
        }
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
        description.AppendLine(quote.content);
        description.AppendLine();
        description.AppendLine("\\- ????? in ????? by ?????");

        string[] images = quote.images.Split('|');

        StringBuilder footer = new();
        if (quote.extraAttachments != 0) {
            footer.Append($"{quote.extraAttachments.ToString("+0;-#")} attachment");
            if (quote.extraAttachments != 1)
                footer.Append('s');
        }

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
}
