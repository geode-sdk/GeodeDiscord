using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GeodeDiscord;

public class InteractionHandler(DiscordSocketClient client, InteractionService handler, IServiceProvider services) {
    public async Task InitializeAsync() {
        client.Ready += ReadyAsync;
        client.InteractionCreated += interaction => {
            Task.Run(async () => await HandleInteraction(interaction).ConfigureAwait(false));
            return Task.CompletedTask;
        };

        handler.Log += log => {
            // ignore interaction exceptions, handle them manually in the interaction handler
            // discord.net *really* wants to try and handle them itself
            // so i have to do a bunch of hacks to get around it
            //if (log.Exception is InteractionException)
            //    return Task.CompletedTask;
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
            IResult? result;
            await using (AsyncServiceScope scope = services.CreateAsyncScope()) {
                scope.ServiceProvider.GetRequiredService<InteractionProvider>().interaction = interaction;
                SocketInteractionContext context = scope.ServiceProvider.GetRequiredService<SocketInteractionContext>();
                result = await handler.ExecuteCommandAsync(context, scope.ServiceProvider);
            }
            if (result.IsSuccess)
                return;
            Exception? exception = null;
            if (result.Error == InteractionCommandError.Exception && result is ExecuteResult executeResult) {
                exception = executeResult.Exception;
                while (exception is not null &&
                    exception is not MessageErrorException &&
                    exception is not DbUpdateException &&
                    exception is not DbUpdateConcurrencyException)
                    exception = exception.InnerException;
                switch (exception) {
                    case MessageErrorException messageError:
                        await InteractionError(messageError.Message);
                        return;
                    case DbUpdateException or DbUpdateConcurrencyException:
                        Log.Error(executeResult.Exception, "Failed to save database changes");
                        await InteractionError("Failed to save database changes!");
                        return;
                    default:
                        exception = executeResult.Exception;
                        break;
                }
            }
            Log.Error(exception, "Error handling interaction: {Error}. {Reason}", result.Error, result.ErrorReason);
            switch (result.Error) {
                case InteractionCommandError.UnmetPrecondition:
                    await InteractionError($"Unmet precondition! ({result.ErrorReason})");
                    break;
                default:
                    await InteractionError($"Unknown error! ({result.ErrorReason})");
                    break;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error handling interaction");
            if (interaction.Type is InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync().ContinueWith(async msg => await msg.Result.DeleteAsync());
        }
        return;

        Task InteractionError(string message) => interaction.HasResponded ?
            interaction.FollowupAsync($"❌ {message}", ephemeral: true) :
            interaction.RespondAsync($"❌ {message}", ephemeral: true);
    }

    public async Task TeardownAsync() {
        Log.Information("Clearing guild commands");
        foreach (SocketGuild guild in client.Guilds)
            await handler.RestClient.BulkOverwriteGuildCommands([], guild.Id);
    }
}
