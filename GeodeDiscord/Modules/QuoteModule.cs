using System.Text.Json;

using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Modules;

[Group("quote", "Quote other people's messages."), UsedImplicitly]
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
        Quote quote = await Util.MessageToQuote(Quote.GetRandomName(), message);
        bool res = TrySaveQuote(_db, quote);
        if (!res) {
            await RespondAsync("❌ Failed to save quote!", ephemeral: true);
            return;
        }
        await RespondAsync(
            $"Quote saved as **{quote.name}**!",
            components: new ComponentBuilder()
                .WithButton("Rename", $"quote-rename-button:{quote.name}", emote: new Emoji("📝"))
                .Build()
        );
    }

    [ComponentInteraction("quote-rename-button:*", true), UsedImplicitly]
    public async Task RenameQuoteButton(string name) {
        if (!_db.quotes.Any()) {
            await RespondAsync("❌ There are no quotes yet!", ephemeral: true);
            return;
        }
        if (await _db.quotes.FindAsync(name) is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        await RespondWithModalAsync<QuoteRenameModal>($"quote-rename-modal:{name}");
    }

    public class QuoteRenameModal : IModal {
        public string Title => "Rename Quote";

        [InputLabel("New Name"), ModalTextInput("newName", placeholder: "geode creepypasta", maxLength: 30),
         UsedImplicitly]
        public string newName { get; set; } = "";
    }

    [ModalInteraction("quote-rename-modal:*", true), UsedImplicitly]
    public async Task RenameQuoteModal(string name, QuoteRenameModal modal) => await RenameQuote(name, modal.newName);

    [SlashCommand("random", "Gets a random quote."), UsedImplicitly]
    public async Task GetRandomQuote() {
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

    [SlashCommand("user", "Gets a random quote from the specified user."), UsedImplicitly]
    public async Task GetUserQuote(IUser user) {
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

    [SlashCommand("user-id", "Gets a random quote from the specified user by ID."), UsedImplicitly]
    public async Task GetUserQuote(string user) {
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

    [SlashCommand("get", "Gets a quote with the specified name."), UsedImplicitly]
    public async Task GetQuoteByName(string name) {
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

    [SlashCommand("get-message-id", "Gets a quote for the specified message."), UsedImplicitly]
    public async Task GetQuoteByMessageId(string message) {
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

    [SlashCommand("find", "Finds the first quote that contains the specified string."), UsedImplicitly]
    public async Task FindQuote(string search) {
        Quote? quote = await _db.quotes.FirstOrDefaultAsync(q => q.name.Contains(search) || q.content.Contains(search));
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        await RespondAsync(
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToEmbeds(quote).ToArray()
        );
    }

    [SlashCommand("rename", "Renames a quote with the specified name."), UsedImplicitly]
    public async Task RenameQuote(string oldName, string newName) {
        Quote? quote = await _db.quotes.FindAsync(oldName);
        if (quote is null) {
            await RespondAsync($"❌ Quote **{oldName}** not found!", ephemeral: true);
            return;
        }
        if (await _db.quotes.FindAsync(newName) is not null) {
            await RespondAsync($"❌ Quote **${newName}** already exists!", ephemeral: true);
            return;
        }
        _db.Remove(quote);
        _db.Add(quote.WithName(newName));
        try {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) {
            Console.WriteLine(ex.ToString());
            await RespondAsync("❌ Failed to rename quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Quote *{quote.name}* renamed to **{newName}**!");
    }

    [SlashCommand("delete", "Deletes a quote with the specified name."), UsedImplicitly]
    public async Task DeleteQuote(string name) {
        Quote? quote = await _db.quotes.FindAsync(name);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        _db.Remove(quote);
        try {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) {
            Console.WriteLine(ex.ToString());
            await RespondAsync("❌ Failed to delete quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Deleted quote **{quote.name}**!");
    }

    [SlashCommand("update", "Updates a quote by re-fetching the message."), UsedImplicitly]
    public async Task UpdateQuote(string name) {
        Quote? quote = await _db.quotes.FindAsync(name);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }

        if (quote.channelId == 0) {
            await RespondAsync("❌ Failed to update quote! (channel ID not set)", ephemeral: true);
            return;
        }

        IMessageChannel? channel = Context.Guild.GetTextChannel(quote.channelId) ??
            Context.Guild.GetStageChannel(quote.channelId) ??
            Context.Guild.GetVoiceChannel(quote.channelId);
        if (channel is null) {
            await RespondAsync($"❌ Failed to update quote! (channel {quote.channelId} not found)", ephemeral: true);
            return;
        }

        IMessage? message = await channel.GetMessageAsync(quote.messageId);
        if (message is null) {
            await RespondAsync($"❌ Failed to update quote! (message {quote.messageId} not found)", ephemeral: true);
            return;
        }

        _db.Remove(quote);
        _db.Add(await Util.MessageToQuote(quote.name, message, quote));

        try {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) {
            Console.WriteLine(ex.ToString());
            await RespondAsync("❌ Failed to update quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Update quote **{quote.name}**!");
    }

    [SlashCommand("count", "Gets the total amount of quotes."), UsedImplicitly]
    public async Task GetQuoteCount() {
        int count = await _db.quotes.CountAsync();
        await RespondAsync($"There are **{count}** total quotes.");
    }

    private readonly record struct UberBotQuote
        (string id, string nick, string channel, string messageId, string text, long time);

    [SlashCommand("import", "Imports quotes from UB3R-B0T's API response."), UsedImplicitly]
    public async Task ImportQuotes(Attachment attachment) {
        await DeferAsync();
        await FollowupAsync($"Importing quotes from {attachment.Filename}: downloading attachment");

        string data;
        using (HttpClient client = new()) {
            data = await client.GetStringAsync(attachment.Url);
        }

        UberBotQuote[]? toImport;
        try {
            await ModifyOriginalResponseAsync(prop =>
                prop.Content = $"Importing quotes from {attachment.Filename}: deserializing JSON");
            toImport = JsonSerializer.Deserialize<UberBotQuote[]>(data);
        }
        catch (JsonException) {
            await FollowupAsync("❌ Failed to import quotes! (failed to deserialize JSON)");
            return;
        }
        if (toImport is null) {
            await FollowupAsync("❌ Failed to import quotes! (Deserialize returned null)");
            return;
        }

        int importedQuotes = 0;
        for (int i = 0; i < toImport.Length; i++) {
            if (i % 10 == 0)
                await ModifyOriginalResponseAsync(prop =>
                    prop.Content = $"Importing quotes from {attachment.Filename}: {i}/{toImport.Length.ToString()}");
            (string id, string nick, string channelName, string messageIdStr, string text, long time) = toImport[i];
            if (!ulong.TryParse(messageIdStr, out ulong messageId)) {
                await FollowupAsync($"⚠️ Failed to import quote {id}! (invalid message ID)");
                continue;
            }
            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(time);

            IMessageChannel? channel = null;
            if (!string.IsNullOrWhiteSpace(channelName)) {
                try {
                    channel = Context.Guild.TextChannels.FirstOrDefault(ch => ch.Name == channelName) ??
                        Context.Guild.StageChannels.FirstOrDefault(ch => ch.Name == channelName) ??
                        Context.Guild.VoiceChannels.FirstOrDefault(ch => ch.Name == channelName);
                    if (channel is null) {
                        await FollowupAsync($"⚠️ Failed to import quote {id}! (channel {channelName} not found)");
                        continue;
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                    await FollowupAsync($"⚠️ Failed to import quote {id}! (failed to access channel)");
                    continue;
                }
            }

            IMessage? message = null;
            if (channel is not null) {
                try {
                    message = await channel.GetMessageAsync(messageId);
                    if (message is null) {
                        await FollowupAsync($"⚠️ Failed to import quote {id}! (message {messageId} not found)");
                        continue;
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                    await FollowupAsync($"⚠️ Failed to import quote {id}! (failed to access message)");
                    continue;
                }
            }

            if (message is not null) {
                _db.Add(await Util.MessageToQuote(id, message, timestamp));
            }
            else {
                RestGuildUser? user;
                nick = nick.ToLowerInvariant();
                try {
                    user = (await Context.Guild.SearchUsersAsync(nick)).FirstOrDefault();
                    if (user is null)
                        await FollowupAsync($"⚠️ Couldn't get user {nick} for quote {id}! (user is null)");
                    else
                        await FollowupAsync($"🗒️ User for quote {id} detected as <@{user.Id}>",
                            allowedMentions: AllowedMentions.None);
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                    await FollowupAsync($"⚠️ Couldn't get user {nick} for quote {id}!");
                    user = null;
                }

                await FollowupAsync($"⚠️ Quote {id} imported with potentially missing data!");

                _db.Add(new Quote {
                    name = id,
                    messageId = messageId,
                    channelId = 0,
                    createdAt = timestamp,
                    lastEditedAt = timestamp,

                    authorId = user?.Id ?? 0,
                    replyAuthorId = 0,
                    jumpUrl = channelName,

                    images = "",
                    extraAttachments = 0,

                    content = text
                });
            }

            importedQuotes++;
        }

        try {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) {
            Console.WriteLine(ex.ToString());
            await FollowupAsync("❌ Failed to import quotes! (error when writing to the database)");
            return;
        }

        await ModifyOriginalResponseAsync(prop =>
            prop.Content = $"Imported {importedQuotes} quotes from {attachment.Filename}.");
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
