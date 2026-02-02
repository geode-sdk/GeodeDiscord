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
    public class SensitiveModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        private async Task<bool> CheckSensitive(Quote quote) {
            if (Context.User is IGuildUser guildUser &&
                (guildUser.GuildPermissions.Has(GuildPermission.Administrator) ||
                    guildUser.Id == quote.quoterId))
                return true;
            await RespondAsync("❌ You are not the original quoter nor an admin!", ephemeral: true);
            return false;
        }

        [ComponentInteraction("rename-button:*"), UsedImplicitly]
        public async Task RenameButton(string messageId) {
            Quote? quote = await db.quotes.FindAsync(ulong.Parse(messageId));
            if (quote is null) {
                await RespondAsync("❌ Quote not found!", ephemeral: true);
                return;
            }
            if (!await CheckSensitive(quote))
                return;
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
            if (quote is null) {
                await RespondAsync("❌ Quote not found!", ephemeral: true);
                return;
            }
            if (!await CheckSensitive(quote))
                return;
            string oldName = quote.GetFullName();
            quote.name = newName.Trim();
            try { await db.SaveChangesAsync(); }
            catch (Exception ex) {
                Log.Error(ex, "[quote/sensitive] Failed to rename quote");
                await RespondAsync("❌ Failed to rename quote!", ephemeral: true);
                return;
            }
            Log.Information("[quote/sensitive] {User} renamed quote {OldName} to {NewName}", Context.User.Id, oldName,
                quote.GetFullName());
            if (shouldRespond)
                await RespondAsync($"Quote *{oldName}* renamed to **{quote.GetFullName()}**!");
            else
                await DeferAsync();
            onUpdate?.Invoke(quote, true);
        }

        [ComponentInteraction("delete-button:*"), UsedImplicitly]
        public async Task DeleteButton(string messageId) =>
            await DeleteQuote(await db.quotes.FindAsync(ulong.Parse(messageId)), false);
        [SlashCommand("delete", "Deletes a quote with the specified name."), UsedImplicitly]
        public async Task Delete([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) =>
            await DeleteQuote(await db.quotes.FirstOrDefaultAsync(q => q.id == id), true);
        private async Task DeleteQuote(Quote? quote, bool shouldRespond) {
            if (quote is null) {
                await RespondAsync("❌ Quote not found!", ephemeral: true);
                return;
            }
            if (!await CheckSensitive(quote))
                return;
            db.Remove(quote);
            try { await db.SaveChangesAsync(); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to delete quote");
                await RespondAsync("❌ Failed to delete quote!", ephemeral: true);
                return;
            }
            Log.Information("[quote/sensitive] {User} deleted quote {Name}", Context.User.Id, quote.GetFullName());
            if (shouldRespond)
                await RespondAsync($"Deleted quote *{quote.GetFullName()}*!");
            else
                await DeferAsync();
            onUpdate?.Invoke(quote, false);
        }

        [SlashCommand("update", "Updates a quote by re-fetching the message."), UsedImplicitly]
        public async Task Update([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
            Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
            if (quote is null) {
                await RespondAsync("❌ Quote not found!", ephemeral: true);
                return;
            }
            if (!await CheckSensitive(quote))
                return;

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

            db.Remove(quote);
            db.Add(await Util.MessageToQuote(db, quote.quoterId, quote.id, message, quote));

            try { await db.SaveChangesAsync(); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to update quote");
                await RespondAsync("❌ Failed to update quote!", ephemeral: true);
                return;
            }
            Log.Information("[quote/sensitive] {User} updated quote {Name}", Context.User.Id, quote.GetFullName());
            await RespondAsync($"Updated quote **{quote.GetFullName()}**!");
        }
    }
}
