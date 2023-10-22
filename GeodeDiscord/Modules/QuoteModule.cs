using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

namespace GeodeDiscord.Modules;

[UsedImplicitly]
public class QuoteModule : InteractionModuleBase<SocketInteractionContext> {
    public required DiscordSocketClient client { get; set; }

    [MessageCommand("Add quote"), UsedImplicitly]
    public async Task AddQuote(IMessage message) {
        if (message.Author.Id == client.CurrentUser.Id) {
            await RespondAsync("❌ Cannot quote myself!", ephemeral: true);
            return;
        }
        IMessageChannel? channel = await Context.Interaction.GetChannelAsync();
        if (channel is not null) {
            IMessage? realMessage = await channel.GetMessageAsync(message.Id);
            if (realMessage is not null)
                message = realMessage;
        }
        Quote quote = await Util.MessageToQuote(Guid.NewGuid().ToString(), message);
        bool res = await TrySaveQuote(quote);
        if (!res) {
            await RespondAsync("❌ Failed to save quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Quote saved as **{quote.name}**!");
    }

    public static async Task<bool> TrySaveQuote(Quote quote) {
        // TODO
        return false;
    }
}
