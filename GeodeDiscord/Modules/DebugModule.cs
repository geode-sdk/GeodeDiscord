using System.Globalization;
using System.Text;

using Discord;
using Discord.Interactions;

using JetBrains.Annotations;

using Serilog;
using Serilog.Events;

namespace GeodeDiscord.Modules;

[Group("debug", "Debug."), DefaultMemberPermissions(GuildPermission.Administrator)]
public class DebugModule : InteractionModuleBase<SocketInteractionContext> {
#pragma warning disable CA1822
    [SlashCommand("uncaught-exception", "Throws an exception and doesn't catch it."),
     UsedImplicitly]
    public Task UncaughtException() => throw new InvalidOperationException("hi");
#pragma warning restore CA1822

    [SlashCommand("exception", "Throws an exception, catches and logs it."),
     UsedImplicitly]
    public async Task ExceptionCmd() {
        try { throw new InvalidOperationException("hiiiii~~ !!!!!!!!!! :3333"); }
        catch (Exception ex) {
            Log.Error(ex, "test exception");
            await RespondAsync("❌ yes, boss~ uwu");
        }
    }

    [SlashCommand("log", "Retrieves the last X log messages."),
     UsedImplicitly]
    public async Task LogCmd([Summary(null, "Amount of messages to retrieve.")] uint count = 1) {
        int start = count == 0 ? 0 : Math.Clamp(Program.log.Count - (int)count, 0, Program.log.Count);
        StringBuilder text = new();
        text.AppendLine($"🗒️ Showing last {Program.log.Count - start} log messages:");
        text.AppendLine("```");
        for (int i = start; i < Program.log.Count; i++) {
            LogEvent log = Program.log[i];
            text.AppendLine(log.RenderMessage(CultureInfo.InvariantCulture));
            if (log.Exception is not null)
                text.AppendLine(log.Exception.ToString());
        }
        text.Append("```");
        await RespondAsync(text.ToString());
    }
}
