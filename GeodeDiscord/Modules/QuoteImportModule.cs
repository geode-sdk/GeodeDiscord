using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Discord;
using Discord.Interactions;
using Discord.Rest;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace GeodeDiscord.Modules;

[Group("quote-import", "Import quotes."), DefaultMemberPermissions(GuildPermission.Administrator), CommandContextType(InteractionContextType.Guild)]
public partial class QuoteImportModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand("manual-quoter", "Sets the quoter of a quote."), CommandContextType(InteractionContextType.Guild),
     UsedImplicitly]
    public async Task ManualQuoter([Autocomplete(typeof(QuoteModule.QuoteAutocompleteHandler))] string name,
        IUser newQuoter) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.name == name);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        db.Remove(quote);
        db.Add(quote with { quoterId = newQuoter.Id });
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to change quote");
            await RespondAsync("❌ Failed to change quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Quote **{quote.name}** quoter changed to `{newQuoter.Id}`!");
    }
    [SlashCommand("manual-author", "Sets the author of a quote."), CommandContextType(InteractionContextType.Guild),
     UsedImplicitly]
    public async Task ManualAuthor([Autocomplete(typeof(QuoteModule.QuoteAutocompleteHandler))] string name,
        IUser newAuthor) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.name == name);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        db.Remove(quote);
        db.Add(quote with { authorId = newAuthor.Id });
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to change quote");
            await RespondAsync("❌ Failed to change quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Quote **{quote.name}** author changed to `{newAuthor.Id}`!");
    }

    private readonly record struct UberBotQuote
        (string id, string nick, string channel, string messageId, string text, long time);

    [Group("uber-bot", "UB3R-B0T")]
    public partial class UberBotModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        [GeneratedRegex("\\.quote add \"(.*)\" - (.*)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
        private static partial Regex QuoteAddRegex();

        [JsonSourceGenerationOptions]
        [JsonSerializable(typeof(UberBotQuote[]))]
        private partial class QuoteJsonSourceGen : JsonSerializerContext;

        [SlashCommand("import", "Imports quotes from UB3R-B0T's API response."), CommandContextType(InteractionContextType.Guild),
         UsedImplicitly]
        public async Task Import(Attachment attachment) {
            await DeferAsync();
            Log.Information("[quote-import] Beginning UB3R-B0T quote import from {File}", attachment.Filename);
            await FollowupAsync($"Importing quotes from {attachment.Filename}: downloading attachment");

            string data;
            using (HttpClient client = new()) { data = await client.GetStringAsync(attachment.Url); }

            UberBotQuote[]? toImport;
            try {
                await ModifyOriginalResponseAsync(prop =>
                    prop.Content = $"Importing quotes from {attachment.Filename}: deserializing JSON");
                toImport = JsonSerializer.Deserialize(data, QuoteJsonSourceGen.Default.UberBotQuoteArray);
            }
            catch (JsonException ex) {
                Log.Error(ex, "Failed to import quotes");
                await FollowupAsync("❌ Failed to import quotes! (failed to deserialize JSON)");
                return;
            }
            if (toImport is null) {
                Log.Error("Failed to import quotes: toImport is null");
                await FollowupAsync("❌ Failed to import quotes! (Deserialize returned null)");
                return;
            }

            int importedQuotes = 0;
            for (int i = 0; i < toImport.Length; i++) {
                if (i % 10 == 0)
                    await ModifyOriginalResponseAsync(prop =>
                        prop.Content =
                            $"Importing quotes from {attachment.Filename}: {i}/{toImport.Length.ToString()}");
                if (await ImportSingle(toImport[i]))
                    importedQuotes++;
            }

            try { await db.SaveChangesAsync(); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to import quotes");
                await FollowupAsync("❌ Failed to import quotes! (error when writing to the database)");
                return;
            }

            Log.Information("[quote-import] Imported {Count} quotes from {File}", importedQuotes, attachment.Filename);
            await ModifyOriginalResponseAsync(prop =>
                prop.Content = $"Imported {importedQuotes} quotes from {attachment.Filename}.");
        }
        private async Task<bool> ImportSingle(UberBotQuote oldQuote) {
            (string id, string nick, string channelName, string messageIdStr, string text, long time) = oldQuote;
            if (!ulong.TryParse(messageIdStr, out ulong messageId)) {
                Log.Warning("[quote-import] Failed to import quote {Id}: invalid message ID", id);
                await FollowupAsync($"⚠️ Failed to import quote {id}! (invalid message ID)");
                return false;
            }
            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(time);

            if (string.IsNullOrWhiteSpace(channelName)) {
                await Infer(nick, id, messageId, timestamp, channelName, text);
                return true;
            }

            try {
                IMessageChannel? channel =
                    Context.Guild.TextChannels.FirstOrDefault(ch => ch.Name == channelName) ??
                    Context.Guild.StageChannels.FirstOrDefault(ch => ch.Name == channelName) ??
                    Context.Guild.VoiceChannels.FirstOrDefault(ch => ch.Name == channelName);
                if (channel is null) {
                    Log.Warning("[quote-import] Failed to import quote {Id}: channel {Ch} not found", id, channelName);
                    await FollowupAsync($"⚠️ Failed to import quote {id}! (channel {channelName} not found)");
                    return false;
                }

                IMessage? message = await channel.GetMessageAsync(messageId);
                if (message is null) {
                    Log.Warning("[quote-import] Failed to import quote {Id}: message {Msg} not found", id, messageId);
                    await FollowupAsync($"⚠️ Failed to import quote {id}! (message {messageId} not found)");
                    return false;
                }

                IUser? quoter = await message.GetReactionUsersAsync(new Emoji("\ud83d\udcac"), 20).Flatten()
                    .FirstOrDefaultAsync(user => !user.IsBot);
                if (quoter is null) {
                    const ulong uberBotUserId = 85614143951892480;
                    IMessage? uberResponse = await channel.GetMessagesAsync(message, Direction.After, 40)
                        .Flatten()
                        .Where(msg =>
                            msg.Author.Id == uberBotUserId &&
                            msg.Content.StartsWith("New quote added by ", StringComparison.Ordinal) &&
                            msg.Content.Contains($" as #{id} ")).FirstOrDefaultAsync();
                    if (uberResponse is not null) {
                        Regex regex = new($"New quote added by (.*?) as #{id} ");
                        quoter = await InferUser("quoter", regex.Match(uberResponse.Content).Groups[1].Value, id);
                    }
                }
                if (quoter is null) {
                    Log.Warning("[quote-import] Failed to find quoter of quote {Id}", id);
                    await FollowupAsync($"⚠️ Failed to find quoter of quote {id}!");
                }

                Quote quote = await Util.MessageToQuote(quoter?.Id ?? 0, id, message, timestamp);
                await AddQuote(await InferManual(quote, id, nick));
                return true;
            }
            catch (Exception ex) {
                Log.Warning(ex, "Failed to import quote {Id}: could not access channel or message", id);
                await FollowupAsync($"⚠️ Failed to import quote {id}! (could not access channel or message)");
                return false;
            }
        }
        private async Task Infer(string nick, string id, ulong messageId, DateTimeOffset timestamp,
            string channelName, string text) {
            RestGuildUser? user = await InferUser("author", nick, id);

            Log.Warning("[quote-import] Quote {Id} imported with potentially missing data", id);
            await FollowupAsync($"⚠️ Quote {id} imported with potentially missing data!");

            await AddQuote(new Quote {
                name = id,
                messageId = messageId,
                channelId = 0,
                createdAt = timestamp,
                lastEditedAt = timestamp,
                quoterId = 0,

                authorId = user?.Id ?? 0,
                replyAuthorId = 0,
                jumpUrl = string.IsNullOrWhiteSpace(channelName) ? null : $"#{channelName}",

                images = [],
                extraAttachments = 0,

                content = text
            });
        }
        private async Task<RestGuildUser?> InferUser(string who, string nick, string id) {
            RestGuildUser? user;
            try {
                string searchNick = nick.ToLowerInvariant();
                user = (await Context.Guild.SearchUsersAsync(searchNick)).FirstOrDefault();
                if (user is null) {
                    searchNick = searchNick[..(searchNick.Length / 2)];
                    user = (await Context.Guild.SearchUsersAsync(searchNick)).FirstOrDefault();
                }
                if (user is null) {
                    Log.Warning("[quote-import] Could not get {Who} {Nick} for quote {Id}: user is null", who, nick, id);
                    await FollowupAsync($"⚠️ Could not get {who} {nick} for quote {id}! (user is null)");
                }
                else {
                    Log.Information("[quote-import] Quote {Id} {Who} inferred as {User}", id, who, user.DisplayName);
                    await FollowupAsync($"🗒️ Quote {id} {who} inferred as {user.Mention}",
                        allowedMentions: AllowedMentions.None);
                }
            }
            catch (Exception ex) {
                Log.Warning(ex, "[quote-import] Could not get {Who} {Nick} for quote {Id}", who, nick, id);
                await FollowupAsync($"⚠️ Could not get {who} {nick} for quote {id}!");
                user = null;
            }
            return user;
        }
        private async Task<ulong> InferUserId(string who, string text, string id) {
            ulong user = 0;
            try {
                if (text.StartsWith("<@", StringComparison.Ordinal) && text.EndsWith('>') &&
                    ulong.TryParse(text[2..^1], out ulong userId))
                    user = userId;
                if (user == 0) {
                    Log.Warning("[quote-import] Could not get {Who} {Text} for quote {Id}: user is 0", who, text, id);
                    await FollowupAsync($"⚠️ Could not get {who} {text} for quote {id}! (user is 0)");
                }
                else {
                    Log.Information("[quote-import] Quote {Id} {Who} inferred as {User}", id, who, user);
                    await FollowupAsync($"🗒️ Quote {id} {who} inferred as <@{user}>",
                        allowedMentions: AllowedMentions.None);
                }
            }
            catch (Exception ex) {
                Log.Warning(ex, "[quote-import] Could not get {Who} {Text} for quote {Id}", who, text, id);
                await FollowupAsync($"⚠️ Could not get {who} {text} for quote {id}!");
            }
            return user;
        }
        private async Task<Quote> InferManual(Quote quote, string id, string nick) {
            Match quoteMatch = QuoteAddRegex().Match(quote.content);
            if (!quoteMatch.Success)
                return quote;
            ulong userId = await InferUserId("author", nick, id);
            if (userId == 0)
                userId = await InferUserId("author", quoteMatch.Groups[2].Value, id);
            if (userId == 0)
                userId = (await InferUser("author", quoteMatch.Groups[2].Value, id))?.Id ?? 0;
            quote = quote with {
                content = quoteMatch.Groups[1].Value,
                authorId = userId
            };
            return quote;
        }

        private async Task AddQuote(Quote quote) {
            // override existing quote
            if (await db.quotes.FirstOrDefaultAsync(q => q.name == quote.name) is { } oldQuote)
                db.Remove(oldQuote);
            db.Add(quote);
        }
    }
}
