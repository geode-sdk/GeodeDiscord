using Discord;
using Discord.Interactions;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Modules;

[Group("quote", "Quote other people's messages."), UsedImplicitly]
public partial class QuoteModule : InteractionModuleBase<SocketInteractionContext> {
    private readonly ApplicationDbContext _db;

    public QuoteModule(ApplicationDbContext db) => _db = db;

    [MessageCommand("Add quote"), EnabledInDm(false), UsedImplicitly]
    public async Task Add(IMessage message) {
        IMessageChannel? channel = await Context.Interaction.GetChannelAsync();
        if (channel is not null) {
            IMessage? realMessage = await channel.GetMessageAsync(message.Id);
            if (realMessage is not null)
                message = realMessage;
        }

        int max = !await _db.quotes.AnyAsync() ? 0 : await _db.quotes.AsAsyncEnumerable()
            .MaxAsync(q => !int.TryParse(q.name, out int n) ? int.MinValue : n);

        Quote quote = await Util.MessageToQuote(Context.User.Id, (max + 1).ToString(), message);
        _db.Add(quote);

        try {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) {
            Console.WriteLine(ex.ToString());
            await RespondAsync("❌ Failed to save quote!", ephemeral: true);
            return;
        }

        Console.WriteLine();
        await RespondAsync(
            $"Quote {quote.jumpUrl} saved as **{quote.name}**!",
            components: new ComponentBuilder()
                .WithButton("Show", $"quote/sensitive/get-button:{quote.name}", ButtonStyle.Secondary, new Emoji("🚿"))
                .WithButton("Rename", $"quote/sensitive/rename-button:{quote.name}", ButtonStyle.Secondary, new Emoji("📝"))
                .WithButton("Delete", $"quote/sensitive/delete-button:{quote.name}", ButtonStyle.Secondary, new Emoji("❌"))
                .Build()
        );
    }

    [SlashCommand("count", "Gets the total amount of quotes."), EnabledInDm(false), UsedImplicitly]
    public async Task GetCount() {
        int count = await _db.quotes.CountAsync();
        await RespondAsync($"There are **{count}** total quotes.");
    }

    [SlashCommand("random", "Gets a random quote."), EnabledInDm(false), UsedImplicitly]
    public async Task GetRandom() {
        if (!_db.quotes.Any()) {
            await RespondAsync("❌ There are no quotes yet!", ephemeral: true);
            return;
        }
        Quote quote = _db.quotes.OrderBy(_ => EF.Functions.Random()).First();
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
    }

    [SlashCommand("user", "Gets a random quote from the specified user."), EnabledInDm(false),
     UsedImplicitly]
    public async Task GetRandomByUser(IUser user) {
        if (!_db.quotes.Any()) {
            await RespondAsync("❌ There are no quotes yet!", ephemeral: true);
            return;
        }
        Quote? quote = await _db.quotes
            .Where(q => q.authorId == user.Id)
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync();
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
    }

    [SlashCommand("user-id", "Gets a random quote from the specified user by ID."), EnabledInDm(false),
     UsedImplicitly]
    public async Task GetRandomByUser(string user) {
        if (!_db.quotes.Any()) {
            await RespondAsync("❌ There are no quotes yet!", ephemeral: true);
            return;
        }
        if (!ulong.TryParse(user, out ulong userId)) {
            await RespondAsync("❌ Invalid user ID!", ephemeral: true);
            return;
        }
        Quote? quote = await _db.quotes
            .Where(q => q.authorId == userId)
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync();
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
    }

    [SlashCommand("get", "Gets a quote with the specified name."), EnabledInDm(false),
     ComponentInteraction("get-button:*"), UsedImplicitly]
    public async Task GetByName([Autocomplete(typeof(QuoteAutocompleteHandler))] string name) {
        Quote? quote = await _db.quotes.FindAsync(name);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
    }

    [SlashCommand("get-message", "Gets a quote for the specified message."), EnabledInDm(false),
     UsedImplicitly]
    public async Task GetByMessage(string message) {
        if (!_db.quotes.Any()) {
            await RespondAsync("❌ There are no quotes yet!", ephemeral: true);
            return;
        }
        if (!ulong.TryParse(message, out ulong messageId)) {
            await RespondAsync("❌ Invalid message ID!", ephemeral: true);
            return;
        }
        Quote? quote = await _db.quotes.FirstOrDefaultAsync(q => q.messageId == messageId);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
    }

    public class QuoteAutocompleteHandler : AutocompleteHandler {
        private readonly ApplicationDbContext _db;
        public QuoteAutocompleteHandler(ApplicationDbContext db) => _db = db;

        public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
            string value = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;
            try {
                return Task.FromResult(AutocompletionResult.FromSuccess(_db.quotes
                    .Where(q =>
                        EF.Functions.Glob(q.name, $"*{value}*") ||
                        EF.Functions.Glob(q.content, $"*{value}*"))
                    .Take(25)
                    .AsEnumerable()
                    .Select(q => {
                        string name = $"{q.name}: {q.content}";
                        return new AutocompleteResult(name.Length <= 100 ? name : $"{name[..97]}...", q.name);
                    })));
            }
            catch (Exception ex) {
                return Task.FromResult(AutocompletionResult.FromError(ex));
            }
        }
    }
}
