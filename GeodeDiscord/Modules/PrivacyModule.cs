using Discord;
using Discord.Interactions;
using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace GeodeDiscord.Modules;

[Group("privacy", "Manage your privacy.")]
public class PrivacyModule(ApplicationDbContext db, QuoteEditor editor) :
    InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand("policy", "Shows the privacy policy."), UsedImplicitly]
    public async Task Policy() {
        await RespondAsync(
            """
            ## Geode Discord Bot's Privacy Policy
            ```
            TODO: write the privacy policy
            ```
            -# [web version](https://geode-sdk.org/todo-discord-bot-privacy-policy)
            """
        );
    }

    [SlashCommand("redact", "Redacts the specified quote of you."), UsedImplicitly]
    public async Task Redact([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
        if (quote is null)
            throw new MessageErrorException("Quote not found!");
        if (quote.authorId != Context.User.Id && quote.replyAuthorId != Context.User.Id)
            throw new MessageErrorException("You are not the author of the quote nor the replied message!");
        editor.Redact(quote, quote.authorId == Context.User.Id, quote.replyAuthorId == Context.User.Id);
        await db.SaveChangesAsync();
        await RespondAsync($"Quote **{quote.GetFullName()}** redacted!");
    }

    [SlashCommand("unredact", "Unredacts the specified quote of you."), UsedImplicitly]
    public async Task Unredact([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
        if (quote is null)
            throw new MessageErrorException("Quote not found!");
        if (quote.authorId != Context.User.Id && quote.replyAuthorId != Context.User.Id)
            throw new MessageErrorException("You are not the author of the quote nor the replied message!");
        await editor.Update(quote, quote.authorId == Context.User.Id, quote.replyAuthorId == Context.User.Id);
        await db.SaveChangesAsync();
        await RespondAsync($"Quote **{quote.GetFullName()}** redacted!");
    }

    [SlashCommand("opt-out", "Redact all existing and future quotes of you."), UsedImplicitly]
    public async Task OptOut() {
        OptOut? optOut = await db.optOuts.FirstOrDefaultAsync(q => q.userId == Context.User.Id);
        if (optOut is not null)
            throw new MessageErrorException("You already opted out!");
        db.optOuts.Add(new OptOut { userId = Context.User.Id });
        foreach (Quote quote in db.quotes
            .Where(q => q.authorId == Context.User.Id || q.replyAuthorId == Context.User.Id)) {
            editor.Redact(quote, quote.authorId == Context.User.Id, quote.replyAuthorId == Context.User.Id);
        }
        await db.SaveChangesAsync();
        await RespondAsync("Opted out of quotes and redacted all existing quotes!");
    }

    [SlashCommand("opt-in", "Unredact all existing and future quotes of you."), UsedImplicitly]
    public async Task OptIn() {
        await DeferAsync();

        OptOut? optOut = await db.optOuts.FirstOrDefaultAsync(q => q.userId == Context.User.Id);
        if (optOut is not null)
            db.optOuts.Remove(optOut);
        await db.SaveChangesAsync();

        List<(Quote quote, Exception exception)> exceptions = [];
        foreach (Quote quote in db.quotes
            .Where(q => q.authorId == Context.User.Id || q.replyAuthorId == Context.User.Id)) {
            try {
                await editor.Update(quote, quote.authorId == Context.User.Id, quote.replyAuthorId == Context.User.Id);
            }
            catch (Exception ex) {
                exceptions.Add((quote, ex));
            }
        }

        await db.SaveChangesAsync();

        if (exceptions.Count == 0) {
            await FollowupAsync("Opted in to quotes and unredacted all existing quotes!");
        }
        else {
            string failed = string.Join(", ", exceptions.Select(x => $"**{x.quote.id}**"));
            await FollowupAsync($"Opted in to quotes but some quotes failed to unredact: {failed}!");
        }
    }

    private class QuoteAutocompleteHandler(ApplicationDbContext db) : AutocompleteHandler {
        public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
            string value = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;
            try {
                return Task.FromResult(AutocompletionResult.FromSuccess(db.quotes
                    .Where(q =>
                        q.authorId == autocompleteInteraction.User.Id ||
                        q.replyAuthorId == autocompleteInteraction.User.Id)
                    .Where(q =>
                        q.id.ToString() == value ||
                        q.messageId.ToString() == value ||
                        q.name != "" && EF.Functions.Like(q.name, $"%{value}%") ||
                        EF.Functions.Like(q.content, $"%{value}%"))
                    .Take(25)
                    .AsEnumerable()
                    .Select(q => {
                        string name = $"{q.GetFullName()}: {q.content}";
                        return new AutocompleteResult(name.Length <= 100 ? name : $"{name[..97]}...", q.id);
                    })));
            }
            catch (Exception ex) {
                Log.Error(ex, "Quote autocomplete failed");
                return Task.FromResult(AutocompletionResult.FromError(ex));
            }
        }
    }
}
