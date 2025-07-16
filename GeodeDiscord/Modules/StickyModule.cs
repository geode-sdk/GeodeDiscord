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
    public static async Task OnUserJoined(SocketGuildUser user, ApplicationDbContext db) {
        await Restore(user, db);
    }

    [SlashCommand("add", "Adds a sticky role to the user."), UsedImplicitly]
    public async Task Add(IRole role, IGuildUser user) {
        if (await db.stickyRoles.AnyAsync(x => x.userId == user.Id && x.roleId == role.Id)) {
            await RespondAsync("❌ This user already has this role as sticky!", ephemeral: true);
            return;
        }

        try { await user.AddRoleAsync(role); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to add role to user");
            await RespondAsync($"❌ Failed to add role to user:\n{ex.Message}", ephemeral: true);
            return;
        }

        db.stickyRoles.Add(new StickyRole { userId = user.Id, roleId = role.Id });
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save sticky role");
            await RespondAsync("❌ Failed to save sticky role!", ephemeral: true);
            return;
        }

        await RespondAsync(
            $"✅ Successfully added sticky role {role.Mention} to {user.Mention}!",
            allowedMentions: AllowedMentions.None
        );
    }

    [SlashCommand("remove", "Remove a sticky role from the user."), UsedImplicitly]
    public async Task Remove(IRole role, IGuildUser user) {
        StickyRole? sr = await db.stickyRoles.FirstOrDefaultAsync(x => x.userId == user.Id && x.roleId == role.Id);
        if (sr is null) {
            await RespondAsync("❌ This user does not have this role as sticky!", ephemeral: true);
            return;
        }

        try { await user.RemoveRoleAsync(role); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to remove role from user");
            await RespondAsync($"❌ Failed to remove role from user:\n{ex.Message}", ephemeral: true);
            return;
        }

        db.stickyRoles.Remove(sr);
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save sticky role");
            await RespondAsync("❌ Failed to save sticky role!", ephemeral: true);
            return;
        }

        await RespondAsync(
            $"✅ Successfully removed sticky role {role.Mention} from {user.Mention}!",
            allowedMentions: AllowedMentions.None
        );
    }

    [SlashCommand("list", "List all sticky roles for user."), UsedImplicitly]
    public async Task List(IGuildUser user) {
        IEnumerable<string> lines = db.stickyRoles
            .Where(sr => sr.userId == user.Id)
            .AsEnumerable()
            .Select(x => $"- <@&{x.roleId}>");
        string list = string.Join("\n", lines);
        if (list.Length == 0) {
            await RespondAsync("❌ No sticky roles found!", ephemeral: true);
            return;
        }
        await RespondAsync($"📜 {user.Mention}'s sticky roles:\n{list}", allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("restore", "Force restore sticky roles."), UsedImplicitly]
    public async Task Restore(IGuildUser user) {
        await RespondAsync($"Restoring sticky roles for {user.Mention}", allowedMentions: AllowedMentions.None);
        await Restore(user, db, message => FollowupAsync(message, allowedMentions: AllowedMentions.None));
    }

    private static async Task Restore(IGuildUser user, ApplicationDbContext db, Func<string, Task>? message = null) {
        // no way
        IQueryable<StickyRole> srs = db.stickyRoles.Where(x => x.userId == user.Id);
        foreach (StickyRole sr in srs) {
            if (user.Guild.GetRole(sr.roleId) is not { } role) {
                Log.Warning("Role {RoleId} not found", sr.roleId);
                await Message($"⚠️ Role *{sr.roleId}* not found");
                continue;
            }
            try { await user.AddRoleAsync(role); }
            catch (Exception ex) {
                Log.Error(ex, "Failed to add role to user");
                await Message($"⚠️ Failed to add role {role.Mention}");
                continue;
            }
            await Message($"✅ Successfully restored role {role.Mention}");
            Log.Information("Restored sticky role {Role} for {User}", role, user);
        }
        return;

        async Task Message(string text) {
            if (message is not null)
                await message(text);
        }
    }
}
