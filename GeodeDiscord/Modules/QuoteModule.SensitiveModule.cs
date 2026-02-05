using Discord;
using Discord.Interactions;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace GeodeDiscord.Modules;

public partial class QuoteModule {
    [Group("sensitive", "Sensitive quote commands."), CommandContextType(InteractionContextType.Guild)]
    public class SensitiveModule(ApplicationDbContext db, QuoteEditor editor) :
        InteractionModuleBase<SocketInteractionContext> {
        private void CheckSensitive(Quote quote) {
            if (Context.User is IGuildUser guildUser &&
                (guildUser.GuildPermissions.Has(GuildPermission.Administrator) ||
                    guildUser.Id == quote.quoterId))
                return;
            throw new MessageErrorException("You are not the original quoter nor an admin!");
        }

        [ComponentInteraction("rename-button:*"), UsedImplicitly]
        public async Task RenameButton(string messageId) {
            Quote? quote = await db.quotes.FindAsync(ulong.Parse(messageId));
            if (quote is null)
                throw new MessageErrorException("Quote not found!");
            CheckSensitive(quote);
            await RespondWithModalAsync<RenameQuoteModal>($"quote/sensitive/rename-modal:{messageId}");
        }
        public class RenameQuoteModal : IModal {
            public string Title => "Rename Quote";

            [InputLabel("New Name"), ModalTextInput(nameof(newName), placeholder: "geode creepypasta", maxLength: 30),
             UsedImplicitly]
            public string newName { get; set; } = "";
        }

        [ModalInteraction("rename-modal:*"), UsedImplicitly]
        public async Task RenameModal(string messageId, RenameQuoteModal modal) =>
            await RenameQuote(await db.quotes.FindAsync(ulong.Parse(messageId)), modal.newName, false);

        [SlashCommand("rename", "Renames a quote."), UsedImplicitly]
        public async Task Rename([Autocomplete(typeof(QuoteAutocompleteHandler))] int id, string newName) =>
            await RenameQuote(await db.quotes.FirstOrDefaultAsync(q => q.id == id), newName, true);

        [SlashCommand("unname", "Removes the name from a quote."), UsedImplicitly]
        public async Task Unname([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) =>
            await RenameQuote(await db.quotes.FirstOrDefaultAsync(q => q.id == id), "", true);

        private async Task RenameQuote(Quote? quote, string newName, bool shouldRespond) {
            if (quote is null)
                throw new MessageErrorException("Quote not found!");
            CheckSensitive(quote);
            string oldName = quote.GetFullName();
            editor.Rename(quote, newName);
            await db.SaveChangesAsync();
            if (shouldRespond)
                await RespondAsync($"Quote *{oldName}* renamed to **{quote.GetFullName()}**!");
            else
                await DeferAsync();
        }

        [ComponentInteraction("delete-button:*"), UsedImplicitly]
        public async Task DeleteButton(string messageId) =>
            await DeleteQuote(await db.quotes.FindAsync(ulong.Parse(messageId)), false);
        [SlashCommand("delete", "Deletes a quote with the specified name."), UsedImplicitly]
        public async Task Delete([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) =>
            await DeleteQuote(await db.quotes.FirstOrDefaultAsync(q => q.id == id), true);
        private async Task DeleteQuote(Quote? quote, bool shouldRespond) {
            if (quote is null)
                throw new MessageErrorException("Quote not found!");
            CheckSensitive(quote);
            editor.Delete(quote);
            await db.SaveChangesAsync();
            if (shouldRespond)
                await RespondAsync($"Deleted quote *{quote.GetFullName()}*!");
            else
                await DeferAsync();
        }

        [SlashCommand("update", "Updates a quote by re-fetching the message."), UsedImplicitly]
        public async Task Update([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
            Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
            if (quote is null)
                throw new MessageErrorException("Quote not found!");
            CheckSensitive(quote);

            await editor.Update(quote);
            await db.SaveChangesAsync();

            await RespondAsync($"Updated quote **{quote.GetFullName()}**!");
        }
    }
}
