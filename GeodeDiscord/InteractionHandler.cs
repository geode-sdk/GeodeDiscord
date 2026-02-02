using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Serilog;

namespace GeodeDiscord;

public class InteractionHandler(DiscordSocketClient client, InteractionService handler, IServiceProvider services) {
    public async Task InitializeAsync() {
        client.Ready += ReadyAsync;
        client.InteractionCreated += HandleInteraction;

        handler.Log += log => {
            Log.Write(Util.DiscordToSerilogLevel(log.Severity), log.Exception, "[{Source}] {Message}", log.Source,
                log.Message);
            return Task.CompletedTask;
        };

        await handler.AddModulesAsync(Assembly.GetEntryAssembly(), services);
    }

    private async Task ReadyAsync() {
#if DEBUG
        Log.Information("Clearing global commands");
        await handler.RestClient.BulkOverwriteGlobalCommands([]);

        Log.Information("Registering guild commands");
        foreach (SocketGuild guild in client.Guilds)
            await handler.RegisterCommandsToGuildAsync(guild.Id);

        await client.SetActivityAsync(new CustomStatusGame("being debugged rn yay"));
#else
        Log.Information("Clearing guild commands");
        foreach (SocketGuild guild in client.Guilds)
            await handler.RestClient.BulkOverwriteGuildCommands([], guild.Id);

        Log.Information("Registering global commands");
        await handler.RegisterCommandsGloballyAsync();
#endif
    }

    private async Task HandleInteraction(SocketInteraction interaction) {
        try {
            SocketInteractionContext context = new(client, interaction);
            IResult? result = await handler.ExecuteCommandAsync(context, services);
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
        catch (Exception ex) {
            Log.Error(ex, "Error handling interaction");
            if (interaction.Type is InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync().ContinueWith(async msg => await msg.Result.DeleteAsync());
        }
    }

    public async Task TeardownAsync() {
        Log.Information("Clearing guild commands");
        foreach (SocketGuild guild in client.Guilds)
            await handler.RestClient.BulkOverwriteGuildCommands([], guild.Id);
    }
}
