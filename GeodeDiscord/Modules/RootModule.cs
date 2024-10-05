using Discord;
using Discord.Interactions;

using JetBrains.Annotations;

namespace GeodeDiscord.Modules;

[UsedImplicitly]
public partial class RootModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand("crash", "my mod is crashing"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Crash() {
        await RespondAsync("https://media.discordapp.net/attachments/979352389985390603/1159030798406647939/this_mod_is_crashing_I_will_not_give_crashlog.jpg?ex=65c3328c&is=65b0bd8c&hm=bbad96287a6d6144e6353e4851f7e52285802d7c5628f63bd196e8c383837b45&=&format=webp");
    }
}
