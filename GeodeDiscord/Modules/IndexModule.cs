using Discord.Interactions;
using JetBrains.Annotations;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GeodeDiscord.Modules;

[Group("index", "Interact with the Geode mod index."), UsedImplicitly]
public partial class IndexModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
    public static string APIEndpoint { get; } = Environment.GetEnvironmentVariable("GEODE_API") ?? "https://api.geode-sdk.org";
    public static string WebsiteEndpoint { get; } = Environment.GetEnvironmentVariable("GEODE_WEBSITE") ?? "https://geode-sdk.org";

    public static string GetAPIEndpoint(string path) => $"{APIEndpoint}{path}";
    public static string GetWebsiteEndpoint(string path) => $"{WebsiteEndpoint}{path}";

    public static async Task<string> GetError(HttpResponseMessage response, string message) {
        if (response.IsSuccessStatusCode) return "";

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonConvert.DeserializeObject<JObject>(json);
        if (data is null || data["error"] is null) {
            return $"❌ {message}.";
        }

        return $"❌ {message}: `{data["error"]?.Value<string>()}`.";
    }

    [SlashCommand("login", "Log in to your Geode account."), UsedImplicitly]
    public async Task Login([Summary(null, "Token to log in with.")] string token) {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", token);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
        using var response = await httpClient.GetAsync(GetAPIEndpoint("/v1/me"));
        if (!response.IsSuccessStatusCode) {
            await RespondAsync(await GetError(response, "An error occurred while logging in"), ephemeral: true);
            return;
        }

        var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
        if (indexToken is null) {
            indexToken = new IndexToken {
                userId = Context.User.Id,
                indexToken = token
            };
            await db.indexTokens.AddAsync(indexToken);
        } else {
            indexToken.indexToken = token;
        }

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonConvert.DeserializeObject<JObject>(json);
        if (data is null || data["payload"] is null) {
            await RespondAsync(await GetError(response, "An error occurred while parsing the user response"), ephemeral: true);
            return;
        }

        try {
            await db.SaveChangesAsync();
            await RespondAsync("✅ Successfully logged in as **" + data["payload"]?["display_name"] + "**!", ephemeral: true);
        } catch (DbUpdateException) {
            await RespondAsync("❌ An error occurred while saving your token.", ephemeral: true);
        }
    }

    [SlashCommand("logout", "Log out of your Geode account."), UsedImplicitly]
    public async Task Logout([Summary(null, "Whether to invalidate the token.")] bool invalidate = false) {
        var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
        if (indexToken is null) {
            await RespondAsync("❌ You are not logged in.", ephemeral: true);
            return;
        }

        db.indexTokens.Remove(indexToken);

        try {
            await db.SaveChangesAsync();
            if (!invalidate) {
                await RespondAsync("✅ Successfully logged out!", ephemeral: true);
                return;
            }
        } catch (DbUpdateException) {
            await RespondAsync("❌ An error occurred while updating login status.", ephemeral: true);
            return;
        }

        if (invalidate) {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.DeleteAsync(GetAPIEndpoint("/v1/me/token"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while invalidating the token"), ephemeral: true);
                return;
            }

            await RespondAsync("✅ Successfully logged out and invalidated token!", ephemeral: true);
        }
    }

    [SlashCommand("invalidate", "Log out and invalidate all tokens."), UsedImplicitly]
    public async Task Invalidate() {
        var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
        if (indexToken is null) {
            await RespondAsync("❌ You are not logged in.", ephemeral: true);
            return;
        }

        db.indexTokens.Remove(indexToken);
        try {
            await db.SaveChangesAsync();
        } catch (DbUpdateException) {
            await RespondAsync("❌ An error occurred while updating login status.", ephemeral: true);
            return;
        }

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
        using var response = await httpClient.DeleteAsync(GetAPIEndpoint("/v1/me/tokens"));
        if (!response.IsSuccessStatusCode) {
            await RespondAsync(await GetError(response, "An error occurred while invalidating tokens"), ephemeral: true);
            return;
        }

        await RespondAsync("✅ Successfully invalidated all tokens!", ephemeral: true);
    }
}
