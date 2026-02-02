using Discord;
using Discord.Interactions;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace GeodeDiscord.Modules;

[Group("quote-import", "Import quotes."), DefaultMemberPermissions(GuildPermission.Administrator), CommandContextType(InteractionContextType.Guild)]
public class QuoteImportModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
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

#pragma warning disable CA1816
    private class ImportProcess(SocketInteractionContext context, string name) {
        public static async Task<ImportProcess> Start(SocketInteractionContext context, string name, string clarifier) {
            await context.Interaction.DeferAsync();
            ImportProcess process = new(context, name);
            if (!string.IsNullOrWhiteSpace(clarifier))
                process.Clarify(clarifier);
            Log.Information(
                "[quote-import] Beginning {Message}",
                string.Join(' ', [name, "import", ..process._clarifiers])
            );
            return process;
        }

        private readonly Stack<string> _clarifiers = [];
        public Clarifier Clarify(string clarifier) {
            _clarifiers.Push(clarifier);
            return new Clarifier(this);
        }
        public class Clarifier(ImportProcess process) : IDisposable {
            public void Dispose() => process._clarifiers.Pop();
        }

        public async Task ReportProgress(string message) {
            string clarified = string.Join(' ', [name, .._clarifiers.Take(1)]);
            await context.Interaction.ModifyOriginalResponseAsync(prop =>
                prop.Content = $"Importing ${clarified}: {message}"
            );
        }

        public async Task ReportWarning(string message, string reason, Exception? exception = null) {
            if (exception is null)
                Log.Warning("[quote-import] {Message}: {Reason}", message, reason);
            else
                Log.Warning(exception, "[quote-import] {Message}", message);
            await context.Interaction.FollowupAsync($"⚠️ ${message}! ({reason})");
        }

        public async Task ReportFail(string reason, Exception? exception = null) {
            string clarified = string.Join(' ', [name, .._clarifiers]);
            await ReportWarning($"Failed to import {clarified}", reason, exception);
        }

        public async Task Fail(string reason, Exception? exception) {
            string clarified = string.Join(' ', [name, .._clarifiers]);
            if (exception is null)
                Log.Error("[quote-import] Failed to import {Name}: {Reason}", clarified, reason);
            else
                Log.Error(exception, "[quote-import] Failed to import {Name}", clarified);
            await context.Interaction.FollowupAsync($"❌ Failed to import ${clarified}! ({reason})");
        }

        public async Task Success(string message) {
            Log.Information("[quote-import] {Message}", message);
            await context.Interaction.ModifyOriginalResponseAsync(prop => prop.Content = $"{message}.");
        }
    }
#pragma warning restore CA1816

    private Task<ImportProcess> StartImport(string name, string clarifier = "") =>
        ImportProcess.Start(Context, name, clarifier);

    [SlashCommand("import-reply-contents", "Import reply contents."),
     CommandContextType(InteractionContextType.Guild),
     UsedImplicitly]
    public async Task ImportReplyContents() {
        ImportProcess process = await StartImport("reply contents");

        await process.ReportProgress("querying quotes");
        List<Quote> quotes = await db.quotes
            .Where(x => x.replyAuthorId != 0 && x.replyMessageId == 0)
            .ToListAsync();

        int imported = 0;
        foreach (Quote quote in quotes) {
            using ImportProcess.Clarifier quoteClarifier = process.Clarify($"for quote {quote.id}");
            try {
                if (imported % 10 == 0) {
                    await process.ReportProgress($"{imported}/{quotes.Count} quotes");
                }

                if (await Util.GetChannelAsync(Context.Client, quote.channelId) is not IMessageChannel channel) {
                    await process.ReportFail("channel not found");
                    continue;
                }

                IMessage? message = await channel.GetMessageAsync(quote.messageId);
                if (message is null) {
                    await process.ReportFail("message not found");
                    continue;
                }

                IMessage? reply = await Util.GetReplyAsync(message);
                if (reply is null) {
                    await process.ReportFail("reply not found");
                    continue;
                }

                db.Remove(quote);
                db.Add(quote with {
                    replyMessageId = reply.Id,
                    replyContent = reply.Content
                });
                imported++;
            }
            catch (Exception ex) {
                await process.ReportFail("unknown error", ex);
            }
        }

        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            await process.Fail("error when writing to the database", ex);
            return;
        }

        await process.Success($"Imported {imported}/{quotes.Count} reply contents");
    }

    [Group("guesses", "Guesses.")]
    public class GuessesModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        private Task<ImportProcess> StartImport(string name, string clarifier = "") =>
            ImportProcess.Start(Context, name, clarifier);

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
}
