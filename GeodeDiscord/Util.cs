using System.Collections.Concurrent;
using System.Text;

using Discord;
using Discord.WebSocket;
using Serilog;
using Serilog.Events;

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
        try { user = await client.GetUserAsync(id); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to get user {Id}", id);
            return null;
        }
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
        try { channel = await client.GetChannelAsync(id); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to get channel {Id}", id);
            return null;
        }
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

    // https://stackoverflow.com/a/4967106
    private static readonly string[] sizeSuffixes = [ "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB" ];
    public static string FormatSize(int size) {
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
