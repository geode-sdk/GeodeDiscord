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

[Group("sticky", "Give roles that save after rejoining the server."),
 DefaultMemberPermissions(GuildPermission.ManageRoles), CommandContextType(InteractionContextType.Guild),
 UsedImplicitly]
public class StickyModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
    public static Task OnUserJoined(SocketGuildUser user, ApplicationDbContext db) {
        List<StickyRole> roles = db.stickyRoles.Where(sr => sr.userId == user.Id).ToList();
        if (roles.Count == 0)
            return Task.CompletedTask;

        Log.Information("Restoring sticky roles for {User}", user);
        foreach (StickyRole role in roles) {
            if (user.Guild.GetRole(role.roleId) is not IRole r) {
                Log.Warning("Role {RoleId} not found", role.roleId);
                continue;
            }
            try { user.AddRoleAsync(r); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to add role to user");
            }
        }

        return Task.CompletedTask;
    }

    [SlashCommand("add", "Adds a sticky role to the user."), UsedImplicitly]
    public async Task Add([Autocomplete(typeof(RoleAutocompleteHandler))] string role,
        [Summary(null, "User to add the role to.")]
        IUser user) {
        if (!ulong.TryParse(role, out ulong roleId)) {
            await RespondAsync("❌ Role is invalid!", ephemeral: true);
            return;
        }

        if (Context.Guild.GetRole(roleId) is not IRole r) {
            await RespondAsync("❌ Role not found!", ephemeral: true);
            return;
        }

        if (await db.stickyRoles.AnyAsync(sr => sr.userId == user.Id && sr.roleId == roleId)) {
            await RespondAsync("❌ This user already has this role as sticky!", ephemeral: true);
            return;
        }

        if (user is IGuildUser guildUser) {
            try { await guildUser.AddRoleAsync(r); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to add role to user");
                await RespondAsync($"❌ Failed to add role to user:\n{ex.Message}", ephemeral: true);
                return;
            }
        }

        db.stickyRoles.Add(new StickyRole { userId = user.Id, roleId = roleId });
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save sticky role");
            await RespondAsync("❌ Failed to save sticky role!", ephemeral: true);
            return;
        }

        await RespondAsync(
            $"✅ Successfully added sticky role **{r.Name}** to {user.Mention}!",
            allowedMentions: AllowedMentions.None
        );
    }

    [SlashCommand("list", "List all sticky roles for user."), UsedImplicitly]
    public async Task List([Summary(null, "User to list the roles for.")] IUser user) {
        List<StickyRole> roles = await db.stickyRoles.Where(sr => sr.userId == user.Id).ToListAsync();
        if (roles.Count == 0) {
            await RespondAsync("❌ No sticky roles found!", ephemeral: true);
            return;
        }

        StringBuilder text = new();
        text.AppendLine($"📜 **{user.Username}**'s sticky roles:");
        foreach (StickyRole role in roles) {
            IRole r = Context.Guild.GetRole(role.roleId);
            if (r is null) {
                Log.Warning("Role {RoleId} not found", role.roleId);
                continue;
            }
            text.AppendLine($"- {r.Name}");
        }

        await RespondAsync(text.ToString());
    }

    [SlashCommand("remove", "Remove a sticky role from the user."), UsedImplicitly]
    public async Task Remove([Autocomplete(typeof(RoleAutocompleteHandler))] string role,
        [Summary(null, "User to remove the role from.")]
        IUser user) {
        if (!ulong.TryParse(role, out ulong roleId)) {
            await RespondAsync("❌ Role is invalid!", ephemeral: true);
            return;
        }

        if (Context.Guild.GetRole(roleId) is not IRole r) {
            await RespondAsync("❌ Role not found!", ephemeral: true);
            return;
        }

        StickyRole? sr = await db.stickyRoles.FirstOrDefaultAsync(sr => sr.userId == user.Id && sr.roleId == roleId);
        if (sr is null) {
            await RespondAsync("❌ This user does not have this role as sticky!", ephemeral: true);
            return;
        }

        if (user is IGuildUser guildUser) {
            try {
                await guildUser.RemoveRoleAsync(r);
            }
            catch (Exception ex) {
                Log.Error(ex, "Failed to remove role from user");
                await RespondAsync($"❌ Failed to remove role from user:\n{ex.Message}", ephemeral: true);
                return;
            }
        }

        db.stickyRoles.Remove(sr);
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save sticky role");
            await RespondAsync("❌ Failed to save sticky role!", ephemeral: true);
            return;
        }

        await RespondAsync(
            $"✅ Successfully removed sticky role *{r.Name}* from {user.Mention}!",
            allowedMentions: AllowedMentions.None
        );
    }

    private class RoleAutocompleteHandler : AutocompleteHandler {
        public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
            string value = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;
            ulong everyoneRoleId = context.Guild.EveryoneRole.Id;
            try {
                return Task.FromResult(AutocompletionResult.FromSuccess(context.Guild.Roles
                    .Where(r => r.Id != everyoneRoleId)
                    .Where(r => r.Name.Contains(value, StringComparison.InvariantCultureIgnoreCase))
                    .Take(25)
                    .Select(r => new AutocompleteResult(r.Name, r.Id.ToString()))));
            }
            catch (Exception ex) {
                Log.Error(ex, "Role autocomplete failed");
                return Task.FromResult(AutocompletionResult.FromError(ex));
            }
        }
    }
}
