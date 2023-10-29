using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Serilog;

namespace GeodeDiscord;

public class InteractionHandler {
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _handler;
    private readonly IServiceProvider _services;

    public InteractionHandler(DiscordSocketClient client, InteractionService handler, IServiceProvider services) {
        _client = client;
        _handler = handler;
        _services = services;
    }

    public async Task InitializeAsync() {
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteraction;

        _handler.Log += log => {
            Log.Write(Util.DiscordToSerilogLevel(log.Severity), log.Exception, "[{Source}] {Message}", log.Source,
                log.Message);
            return Task.CompletedTask;
        };

        await _handler.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    private async Task ReadyAsync() {
        if (Environment.GetEnvironmentVariable("DISCORD_TEST_GUILD") is null)
            await _handler.RegisterCommandsGloballyAsync();
        else
            await _handler.RegisterCommandsToGuildAsync(
                ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_TEST_GUILD") ?? "0"));
    }

    private async Task HandleInteraction(SocketInteraction interaction) {
        try {
            SocketInteractionContext context = new(_client, interaction);
            IResult? result = await _handler.ExecuteCommandAsync(context, _services);
            if (result.IsSuccess)
                return;
            Log.Error("Error handling interaction: {Error}. {Reason}", result.Error, result.ErrorReason);
            switch (result.Error) {
                case InteractionCommandError.UnmetPrecondition:
                    await interaction.RespondAsync($"❌ Unmet precondition! ({result.ErrorReason})");
                    break;
                default:
                    await interaction.RespondAsync($"❌ Unknown error! ({result.ErrorReason})");
                    break;
            }
        }
        catch {
            if (interaction.Type is InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync().ContinueWith(async msg => await msg.Result.DeleteAsync());
        }
    }
}
