using System.Diagnostics.CodeAnalysis;
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
public partial class QuoteModule(ApplicationDbContext db, QuoteEditor editor, QuoteRenderer renderer) :
    InteractionModuleBase<SocketInteractionContext> {
    [MessageCommand("Add quote"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Add(IMessage message) {
        await DeferAsync();

        Quote quote = await editor.Create(message);
        await db.SaveChangesAsync();

        IUserMessage response = await FollowupAsync(
            allowedMentions: AllowedMentions.None,
            components: await GetAddMessageComponents(quote, true, false, false)
        );
        await ContinueAddQuote(quote, response, false);
    }

    private async Task ContinueAddQuote(Quote quote, IUserMessage response, bool show) {
        bool setShow = false;
        bool exists = true;
        Quote waitingQuote = quote;
        bool finished = await await Task.WhenAny(
            new Func<Task<bool>>(async () => {
                await Task.Delay(TimeSpan.FromSeconds(20.0));
                return true;
            })(),
            new Func<Task<bool>>(async () => {
                await Util.WaitForInteractionAsync(
                    Context.Client, inter => inter.Type switch {
                        InteractionType.MessageComponent => inter is SocketMessageComponent msg &&
                            msg.Message.Id == response.Id &&
                            msg.Data.CustomId == $"quote/get-button:{waitingQuote.messageId}",
                        _ => false
                    }
                );
                setShow = true;
                return false;
            })(),
            new Func<Task<bool>>(async () => {
                Quote? updatedQuote = await editor.WaitForUpdateAsync(waitingQuote);
                if (updatedQuote is null) {
                    exists = false;
                    return true;
                }
                quote = updatedQuote;
                return false;
            })()
        );

        show = exists && (show || setShow);

        MessageComponent components = await GetAddMessageComponents(quote, exists, show, finished);
        await response.ModifyAsync(msg => {
            msg.AllowedMentions = AllowedMentions.None;
            msg.Components = components;
        });

        if (!finished)
            await ContinueAddQuote(quote, response, show);
    }

    private async Task<MessageComponent> GetAddMessageComponents(Quote quote, bool exists, bool show, bool finished) {
        ComponentBuilderV2 builder = new();

        IMessageComponentBuilder[] renderedQuote = await renderer.Render(quote).ToArrayAsync();

        // not gonna be 100% accurate but close enough
        // delay by like a second bc yeah
        DateTimeOffset timeoutTimestamp = DateTimeOffset.Now + TimeSpan.FromSeconds(21.0);
        string timeoutTime = finished ? "" : $"<t:{timeoutTimestamp.ToUnixTimeSeconds()}:R>";

        if (exists) {
            if (show) {
                builder.WithTextDisplay($"{timeoutTime}Quote saved!");
                builder.AddComponents(renderedQuote);
            }
            else {
                builder.WithTextDisplay($"{timeoutTime}Quote {quote.jumpUrl} saved as **{quote.GetFullName()}**!");
            }
        }
        else {
            builder.WithTextDisplay($"{timeoutTime}~~Quote {quote.jumpUrl} saved as *{quote.GetFullName()}*!~~");
        }
        if (!finished) {
            builder.WithActionRow(x => x
                .WithButton(
                    "Show", $"quote/get-button:{quote.messageId}", ButtonStyle.Secondary,
                    new Emoji("🚿"), null, show)
                .WithButton(
                    "Rename", $"quote/sensitive/rename-button:{quote.messageId}", ButtonStyle.Secondary,
                    new Emoji("📝"))
                .WithButton(
                    "Delete", $"quote/sensitive/delete-button:{quote.messageId}", ButtonStyle.Secondary,
                    new Emoji("❌"))
            );
        }
        return builder.Build();
    }

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
        await DeferAsync();
        IEnumerable<string> lines = db.quotes
            .GroupBy(x => x.authorId)
            .Select(x => new { authorId = x.Key, quoteCount = x.Count() })
            .OrderByDescending(x => x.quoteCount)
            .Take(10)
            .AsEnumerable()
            .Select((x, i) => $"{i + 1}. <@{x.authorId}> - **{x.quoteCount}** quotes");
        await FollowupAsync(
            text: $"## 🏆 10 most quoted users:\n{string.Join("\n", lines)}",
            allowedMentions: AllowedMentions.None
        );
    }

    [SuppressMessage("ReSharper", "EntityFramework.ClientSideDbFunctionCall")]
    [SlashCommand("random", "Gets a random quote."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task GetRandom(IUser? user = null) {
        if (user is null ? !db.quotes.Any() : !db.quotes.Any(q => q.authorId == user.Id))
            throw new MessageErrorException("There are no quotes of this user yet!");
        Quote quote = await (user is null ? db.quotes : db.quotes.Where(q => q.authorId == user.Id))
            .OrderBy(_ => EF.Functions.Random())
            .FirstAsync();
        await DeferAsync();
        await FollowupAsync(
            allowedMentions: AllowedMentions.None,
            components: new ComponentBuilderV2(await renderer.Render(quote).ToListAsync()).Build()
        );
    }

    [SlashCommand("get", "Gets a quote with the specified ID."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Get([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
        if (quote is null)
            throw new MessageErrorException("Quote not found!");
        await DeferAsync();
        await FollowupAsync(
            allowedMentions: AllowedMentions.None,
            components: new ComponentBuilderV2(await renderer.Render(quote).ToListAsync()).Build()
        );
    }

    [SlashCommand("info", "Gets all stored information for a quote with the specified ID."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Info([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
        Quote? quote = await db.quotes
            .Include(x => x.attachments)
            .Include(x => x.embeds)
            .FirstOrDefaultAsync(q => q.id == id);
        if (quote is null)
            throw new MessageErrorException("Quote not found!");
        StringBuilder builder = new();
        // defer in case getting the channel and all the users is slow
        await DeferAsync();
        IChannel? channel = await Util.GetChannelAsync(Context.Client, quote.channelId);
        IUser? quoter = await Util.GetUserAsync(Context.Client, quote.quoterId);
        IUser? author = await Util.GetUserAsync(Context.Client, quote.authorId);
        IUser? replyAuthor = await Util.GetUserAsync(Context.Client, quote.replyAuthorId);
        builder.AppendLine($"- Message: `{quote.messageId}` {quote.jumpUrl}");
        builder.AppendLine($"- ID: `{quote.id}`");
        builder.AppendLine($"- Name: `{quote.name}`");
        builder.AppendLine($"- Channel: `{quote.channelId}` `#{channel?.Name ?? "<unknown>"}` <#{quote.channelId}>");
        builder.AppendLine($"- Created at: `{quote.createdAt}` <t:{quote.createdAt.ToUnixTimeSeconds()}:f>");
        builder.AppendLine($"- Last edited at: `{quote.lastEditedAt}` <t:{quote.lastEditedAt.ToUnixTimeSeconds()}:f>");
        builder.AppendLine($"- Quoter: `{quote.quoterId}` `{quoter?.GlobalName ?? "<unknown>"}` `@{quoter?.Username ?? "<unknown>"}` <@{quote.quoterId}>");
        builder.AppendLine($"- Author: `{quote.authorId}` `{author?.GlobalName ?? "<unknown>"}` `@{author?.Username ?? "<unknown>"}` <@{quote.authorId}>");
        builder.AppendLine($"- Reply author: `{quote.replyAuthorId}` `{replyAuthor?.GlobalName ?? "<unknown>"}` `@{replyAuthor?.Username ?? "<unknown>"}` <@{quote.replyAuthorId}>");
        builder.AppendLine("- Files:");
        foreach (Quote.Attachment file in quote.attachments)
            builder.AppendLine($"  - `{file.id}` `{file.name}` `{file.size}` `{file.url}` `{file.contentType}` `{file.description}` {file.url}");
        builder.AppendLine("- Embeds:");
        foreach (Quote.Embed embed in quote.embeds)
            builder.AppendLine($"  - {embed.url}");
        builder.AppendLine($"- Content: `{quote.content}`");
        await FollowupAsync(
            text: builder.ToString(),
            allowedMentions: AllowedMentions.None
        );
    }

    public class QuoteAutocompleteHandler(ApplicationDbContext db) : AutocompleteHandler {
        public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
            string value = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;
            try {
                return Task.FromResult(AutocompletionResult.FromSuccess(db.quotes
                    .Where(q =>
                        q.id.ToString() == value ||
                        q.messageId.ToString() == value ||
                        q.authorId.ToString() == value ||
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
