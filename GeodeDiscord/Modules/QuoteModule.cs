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
    private static readonly TimeSpan addQuoteTimeout = TimeSpan.FromSeconds(20.0);
    private enum QuoteShow { Hide, Show, Error }
    private enum AddQuoteStatus { Waiting, Saved, Cancelled }

    [MessageCommand("Add quote"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Add(IMessage message) {
        await DeferAsync();

        Quote quote = await editor.Create(message);

        bool show = false;
        AddQuoteStatus status = AddQuoteStatus.Waiting;

        IUserMessage response = await FollowupAsync(
            allowedMentions: AllowedMentions.None,
            components: await GetAddMessageComponents(quote, show ? QuoteShow.Show : QuoteShow.Hide, status)
        );

        while (status == AddQuoteStatus.Waiting) {
            (bool setShow, status) = await CheckAddQuoteInteraction(
                quote,
                await InteractionUtility.WaitForInteractionAsync(
                    Context.Client, addQuoteTimeout, x =>
                        x is SocketMessageComponent y && y.Message.Id == response.Id ||
                        x is SocketModal z && z.Message.Id == response.Id
                )
            );

            show = status != AddQuoteStatus.Cancelled && (show || setShow);

            if (status == AddQuoteStatus.Saved) {
                quote = await editor.Add(quote);
                await db.SaveChangesAsync();
            }

            MessageComponent components;
            try {
                components = await GetAddMessageComponents(quote, show ? QuoteShow.Show : QuoteShow.Hide, status);
            }
            catch {
                components = await GetAddMessageComponents(quote, QuoteShow.Error, status);
            }

            await response.ModifyAsync(msg => {
                msg.AllowedMentions = AllowedMentions.None;
                msg.Components = components;
            });
        }
    }

    private static async Task<(bool setShow, AddQuoteStatus status)> CheckAddQuoteInteraction(Quote quote,
        SocketInteraction? interaction) {
        try {
            switch (interaction) {
                case null:
                    return (false, AddQuoteStatus.Saved);
                case SocketMessageComponent { Data.CustomId: "quote/add-get-button" }:
                    await interaction.DeferAsync();
                    return (true, AddQuoteStatus.Waiting);
                case SocketMessageComponent { Data.CustomId: "quote/add-rename-button" }:
                    await interaction.RespondWithModalAsync(
                        new ModalBuilder(
                            "Rename Quote",
                            "quote/add-rename-modal",
                            new ModalComponentBuilder()
                                .WithTextInput(
                                    label: "New name",
                                    customId: "quote/add-rename-modal-new-name",
                                    placeholder: "geode creepypasta",
                                    maxLength: 30,
                                    required: false,
                                    value: quote.name
                                )
                        ).Build());
                    return (false, AddQuoteStatus.Waiting);
                case SocketModal { Data.CustomId: "quote/add-rename-modal" } modal:
                    await interaction.DeferAsync();
                    quote.name = modal.Data.Components.First().Value;
                    return (false, AddQuoteStatus.Waiting);
                case SocketMessageComponent { Data.CustomId: "quote/add-cancel-button" }:
                    await interaction.DeferAsync();
                    return (false, AddQuoteStatus.Cancelled);
                default:
                    await interaction.DeferAsync();
                    return (false, AddQuoteStatus.Saved);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Add quote failed");
            return (false, AddQuoteStatus.Saved);
        }
    }

    private async Task<MessageComponent> GetAddMessageComponents(Quote quote, QuoteShow show, AddQuoteStatus status) {
        ComponentBuilderV2 builder = new();

        // not gonna be 100% accurate but close enough
        // delay by a second bc yeah
        DateTimeOffset timeoutTimestamp = DateTimeOffset.Now + addQuoteTimeout + TimeSpan.FromSeconds(1.0);
        string timeoutTime = $"<t:{timeoutTimestamp.ToUnixTimeSeconds()}:R>";

        string name = quote.id == 0 && string.IsNullOrWhiteSpace(quote.name) ? "" :
            status == AddQuoteStatus.Cancelled ?
                $" as *{quote.GetFullName()}*" :
                $" as **{quote.GetFullName()}**";

        switch (status) {
            case AddQuoteStatus.Waiting:
                builder.WithTextDisplay(show == QuoteShow.Show ?
                    $"Quote will be saved{name} {timeoutTime}..." :
                    $"Quote {quote.jumpUrl} will be saved{name} {timeoutTime}..."
                );
                break;
            case AddQuoteStatus.Saved:
                builder.WithTextDisplay(show == QuoteShow.Show ?
                    "Quote saved!" :
                    $"Quote {quote.jumpUrl} saved{name}!"
                );
                break;
            case AddQuoteStatus.Cancelled:
                builder.WithTextDisplay($"~~Quote {quote.jumpUrl} will be saved `never`...~~");
                break;
        }

        switch (show) {
            case QuoteShow.Show:
                builder.AddComponents(await renderer.Render(quote).ToArrayAsync());
                break;
            case QuoteShow.Error:
                builder.WithTextDisplay("-# `❌ Failed to render quote!`");
                break;
        }

        if (status == AddQuoteStatus.Waiting) {
            builder.WithActionRow(x => x
                .WithButton(
                    "Show", "quote/add-get-button", ButtonStyle.Secondary,
                    new Emoji("🚿"), null, show != QuoteShow.Hide)
                .WithButton(
                    "Rename", "quote/add-rename-button", ButtonStyle.Secondary,
                    new Emoji("📝"))
                .WithButton(
                    "Cancel", "quote/add-cancel-button", ButtonStyle.Secondary,
                    new Emoji("❌"))
            );
        }

        return builder.Build();
    }

    // so that discord.net doesnt complain
#pragma warning disable CA1822
    [ComponentInteraction("add-*"), ModalInteraction("add-*"), UsedImplicitly]
    private Task QuoteAddButton(string stupid) => Task.CompletedTask;
#pragma warning restore CA1822

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
        IChannel? channel = await Util.GetChannelAsyncSafe(Context.Client, quote.channelId);
        IUser? quoter = await Util.GetUserAsyncSafe(Context.Client, quote.quoterId);
        IUser? author = await Util.GetUserAsyncSafe(Context.Client, quote.authorId);
        IUser? replyAuthor = await Util.GetUserAsyncSafe(Context.Client, quote.replyAuthorId);
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
