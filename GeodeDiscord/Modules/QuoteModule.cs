using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Modules;

[UsedImplicitly]
public class QuoteModule : InteractionModuleBase<SocketInteractionContext> {
    private readonly DiscordSocketClient _client;
    private readonly ApplicationDbContext _db;

    public QuoteModule(DiscordSocketClient client, ApplicationDbContext db) {
        _client = client;
        _db = db;
    }

    [MessageCommand("Add quote"), UsedImplicitly]
    public async Task AddQuote(IMessage message) {
        if (message.Author.Id == _client.CurrentUser.Id) {
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
        bool res = TrySaveQuote(_db, quote);
        if (!res) {
            await RespondAsync("❌ Failed to save quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Quote saved as **{quote.name}**!");
    }

    [SlashCommand("quote", "Gets a random quote.")]
    public async Task GetRandomQuote() {
        if (!_db.quotes.Any()) {
            await RespondAsync("❌ There no quotes yet!", ephemeral: true);
            return;
        }
        Quote quote = _db.quotes.OrderBy(_ => EF.Functions.Random()).First();
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: await Util.QuoteToEmbeds(Context.Guild, quote).ToArrayAsync()
        );
    }

    public static bool TrySaveQuote(ApplicationDbContext db, Quote quote) {
        try {
            db.Add(quote);
            db.SaveChanges();
            return true;
        }
        catch (Exception ex) {
            Console.WriteLine(ex.ToString());
            return false;
        }
    }
}
