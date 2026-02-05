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

    public static async Task<SocketInteraction> WaitForInteractionAsync(BaseSocketClient client,
        Predicate<SocketInteraction> predicate) {
        TaskCompletionSource<SocketInteraction> tcs = new();

        client.InteractionCreated += HandleInteraction;
        SocketInteraction result = await tcs.Task.ConfigureAwait(false);
        client.InteractionCreated -= HandleInteraction;

        return result;

        Task HandleInteraction(SocketInteraction interaction) {
            if (predicate(interaction)) {
                tcs.SetResult(interaction);
            }
            return Task.CompletedTask;
        }
    }
}
