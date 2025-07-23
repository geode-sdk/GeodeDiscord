﻿using System.Reflection;

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
        ulong testGuild = ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_TEST_GUILD") ?? "0");
        if (testGuild != 0)
            await handler.RegisterCommandsToGuildAsync(testGuild);
        await client.SetActivityAsync(new CustomStatusGame("being debugged rn yay"));
#else
        foreach (SocketGuild guild in client.Guilds)
            await handler.RestClient.BulkOverwriteGuildCommands([], guild.Id);
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
        catch {
            if (interaction.Type is InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync().ContinueWith(async msg => await msg.Result.DeleteAsync());
        }
    }
}
