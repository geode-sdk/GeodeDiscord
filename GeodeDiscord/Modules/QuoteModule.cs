using Discord;
using Discord.Interactions;

namespace GeodeDiscord.Modules;

public class QuoteModule : InteractionModuleBase<SocketInteractionContext> {
    public required InteractionService commands { get; set; }

    private InteractionHandler _handler;

    public QuoteModule(InteractionHandler handler) => _handler = handler;

    [MessageCommand("Quote")]
    public async Task Quote(IMessage message) {
        if (message.Author.IsBot || message.Author.IsWebhook) {
            await RespondAsync("Can't quote bots!", ephemeral: true);
            return;
        }
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.MessageToEmbeds(message).ToArray()
        );
    }
}
