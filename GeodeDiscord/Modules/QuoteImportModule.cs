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
    [SlashCommand("manual-id", "Sets the id of a quote."), CommandContextType(InteractionContextType.Guild),
     UsedImplicitly]
    public async Task ManualId([Autocomplete(typeof(QuoteModule.QuoteAutocompleteHandler))] int id, int newId) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        db.Remove(quote);
        db.Add(quote with { id = newId });
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to change quote");
            await RespondAsync("❌ Failed to change quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Quote **{id}** ID changed to `{newId}`!");
    }

    [SlashCommand("manual-quoter", "Sets the quoter of a quote."), CommandContextType(InteractionContextType.Guild),
     UsedImplicitly]
    public async Task ManualQuoter([Autocomplete(typeof(QuoteModule.QuoteAutocompleteHandler))] int id,
        IUser newQuoter) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
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
        await RespondAsync($"Quote **{quote.GetFullName()}** quoter changed to `{newQuoter.Id}`!");
    }

    [SlashCommand("manual-author", "Sets the author of a quote."), CommandContextType(InteractionContextType.Guild),
     UsedImplicitly]
    public async Task ManualAuthor([Autocomplete(typeof(QuoteModule.QuoteAutocompleteHandler))] int id,
        IUser newAuthor) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
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
        await RespondAsync($"Quote **{quote.GetFullName()}** author changed to `{newAuthor.Id}`!");
    }

    [SlashCommand("clear-last-edited", "Clears the last edited date."), CommandContextType(InteractionContextType.Guild),
     UsedImplicitly]
    public async Task ClearLastEdited([Autocomplete(typeof(QuoteModule.QuoteAutocompleteHandler))] int id) {
        Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
        if (quote is null) {
            await RespondAsync("❌ Quote not found!", ephemeral: true);
            return;
        }
        db.Remove(quote);
        db.Add(quote with { lastEditedAt = quote.createdAt });
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to change quote");
            await RespondAsync("❌ Failed to change quote!", ephemeral: true);
            return;
        }
        await RespondAsync($"Quote **{quote.GetFullName()}** last edited cleared!");
    }

    [Group("guesses", "Guesses.")]
    public partial class GuessesModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        [SlashCommand("import", "Import guesses from HAR file."),
         CommandContextType(InteractionContextType.Guild),
         UsedImplicitly]
        public async Task ImportGuesses(Attachment attachment) {
            await DeferAsync();
            Log.Information("[quote-import] Beginning guesses import from {File}", attachment.Filename);

            JsonDocument? toImport;
            try {
                await ModifyOriginalResponseAsync(prop =>
                    prop.Content = $"Importing guesses from {attachment.Filename}: downloading & deserializing JSON"
                );
                using HttpClient client = new();
                Stream data = await client.GetStreamAsync(attachment.Url);
                toImport = await JsonSerializer.DeserializeAsync<JsonDocument>(data);
            }
            catch (JsonException ex) {
                Log.Error(ex, "Failed to import guesses");
                await FollowupAsync("❌ Failed to import guesses! (failed to deserialize JSON)");
                return;
            }
            catch (Exception ex) {
                Log.Error(ex, "Failed to import guesses");
                await FollowupAsync("❌ Failed to import guesses! (unknown error)");
                return;
            }
            if (toImport is null) {
                Log.Error("Failed to import guesses: toImport is null");
                await FollowupAsync("❌ Failed to import guesses! (Deserialize returned null)");
                return;
            }

            try {
                await ModifyOriginalResponseAsync(prop =>
                    prop.Content = $"Importing guesses from {attachment.Filename}: caching quote users"
                );
                // get a list of all users that appear in quotes
                Dictionary<string, ulong> nameToId = await db.quotes
                    .Select(x => x.authorId)
                    .Distinct()
                    .ToAsyncEnumerable()
                    .SelectAwait(x => Context.Client.GetUserAsync(x))
                    .Select(x => (x?.GlobalName ?? x?.Username ?? "", x?.Id ?? 0))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Item1))
                    .ToDictionaryAsync(x => x.Item1, x => x.Item2);
                nameToId.Add("sherobrine", 645017179854471178);
                nameToId.Add("alicia", 1081356418180984952);

                JsonElement entries = toImport.RootElement.GetProperty("log").GetProperty("entries");
                int importedRequests = 0;
                int importedGuesses = 0;
                for (int i = 0; i < entries.GetArrayLength(); i++) {
                    await ModifyOriginalResponseAsync(prop =>
                        prop.Content =
                            $"Importing guesses from {attachment.Filename}: request {i}/{entries.GetArrayLength().ToString()}, {importedGuesses} guesses"
                    );
                    int imported = await ImportRequest(entries[i], nameToId);
                    if (imported < 0)
                        continue;
                    importedRequests++;
                    importedGuesses += imported;
                }

                try { await db.SaveChangesAsync(); }
                catch (Exception ex) {
                    Log.Error(ex, "Failed to import guesses");
                    await FollowupAsync("❌ Failed to import guesses! (error when writing to the database)");
                    return;
                }

                Log.Information(
                    "[quote-import] Imported {Count} guesses from {ReqCount} requests in {File}", importedGuesses,
                    importedRequests, attachment.Filename
                );
                await ModifyOriginalResponseAsync(prop =>
                    prop.Content =
                        $"Imported {importedGuesses} guesses from {importedRequests} requests in {attachment.Filename}."
                );
            }
            catch (Exception ex) {
                Log.Error(ex, "Failed to import guesses");
                await FollowupAsync("❌ Failed to import guesses! (unknown error)");
            }
        }

        private async Task<int> ImportRequest(JsonElement element, Dictionary<string, ulong> nameToId) {
            try {
                string? responseJson = element.GetProperty("response").GetProperty("content").GetProperty("text")
                    .GetString();
                if (responseJson is null) {
                    Log.Warning("[quote-import] Failed to import guesses request: messageJson is null");
                    await FollowupAsync("⚠️ Failed to import guesses request! (missing content?)");
                    return -1;
                }
                JsonDocument? response = JsonSerializer.Deserialize<JsonDocument>(responseJson);
                if (response is null) {
                    Log.Error("[quote-import] Failed to import guesses request: message is null");
                    await FollowupAsync("⚠️ Failed to import guesses! (Deserialize returned null)");
                    return -1;
                }
                JsonElement messages = response.RootElement.GetProperty("messages");
                int importedGuesses = 0;
                for (int i = 0; i < messages.GetArrayLength(); i++) {
                    for (int j = 0; j < messages[i].GetArrayLength(); j++) {
                        if (await ImportGuess(messages[i][j], nameToId))
                            importedGuesses++;
                    }
                }
                return importedGuesses;
            }
            catch (Exception ex) {
                Log.Warning(ex, "[quote-import] Failed to import guesses request");
                await FollowupAsync("⚠️ Failed to import guesses request! (unknown error)");
                return -1;
            }
        }

        // stupid idiot.
        // ReSharper disable once CognitiveComplexity
        private async Task<bool> ImportGuess(JsonElement element, Dictionary<string, ulong> nameToId) {
            ulong messageId = 0;
            string channelId = "";
            try {
                channelId = element.GetProperty("channel_id").GetString() ?? "";
                // skip staff chat, i used it for testing too much
                if (channelId == "985609213051015268")
                    return false;
                messageId = ulong.Parse(element.GetProperty("id").GetString() ?? "");
                // not exactly accurate but it doesnt matter
                DateTimeOffset guessedAt = element.GetProperty("timestamp").GetDateTimeOffset();
                string botContent = element.GetProperty("content").GetString() ?? "";
                bool correct = botContent.Contains('✅') || botContent.Contains("🔥");
                bool timeout = botContent.Contains("Time's up") || botContent.Contains("🕛") ||
                    botContent.Contains("TOO LONG") || botContent.Contains("too long");
                ulong showedId = 0;
                if (!correct && !timeout && !TryParseShowedId(element, nameToId, out showedId)) {
                    Log.Warning("[quote-import] Failed to import guess {Id}: could not parse showed id", messageId);
                    await FollowupAsync(
                        $"⚠️ Failed to import guess {channelId}/{messageId}! (Could not parse showed id)"
                    );
                    return false;
                }
                ulong userId = ulong.Parse(
                    element.GetProperty("interaction_metadata").GetProperty("user").GetProperty("id").GetString() ?? ""
                );
                string content = ContentRegex()
                    .Match(element.GetProperty("embeds")[0].GetProperty("description").GetString() ?? "").Groups[1]
                    .Value;
                string image = ParseImage(element);
                string name = element.GetProperty("embeds")[0].GetProperty("author").GetProperty("name").GetString() ?? "";
                DateTimeOffset timestamp = default;
                if (element.GetProperty("embeds")[0].TryGetProperty("timestamp", out JsonElement timestampJson))
                    timestamp = timestampJson.GetDateTimeOffset();
                Quote? quote = await db.quotes.FirstOrDefaultAsync(x =>
                    x.createdAt == timestamp ||
                    x.content != "" && x.content.Trim() == content.Trim() ||
                    x.images != "" && image != "" && x.images.StartsWith(image) ||
                    name.StartsWith(x.id.ToString() + ":") ||
                    name == x.id.ToString() ||
                    x.name != "" && name.EndsWith(x.name)
                );
                if (quote is null) {
                    Log.Warning("[quote-import] Failed to import guess {Id}: quote is null", messageId);
                    await FollowupAsync($"⚠️ Failed to import guess {channelId}/{messageId}! (Could not find quote)");
                    return false;
                }
                db.Add(new Guess {
                    messageId = messageId,
                    startedAt = DateTimeOffset.MinValue,
                    guessedAt = guessedAt,
                    userId = userId,
                    guessId = correct ? quote.authorId : timeout || showedId == quote.authorId ? 0 : showedId,
                    quote = quote
                });
                return true;
            }
            catch (Exception ex) {
                Log.Warning(ex, "[quote-import] Failed to import guess {Id}", messageId);
                await FollowupAsync($"⚠️ Failed to import guess {channelId}/{messageId}! (unknown error)");
                return false;
            }
        }

        private static string ParseImage(JsonElement element) {
            if (!element.GetProperty("embeds")[0].TryGetProperty("image", out JsonElement imageJson))
                return "";
            string image = imageJson.GetProperty("url").GetString() ?? "";
            int index = image.IndexOf('?');
            if (index != -1)
                image = image[..index];
            return image;
        }

        private static bool TryParseShowedId(JsonElement element, Dictionary<string, ulong> nameToId, out ulong showedId) {
            showedId = 0;
            Match? showedIdMatch = ShowedIdRegex().Matches(element.GetProperty("content").GetString() ?? "")
                .LastOrDefault();
            if (showedIdMatch is not null && showedIdMatch.Success && showedIdMatch.Groups[1].Success) {
                showedId = ulong.Parse(showedIdMatch.Groups[1].ValueSpan);
            }
            else {
                string? showedName = ShowedNameRegex().Matches(element.GetProperty("content").GetString() ?? "")
                    .LastOrDefault()?.Groups[1].Value;
                if (showedName is null || !nameToId.TryGetValue(showedName, out showedId))
                    return false;
            }
            return true;
        }

        [GeneratedRegex("(?:by|not by|not) <@(.*?)>")]
        private static partial Regex ShowedIdRegex();

        [GeneratedRegex("(?:by|not by|not) [`*](.*?)[`*]")]
        private static partial Regex ShowedNameRegex();

        [GeneratedRegex(@"(.*)(?:\n\n?)", RegexOptions.Singleline)]
        private static partial Regex ContentRegex();

        private readonly Dictionary<ulong, IMessage?> _messageCache = [];

        [SlashCommand("import-timestamps-from-dms", "Import timestamps for all guesses in DMs with the specified user."),
         CommandContextType(InteractionContextType.Guild),
         UsedImplicitly]
        private async Task ImportTimestampsFromDms(IUser user) {
            await DeferAsync();
            Log.Information("[quote-import] Beginning guess timestamps import from DMs with {User}", user.Id);

            await ModifyOriginalResponseAsync(prop => {
                prop.Content = $"Importing guess timestamps from DMs with <@{user.Id}>: querying guesses";
                prop.AllowedMentions = AllowedMentions.None;
            });
            List<Guess> guesses = await db.guesses
                .Where(x => x.startedAt == default(DateTimeOffset) && x.userId == user.Id)
                .AsAsyncEnumerable()
                .OrderBy(x => x.messageId)
                .ToListAsync();

            await ImportTimestamps(await user.CreateDMChannelAsync(), guesses);
        }

        // please may God forgive me
        [SlashCommand("import-timestamps", "Import timestamps for all guesses in the specified channel."),
         CommandContextType(InteractionContextType.Guild),
         UsedImplicitly]
        private async Task ImportTimestamps(IMessageChannel channel, string firstGuessMessageId, string lastGuessMessageId) {
            await DeferAsync();
            Log.Information("[quote-import] Beginning guess timestamps import from {Channel}", channel.Id);

            ulong startId = ulong.Parse(firstGuessMessageId);
            ulong endId = ulong.Parse(lastGuessMessageId);

            await ModifyOriginalResponseAsync(prop =>
                prop.Content = $"Importing guess timestamps from <#{channel.Id}>: querying guesses"
            );
            List<Guess> guesses = await db.guesses
                .Where(x => x.startedAt == default(DateTimeOffset))
                .AsAsyncEnumerable()
                .OrderBy(x => x.messageId)
                .Where(x => x.messageId >= startId && x.messageId <= endId)
                .ToListAsync();

            // cache 1000 messages after the specified one
            await ModifyOriginalResponseAsync(prop =>
                prop.Content = $"Importing guess timestamps from <#{channel.Id}>: caching messages"
            );
            foreach (IMessage msg in await channel.GetMessagesAsync(startId, Direction.After, 1000).FlattenAsync()) {
                _messageCache.TryAdd(msg.Id, msg);
            }

            await ImportTimestamps(channel, guesses);
        }

        private async Task ImportTimestamps(IMessageChannel channel, List<Guess> guesses) {
            // guesses are sorted by messageId
            ulong endId = guesses.Last().messageId;

            int totalGuessCount = guesses.Count;
            int guessCount = guesses.Count;
            int imported = 0;
            HashSet<ulong> skip = [];
            int skip2 = 0;
            ulong lastSkipped = 0;
            while (guesses.Count - skip2 > 0) {
                Guess? first = guesses.Skip(skip2).FirstOrDefault(x => x.messageId > lastSkipped);
                if (first is null) {
                    first = guesses.Skip(skip2).First();
                    await ImportSingleGuessLocal(first);
                    skip2++;
                }
                else {
                    await ImportSingleGuessLocal(first);
                    guesses.Remove(first);
                }
                foreach (Guess guess in guesses.Skip(skip2).Where(x => _messageCache.ContainsKey(x.messageId))) {
                    await ImportSingleGuessLocal(guess);
                    skip.Add(guess.messageId);
                    lastSkipped = guess.messageId;
                }
                if (skip.Contains(endId) || guesses.Count > skip2 && guesses.Skip(skip2).First().messageId > endId)
                    break;
                if (skip.Count == 0)
                    continue;
                guesses.RemoveAll(x => skip.Contains(x.messageId));
                skip.Clear();
            }

            try { await db.SaveChangesAsync(); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to import guess timestamps");
                await FollowupAsync("❌ Failed to import guess timestamps! (error when writing to the database)");
                return;
            }

            Log.Information(
                "[quote-import] Imported {Count}/{Total} guess timestamps from {Channel}",
                imported, guessCount, channel.Id
            );
            await ModifyOriginalResponseAsync(prop =>
                prop.Content = $"Imported {imported}/{guessCount} guess timestamps from <#{channel.Id}>."
            );

            return;

            async Task ImportSingleGuessLocal(Guess guess) {
                try {
                    if ((totalGuessCount - guessCount + imported) % 10 == 0) {
                        await ModifyOriginalResponseAsync(prop =>
                            prop.Content = $"Importing guess timestamps from <#{channel.Id}>: {imported}/{guessCount} guesses"
                        );
                    }

                    int result = await ImportSingleGuessTimestamps(channel, guess);
                    if (result <= 0) {
                        guessCount += result;
                        return;
                    }
                    imported += result;
                }
                catch (Exception ex) {
                    Log.Warning(ex, "[quote-import] Failed to import guess {Id} timestamps", guess.messageId);
                    await FollowupAsync($"⚠️ Failed to import guess {guess.messageId} timestamps! (unknown error)");
                }
            }
        }

        private async Task<int> ImportSingleGuessTimestamps(IMessageChannel channel, Guess guess) {
            if (!_messageCache.TryGetValue(guess.messageId, out IMessage? message)) {
                message = await channel.GetMessageAsync(guess.messageId);
                _messageCache.TryAdd(guess.messageId, message);
                if (message is not null) {
                    foreach (IMessage msg in await channel.GetMessagesAsync(message, Direction.After).FirstAsync()) {
                        _messageCache.TryAdd(msg.Id, msg);
                    }
                }
            }
            if (message is null) {
                return -1;
            }

            DateTimeOffset startedAt;
            DateTimeOffset guessedAt;
            if (message.EditedTimestamp is null) {
                ulong refId = message.Reference.MessageId.GetValueOrDefault(0);
                if (!_messageCache.TryGetValue(refId, out IMessage? reference)) {
                    reference = await channel.GetMessageAsync(refId);
                    _messageCache.TryAdd(refId, reference);
                    if (reference is not null) {
                        foreach (IMessage msg in await channel.GetMessagesAsync(reference, Direction.After).FirstAsync())
                            _messageCache.TryAdd(msg.Id, msg);
                    }
                }
                if (reference is null) {
                    Log.Warning("[quote-import] Failed to import guess {Id} timestamps", guess.messageId);
                    await FollowupAsync($"⚠️ Failed to import guess {guess.messageId} timestamps! (message is not edited and isn't a reply)");
                    return 0;
                }

                if (!reference.Content.Contains("Who said this?", StringComparison.OrdinalIgnoreCase)) {
                    Log.Warning("[quote-import] Inferred guess {Id} start ({StartId}) doesn't contain \"Who said this?\"", guess.messageId, reference.Id);
                    await FollowupAsync($"⚠️ Inferred guess {guess.messageId} start ({reference.Id}) doesn't contain \"Who said this?\"!");
                }

                startedAt = reference.Timestamp;
                guessedAt = message.Timestamp;
            }
            else {
                startedAt = message.Timestamp;
                guessedAt = message.EditedTimestamp.GetValueOrDefault();
            }

            // check if stored guessed at is closer to inferred guessed at than to inferred guessed at
            // if it is, we don't need to override it as it's probably more accurate
            TimeSpan distToStarted = (guess.guessedAt - startedAt).Duration();
            TimeSpan distToGuessed = (guess.guessedAt - guessedAt).Duration();
            if (distToGuessed < distToStarted || guessedAt - startedAt > TimeSpan.FromSeconds(60.0)) {
                // stored guessed at is closer to inferred guessed at than to started at
                // so it's probably more accurate than what we inferred from message timestamps
                guessedAt = guess.guessedAt;
            }

            db.Remove(guess);
            db.Update(guess with {
                startedAt = startedAt,
                guessedAt = guessedAt
            });

            return 1;
        }

        [SlashCommand("clear-message-cache", "Clear message cache of import-timestamps."),
         CommandContextType(InteractionContextType.Guild),
         UsedImplicitly]
        private async Task ClearMessageCache() {
            _messageCache.Clear();
            await RespondAsync("Cleared message cache.");
        }

        [SlashCommand("count-invalid-timestamps", "Count amount of invalid guess timestamps."),
         CommandContextType(InteractionContextType.Guild),
         UsedImplicitly]
        private async Task CountInvalidTimestamps() {
            int count = await db.guesses
                .ToAsyncEnumerable()
                .CountAsync(x => x.guessedAt - x.startedAt > TimeSpan.FromSeconds(60.0));
            await RespondAsync($"{count}/{await db.guesses.CountAsync()} guesses have invalid timestamps bc i fucked up sorgy");
        }
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

        // stupid idiot.
        // ReSharper disable once CognitiveComplexity
        private async Task<bool> ImportSingle(UberBotQuote oldQuote) {
            (string idStr, string nick, string channelName, string messageIdStr, string text, long time) = oldQuote;
            if (!int.TryParse(idStr, out int id)) {
                Log.Warning("[quote-import] Failed to import quote {Id}: invalid ID", idStr);
                await FollowupAsync($"⚠️ Failed to import quote {idStr}! (invalid ID)");
                return false;
            }
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
        private async Task Infer(string nick, int id, ulong messageId, DateTimeOffset timestamp,
            string channelName, string text) {
            RestGuildUser? user = await InferUser("author", nick, id);

            Log.Warning("[quote-import] Quote {Id} imported with potentially missing data", id);
            await FollowupAsync($"⚠️ Quote {id} imported with potentially missing data!");

            await AddQuote(new Quote {
                id = id,
                messageId = messageId,
                channelId = 0,
                createdAt = timestamp,
                lastEditedAt = timestamp,
                quoterId = 0,

                authorId = user?.Id ?? 0,
                replyAuthorId = 0,
                jumpUrl = string.IsNullOrWhiteSpace(channelName) ? null : $"#{channelName}",

                images = "",
                videos = "",
                extraAttachments = 0,

                content = text
            });
        }
        private async Task<RestGuildUser?> InferUser(string who, string nick, int id) {
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
        private async Task<ulong> InferUserId(string who, string text, int id) {
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
        private async Task<Quote> InferManual(Quote quote, int id, string nick) {
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
            if (await db.quotes.FirstOrDefaultAsync(q => q.id == quote.id) is { } oldQuote)
                db.Remove(oldQuote);
            db.Add(quote);
        }
    }
}
