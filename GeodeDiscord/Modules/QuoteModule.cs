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
using Attachment = GeodeDiscord.Database.Entities.Attachment;

namespace GeodeDiscord.Modules;

[Group("quote", "Quote other people's messages."), UsedImplicitly]
public partial class QuoteModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
    private static event Func<Quote, bool, Task>? onUpdate;

    [MessageCommand("Add quote"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Add(IMessage message) {
        await DeferAsync();

        IMessageChannel? channel = await Context.Interaction.GetChannelAsync();
        if (channel is not null) {
            IMessage? realMessage = await channel.GetMessageAsync(message.Id);
            if (realMessage is not null)
                message = realMessage;
        }

        if (db.quotes.Any(q => q.messageId == message.Id)) {
            await FollowupAsync("❌ This message is already quoted!", ephemeral: true);
            return;
        }

        int max = !await db.quotes.AnyAsync() ? 0 : await db.quotes.Select(x => x.id).MaxAsync();

        Quote quote = await Util.MessageToQuote(db, Context.User.Id, max + 1, message);
        db.Add(quote);

        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save quote");
            await FollowupAsync("❌ Failed to save quote!", ephemeral: true);
            return;
        }

        IUserMessage response = await FollowupAsync(
            GetAddMessageContent(quote, true, false),
            allowedMentions: AllowedMentions.None,
            components: GetAddMessageComponents(quote, true, false)
        );
        await ContinueAddQuote(quote.messageId, [response]);
    }

    private async Task ContinueAddQuote(ulong quoteMessage, List<IUserMessage> response) {
        onUpdate += OnUpdate;
        SocketInteraction? interaction = await InteractionUtility.WaitForInteractionAsync(Context.Client,
            TimeSpan.FromSeconds(20d),
            inter => inter.Type switch {
                InteractionType.MessageComponent => inter is SocketMessageComponent msg &&
                    msg.Message.Id == response[0].Id &&
                    msg.Data.CustomId == $"quote/get-button:{quoteMessage}",
                _ => false
            });
        onUpdate -= OnUpdate;
        // if timed out, remove the buttons
        if (interaction is null) {
            await response[0].ModifyAsync(msg => msg.Components = new ComponentBuilder().Build());
            return;
        }
        if (await db.quotes.FindAsync(quoteMessage) is not { } currentQuote)
            return;
        await UpdateAddMessage(currentQuote, true, true);
        await ContinueAddQuote(quoteMessage, response);
        return;

        async Task OnUpdate(Quote quote, bool exists) {
            // OnUpdate can be called from outside here even when multiple interaction is current,
            // causing all interactions to get updated with the same message
            if (quote.messageId != quoteMessage)
                return;
            await UpdateAddMessage(quote, exists, false);
        }
        async Task UpdateAddMessage(Quote quote, bool exists, bool setShow) {
            bool hasEmbeds = response[0].Embeds.Count > 0;
            bool show = exists && (hasEmbeds || setShow);
            if (show) {
                QuoteRenderer renderer = new(db, Context);
                response = await renderer.Render(quote, async (embeds, attachments) => {
                    await response[0].ModifyAsync(msg => {
                        msg.Attachments = new Optional<IEnumerable<FileAttachment>>(attachments);
                        msg.Content = GetAddMessageContent(quote, exists, true);
                        msg.Embeds = embeds;
                        msg.Components = GetAddMessageComponents(quote, exists, true);
                    });
                    return response[0];
                }, response);
            }
            else {
                await response[0].ModifyAsync(msg => {
                    msg.Attachments = new Optional<IEnumerable<FileAttachment>>([]);
                    msg.Content = GetAddMessageContent(quote, exists, false);
                    msg.Embeds = new Optional<Embed[]>([]);
                    msg.Components = GetAddMessageComponents(quote, exists, false);
                });
                foreach (IUserMessage message in response.Skip(1)) {
                    await message.DeleteAsync();
                }
            }
        }
    }
    private static string GetAddMessageContent(Quote quote, bool exists, bool show) =>
        exists ? show ? "Quote saved!" : $"Quote {quote.jumpUrl} saved as **{quote.GetFullName()}**!" :
            $"~~Quote {quote.jumpUrl} saved as *{quote.GetFullName()}*!~~";
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
        if (user is null ? !db.quotes.Any() : !db.quotes.Any(q => q.authorId == user.Id)) {
            await RespondAsync("❌ There are no quotes of this user yet!", ephemeral: true);
            return;
        }
        Quote quote = await (user is null ? db.quotes : db.quotes.Where(q => q.authorId == user.Id))
            .OrderBy(_ => EF.Functions.Random())
            .FirstAsync();
        QuoteRenderer renderer = new(db, Context);
        await renderer.Render(quote, async (embeds, attachments) => {
            await RespondWithFilesAsync(
                attachments: attachments,
                allowedMentions: AllowedMentions.None,
                embeds: embeds
            );
            return await GetOriginalResponseAsync();
        });
    }

    [SlashCommand("get", "Gets a quote with the specified ID."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Get([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        QuoteRenderer renderer = new(db, Context);
        await renderer.Render(quote, async (embeds, attachments) => {
            await RespondWithFilesAsync(
                attachments: attachments,
                allowedMentions: AllowedMentions.None,
                embeds: embeds
            );
            return await GetOriginalResponseAsync();
        });
    }

    [SlashCommand("info", "Gets all stored information for a quote with the specified ID."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Info([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
        Quote? quote = await db.quotes
            .Include(quote => quote.files)
            .Include(quote => quote.embeds)
            .FirstOrDefaultAsync(q => q.id == id);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
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
        foreach (Attachment file in quote.files)
            builder.AppendLine($"  - `{file.contentType}` `{file.url}` {file.url}");
        builder.AppendLine("- Embeds:");
        foreach (Attachment embed in quote.embeds)
            builder.AppendLine($"  - `{embed.contentType}` `{embed.url}` {embed.url}");
        builder.AppendLine($"- Content: `{quote.content}`");
        await FollowupAsync(
            text: builder.ToString(),
            allowedMentions: AllowedMentions.None
        );
    }

    [ComponentInteraction("guess-again"), UsedImplicitly]
    private Task GuessAgainOld() {
        GuessModule module = new(db);
        (module as IInteractionModuleBase).SetContext(Context);
        return Context.Guild.Id == 911701438269386882 && Context.Channel.Id != 1102573869832876042 ?
            RespondAsync("❌ Can only guess again old messages in <#1102573869832876042>!", ephemeral: true) :
            module.Guess();
    }

    [ComponentInteraction("guess-fix-names"), UsedImplicitly]
    private Task GuessFixNamesOld() {
        GuessModule module = new(db);
        (module as IInteractionModuleBase).SetContext(Context);
        return module.GuessFixNames();
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
