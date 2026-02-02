using System.Collections.Concurrent;
using System.Text;

using Discord;
using Discord.WebSocket;
using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;
using Serilog;
using Serilog.Events;
using Attachment = GeodeDiscord.Database.Entities.Attachment;

namespace GeodeDiscord;

public static class Util {
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

    public static Task<Quote> MessageToQuote(ApplicationDbContext db, ulong quoterId, int id, IMessage message,
        Quote? original = null) => MessageToQuote(db, quoterId, id, message, DateTimeOffset.Now, original);

    public static async Task<Quote> MessageToQuote(ApplicationDbContext db, ulong quoterId, int id, IMessage message,
        DateTimeOffset timestamp, Quote? original = null) {
        while (true) {
            // if we're just quoting a forwarded message, quote the forwarded message instead
            IMessage? forwarded = await GetForwardedAsync(message);
            if (forwarded is not null) {
                message = forwarded;
                continue;
            }

            Dictionary<string, Attachment> attachments = [];

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
                files = [..message.Attachments.Select(x => {
                    Attachment attachment = attachments.GetValueOrDefault(x.Url) ??
                        db.attachments.FirstOrDefault(y => y.url == x.Url) ??
                        new Attachment {
                            url = x.Url,
                            contentType = x.ContentType
                        };
                    attachments.TryAdd(attachment.url, attachment);
                    return attachment;
                })],
                embeds = [..message.Embeds.Select(x => {
                    Attachment attachment = attachments.GetValueOrDefault(x.Url) ??
                        db.attachments.FirstOrDefault(y => y.url == x.Url) ??
                        new Attachment {
                            url = x.Url,
                            contentType = null
                        };
                    attachments.TryAdd(attachment.url, attachment);
                    return attachment;
                })],
                content = message.Content,
                replyAuthorId = reply?.Author.Id ?? 0,
                replyMessageId = reply?.Id ?? 0,
                replyContent = reply?.Content ?? ""
            };
        }
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
