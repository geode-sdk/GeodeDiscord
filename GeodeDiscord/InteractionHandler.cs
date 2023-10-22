using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Microsoft.Extensions.DependencyInjection;

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
        _client.ReactionAdded += HandleReaction;

        _handler.Log += log => {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        };

        await _handler.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    private async Task ReadyAsync() {
#if DEBUG
        await _handler.RegisterCommandsToGuildAsync(504366353965121587);
#else
        await _handler.RegisterCommandsGloballyAsync();
#endif
    }

    private async Task HandleInteraction(SocketInteraction interaction) {
        try {
            SocketInteractionContext context = new(_client, interaction);
            IResult? result = await _handler.ExecuteCommandAsync(context, _services);
            if (!result.IsSuccess) {
                switch (result.Error) {
                    case InteractionCommandError.UnmetPrecondition:
                        // TODO
                        break;
                }
            }
        }
        catch {
            if (interaction.Type is InteractionType.ApplicationCommand)
                await interaction.GetOriginalResponseAsync().ContinueWith(async msg => await msg.Result.DeleteAsync());
        }
    }

    private async Task HandleReaction(Cacheable<IUserMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction) {
        DiscordSocketClient client = _services.GetRequiredService<DiscordSocketClient>();
        if (reaction.UserId == client.CurrentUser.Id)
            return;

        // 💬
        if (reaction.Emote.Name != "\ud83d\udcac")
            return;

        IUserMessage message = await cachedMessage.GetOrDownloadAsync();
        if (message.Author.IsBot || message.Author.IsWebhook)
            return;

        IMessageChannel channel = await cachedChannel.GetOrDownloadAsync();
        await channel.SendMessageAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.MessageToEmbeds(message).ToArray()
        );
    }
}
