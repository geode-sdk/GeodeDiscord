using Discord;
using Discord.Interactions;

using JetBrains.Annotations;

namespace GeodeDiscord.Modules;

public partial class RootModule {
    [SlashCommand("say", "Make the bot say something as you."),
     CommandContextType(InteractionContextType.Guild),
     DefaultMemberPermissions(GuildPermission.Administrator),
     UsedImplicitly]
    public async Task Say(string message) => await RespondAsync($"`@{Context.User.GlobalName}`: {message}");
}
