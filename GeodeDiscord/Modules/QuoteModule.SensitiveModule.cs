using Discord;
using Discord.Interactions;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

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

        [SlashCommand("rename", "Renames a quote."), UsedImplicitly]
        public async Task Rename([Autocomplete(typeof(QuoteAutocompleteHandler))] int id, string newName) {
            Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
            if (quote is null)
                throw new MessageErrorException("Quote not found!");
            CheckSensitive(quote);
            string oldName = quote.GetFullName();
            editor.Rename(quote, newName);
            await db.SaveChangesAsync();
            await RespondAsync($"Quote *{oldName}* renamed to **{quote.GetFullName()}**!");
        }

        [SlashCommand("unname", "Removes the name from a quote."), UsedImplicitly]
        public async Task Unname([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
            Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
            if (quote is null)
                throw new MessageErrorException("Quote not found!");
            CheckSensitive(quote);
            string oldName = quote.GetFullName();
            editor.Rename(quote, "");
            await db.SaveChangesAsync();
            await RespondAsync($"Quote *{oldName}* renamed to **{quote.GetFullName()}**!");
        }

        [SlashCommand("delete", "Deletes a quote with the specified name."), UsedImplicitly]
        public async Task Delete([Autocomplete(typeof(QuoteAutocompleteHandler))] int id) {
            Quote? quote = await db.quotes.FirstOrDefaultAsync(q => q.id == id);
            if (quote is null)
                throw new MessageErrorException("Quote not found!");
            CheckSensitive(quote);
            editor.Delete(quote);
            await db.SaveChangesAsync();
            await RespondAsync($"Deleted quote *{quote.GetFullName()}*!");
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
