using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;
using GeodeDiscord.Modules;

namespace GeodeDiscord;

public class InteractionHandler {
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _handler;
    private readonly IServiceProvider _services;
    private readonly ApplicationDbContext _db;

    public InteractionHandler(DiscordSocketClient client, InteractionService handler, IServiceProvider services,
        ApplicationDbContext db) {
        _client = client;
        _handler = handler;
        _services = services;
        _db = db;
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
        await _handler.RegisterCommandsToGuildAsync(
            ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_TEST_GUILD") ?? "0"));
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
        if (reaction.UserId == _client.CurrentUser.Id)
            return;

        // 💬
        if (reaction.Emote.Name != "\ud83d\udcac")
            return;

        IUserMessage? message = await cachedMessage.GetOrDownloadAsync();
        IMessageChannel? channel = await cachedChannel.GetOrDownloadAsync();
        if (message is null || channel is null)
            return;

        if (message.Author.Id == _client.CurrentUser.Id) {
            await channel.SendMessageAsync($"<@{reaction.UserId}>: ❌ Cannot quote myself!");
            return;
        }

        Quote quote = await Util.MessageToQuote(Guid.NewGuid().ToString(), message);
        bool res = QuoteModule.TrySaveQuote(_db, quote);
        if (!res) {
            await channel.SendMessageAsync($"<@{reaction.UserId}>: ❌ Failed to save quote!");
            return;
        }
        await channel.SendMessageAsync($"Quote saved as **{quote.name}**!");
    }
}
