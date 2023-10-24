using Discord;
using Discord.Interactions;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

namespace GeodeDiscord.Modules;

public partial class QuoteModule {
    [Group("sensitive", "Sensitive quote commands.")]
    public class SensitiveModule : InteractionModuleBase<SocketInteractionContext> {
        private readonly ApplicationDbContext _db;

        public SensitiveModule(ApplicationDbContext db) => _db = db;

        private async Task<bool> CheckSensitive(Quote quote) {
            if (Context.User is IGuildUser guildUser &&
                (guildUser.GuildPermissions.Has(GuildPermission.Administrator) ||
                    guildUser.Id == quote.quoterId))
                return true;
            await RespondAsync("❌ You are not the original quoter nor an admin!", ephemeral: true);
            return false;
        }

        [ComponentInteraction("rename-button:*"), UsedImplicitly]
        public async Task RenameQuoteButton(string name) {
            Quote? quote = await _db.quotes.FindAsync(name);
            if (quote is null) {
                await RespondAsync("❌ Quote not found!", ephemeral: true);
                return;
            }
            if (!await CheckSensitive(quote))
                return;
            await RespondWithModalAsync<RenameModal>($"quote/sensitive/rename-modal:{name}");
        }

        public class RenameModal : IModal {
            public string Title => "Rename Quote";

            [InputLabel("New Name"), ModalTextInput(nameof(newName), placeholder: "geode creepypasta", maxLength: 30),
             UsedImplicitly]
            public string newName { get; set; } = "";
        }

        [ModalInteraction("rename-modal:*"), UsedImplicitly]
        public async Task RenameQuoteModal(string name, RenameModal modal) =>
            await RenameQuote(name, modal.newName);

        [SlashCommand("rename", "Renames a quote with the specified name."), UsedImplicitly]
        public async Task RenameQuote([Autocomplete(typeof(QuoteAutocompleteHandler))] string oldName, string newName) {
            Quote? quote = await _db.quotes.FindAsync(oldName);
            if (quote is null) {
                await RespondAsync($"❌ Quote **{oldName}** not found!", ephemeral: true);
                return;
            }
            if (!await CheckSensitive(quote))
                return;
            if (await _db.quotes.FindAsync(newName) is not null) {
                await RespondAsync($"❌ Quote **${newName}** already exists!", ephemeral: true);
                return;
            }
            _db.Remove(quote);
            _db.Add(quote.WithName(newName));
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
                await RespondAsync("❌ Failed to rename quote!", ephemeral: true);
                return;
            }
            await RespondAsync($"Quote *{quote.name}* renamed to **{newName}**!");
        }

        [SlashCommand("delete", "Deletes a quote with the specified name."),
         ComponentInteraction("delete-button:*"), UsedImplicitly]
        public async Task DeleteQuote([Autocomplete(typeof(QuoteAutocompleteHandler))] string name) {
            Quote? quote = await _db.quotes.FindAsync(name);
            if (quote is null) {
                await RespondAsync("❌ Quote not found!", ephemeral: true);
                return;
            }
            if (!await CheckSensitive(quote))
                return;
            _db.Remove(quote);
            try { await _db.SaveChangesAsync(); }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
                await RespondAsync("❌ Failed to delete quote!", ephemeral: true);
                return;
            }
            await RespondAsync($"Deleted quote *{quote.name}*!");
        }

        [SlashCommand("update", "Updates a quote by re-fetching the message."), UsedImplicitly]
        public async Task UpdateQuote([Autocomplete(typeof(QuoteAutocompleteHandler))] string name) {
            Quote? quote = await _db.quotes.FindAsync(name);
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

            _db.Remove(quote);
            _db.Add(await Util.MessageToQuote(quote.quoterId, quote.name, message, quote));

            try { await _db.SaveChangesAsync(); }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
                await RespondAsync("❌ Failed to update quote!", ephemeral: true);
                return;
            }
            await RespondAsync($"Update quote **{quote.name}**!");
        }
    }
}
