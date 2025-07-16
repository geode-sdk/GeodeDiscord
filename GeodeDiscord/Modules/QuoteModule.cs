using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace GeodeDiscord.Modules;

[Group("quote", "Quote other people's messages."), UsedImplicitly]
public partial class QuoteModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
    private static event Func<Quote, bool, Task>? onUpdate;

    [MessageCommand("Add quote"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Add(IMessage message) {
        IMessageChannel? channel = await Context.Interaction.GetChannelAsync();
        if (channel is not null) {
            IMessage? realMessage = await channel.GetMessageAsync(message.Id);
            if (realMessage is not null)
                message = realMessage;
        }

        if (db.quotes.Any(q => q.messageId == message.Id)) {
            await RespondAsync("❌ This message is already quoted!", ephemeral: true);
            return;
        }

        int max = !await db.quotes.AnyAsync() ? 0 : await db.Database
            .SqlQueryRaw<int>("SELECT max(CAST(name AS INTEGER)) as Value FROM quotes")
            .SingleAsync();

        Quote quote = await Util.MessageToQuote(Context.User.Id, (max + 1).ToString(), message);
        db.Add(quote);

        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save quote");
            await RespondAsync("❌ Failed to save quote!", ephemeral: true);
            return;
        }

        await RespondAsync(
            GetAddMessageContent(quote, true, false),
            GetAddMessageEmbeds(quote, true, false),
            allowedMentions: AllowedMentions.None,
            components: GetAddMessageComponents(quote, true, false)
        );
        await ContinueAddQuote(quote.messageId);
    }

    private async Task ContinueAddQuote(ulong quoteMessage) {
        onUpdate += OnUpdate;
        ulong responseId = (await GetOriginalResponseAsync()).Id;
        SocketInteraction? interaction = await InteractionUtility.WaitForInteractionAsync(Context.Client,
            TimeSpan.FromSeconds(20d),
            inter => inter.Type switch {
                InteractionType.MessageComponent => inter is SocketMessageComponent msg &&
                    msg.Message.Id == responseId &&
                    msg.Data.CustomId == $"quote/get-button:{quoteMessage}",
                _ => false
            });
        onUpdate -= OnUpdate;
        // if timed out, remove the buttons
        if (interaction is null) {
            await ModifyOriginalResponseAsync(msg => msg.Components = new ComponentBuilder().Build());
            return;
        }
        if (await db.quotes.FindAsync(quoteMessage) is not { } currentQuote)
            return;
        await UpdateAddMessage(currentQuote, true, true);
        await ContinueAddQuote(quoteMessage);
        return;

        async Task OnUpdate(Quote quote, bool exists) {
            // OnUpdate can be called from outside here even when multiple interaction is current,
            // causing all interactions to get updated with the same message
            if (quote.messageId != quoteMessage)
                return;
            await UpdateAddMessage(quote, exists, false);
        }
        async Task UpdateAddMessage(Quote quote, bool exists, bool setShow) {
            bool hasEmbeds = (await GetOriginalResponseAsync()).Embeds.Count > 0;
            await ModifyOriginalResponseAsync(msg => {
                bool show = hasEmbeds || setShow;
                msg.Content = GetAddMessageContent(quote, exists, show);
                msg.Embeds = GetAddMessageEmbeds(quote, exists, show);
                msg.Components = GetAddMessageComponents(quote, exists, show);
            });
        }
    }
    private static string GetAddMessageContent(Quote quote, bool exists, bool show) =>
        exists ? show ? "Quote saved!" : $"Quote {quote.jumpUrl} saved as **{quote.name}**!" :
            $"~~Quote {quote.jumpUrl} saved as *{quote.name}*!~~";
    private static Embed[] GetAddMessageEmbeds(Quote quote, bool exists, bool show) =>
        show && exists ? Util.QuoteToEmbeds(quote).ToArray() : [];
    private static MessageComponent GetAddMessageComponents(Quote quote, bool exists, bool show) =>
        !exists ? new ComponentBuilder().Build() :
            new ComponentBuilder()
                .WithButton("Show", $"quote/get-button:{quote.messageId}", ButtonStyle.Secondary,
                    new Emoji("🚿"), null, show)
                .WithButton("Rename", $"quote/sensitive/rename-button:{quote.messageId}", ButtonStyle.Secondary,
                    new Emoji("📝"))
                .WithButton("Delete", $"quote/sensitive/delete-button:{quote.messageId}", ButtonStyle.Secondary,
                    new Emoji("❌"))
                .Build();
    [ComponentInteraction("get-button:*"), UsedImplicitly]
    private async Task GetButton(string messageId) => await DeferAsync();

    [SlashCommand("count", "Gets the total amount of quotes."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task GetCount(IUser? user = null) {
        int count;
        if (user is null) {
            count = await db.quotes.CountAsync();
            await RespondAsync($"There are **{count}** total quotes.");
            return;
        }
        count = await db.quotes.CountAsync(x => x.authorId == user.Id);
        await RespondAsync($"{user.Mention} has been quoted **{count}** times.",
            allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("leaderboard", "Shows top 10 most quoted users."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task GetLeaderboard() {
        IEnumerable<string> lines = db.quotes
            .GroupBy(x => x.authorId)
            .Select(x => new {
                authorId = x.Key,
                quoteCount = x.Count()
            })
            .OrderByDescending(x => x.quoteCount)
            .Take(10)
            .AsEnumerable()
            .Select((x, i) => $"{i + 1}. <@{x.authorId}> - **{x.quoteCount}** quotes");
        await RespondAsync($"## 🏆 10 most quoted users:\n{string.Join("\n", lines)}",
            allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("random", "Gets a random quote."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task GetRandom(IUser? user = null) {
        if (user is null ? !db.quotes.Any() : !db.quotes.Any(q => q.authorId == user.Id)) {
            await RespondAsync("❌ There are no quotes of this user yet!", ephemeral: true);
            return;
        }
        Quote quote = await (user is null ? db.quotes : db.quotes.Where(q => q.authorId == user.Id))
            .OrderBy(_ => EF.Functions.Random())
            .FirstAsync();
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
        if (quote.extraAttachments != 0)
            await ReplyAsync(messageReference: Util.QuoteToForward(quote));
    }

    [SlashCommand("get", "Gets a quote with the specified name."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Get([Autocomplete(typeof(QuoteAutocompleteHandler))] string name) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.name == name);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
        if (quote.extraAttachments != 0)
            await ReplyAsync(messageReference: Util.QuoteToForward(quote));
    }

    public class QuoteAutocompleteHandler(ApplicationDbContext db) : AutocompleteHandler {
        public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
            string value = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;
            try {
                return Task.FromResult(AutocompletionResult.FromSuccess(db.quotes
                    .Where(q =>
                        q.messageId.ToString() == value ||
                        q.authorId.ToString() == value ||
                        EF.Functions.Like(q.name, $"%{value}%") ||
                        EF.Functions.Like(q.content, $"%{value}%"))
                    .Take(25)
                    .AsEnumerable()
                    .Select(q => {
                        string name = $"{q.name}: {q.content}";
                        return new AutocompleteResult(name.Length <= 100 ? name : $"{name[..97]}...", q.name);
                    })));
            }
            catch (Exception ex) {
                Log.Error(ex, "Quote autocomplete failed");
                return Task.FromResult(AutocompletionResult.FromError(ex));
            }
        }
    }
}
