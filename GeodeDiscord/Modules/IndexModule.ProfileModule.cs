using Discord.Interactions;
using JetBrains.Annotations;

using GeodeDiscord.Database;

using Newtonsoft.Json;

using System.Text;

namespace GeodeDiscord.Modules;

public partial class IndexModule {
    [Group("profile", "Interact with your index profile."), UsedImplicitly]
    public class ProfileModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        [SlashCommand("rename", "Change your Geode display name."), UsedImplicitly]
        public async Task Rename([Summary(null, "New display name.")] string name) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.PutAsync(GetAPIEndpoint("/v1/me"),
                new StringContent(JsonConvert.SerializeObject(new { display_name = name }), Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while updating your display name"), ephemeral: true);
                return;
            }

            // get payload.display_name
            await RespondAsync("✅ Successfully updated your display name to **" + name + "**!", ephemeral: true);
        }
    }
}
