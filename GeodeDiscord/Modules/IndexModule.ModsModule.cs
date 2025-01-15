using Discord;
using Discord.Interactions;
using JetBrains.Annotations;

using GeodeDiscord.Database;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.IO.Compression;
using System.Text;

namespace GeodeDiscord.Modules;

public partial class IndexModule {
    [Group("mods", "Interact with your mods on the Geode index."), UsedImplicitly]
    public class ModsModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        [SlashCommand("create", "Create a new mod on the Geode index."), UsedImplicitly]
        public async Task Create([Summary(null, "Download link to the .geode file.")] string downloadLink) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.PostAsync(GetAPIEndpoint("/v1/mods"),
                new StringContent(JsonConvert.SerializeObject(new { download_link = downloadLink }),
                Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while creating your mod"), ephemeral: true);
                return;
            }

            await RespondAsync("✅ Successfully created your mod!", ephemeral: true);
        }

        [SlashCommand("update", "Update an existing mod on the Geode index."), UsedImplicitly]
        public async Task Update([Summary(null, "Download link to the .geode file.")] string downloadLink) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            using var zipResponse = await httpClient.GetAsync(downloadLink);
            if (!zipResponse.IsSuccessStatusCode) {
                await RespondAsync("❌ An error occurred while downloading your mod.", ephemeral: true);
                return;
            }

            var zipStream = await zipResponse.Content.ReadAsStreamAsync();
            var zipArchive = new ZipArchive(zipStream);
            var geodeEntry = zipArchive.GetEntry("mod.json");
            if (geodeEntry is null) {
                await RespondAsync("❌ Your mod does not contain a mod.json file.", ephemeral: true);
                return;
            }

            var geodeStream = geodeEntry.Open();
            var geodeReader = new StreamReader(geodeStream);
            var geodeJson = await geodeReader.ReadToEndAsync();
            var geodeData = JsonConvert.DeserializeObject<JObject>(geodeJson);
            if (geodeData is null || geodeData["id"] is null) {
                await RespondAsync("❌ Your mod.json file is missing the \"id\" field.", ephemeral: true);
                return;
            }

            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.PostAsync(GetAPIEndpoint($"/v1/mods/{geodeData["id"]}/versions"),
                new StringContent(JsonConvert.SerializeObject(new { download_link = downloadLink }),
                Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while updating your mod"), ephemeral: true);
                return;
            }

            await RespondAsync("✅ Successfully updated your mod!", ephemeral: true);
        }

        [SlashCommand("add-dev", "Add a developer to an existing mod on the Geode index."), UsedImplicitly]
        public async Task AddDeveloper([Summary(null, "ID of the mod.")] string modId,
            [Summary(null, "Username of the developer.")] string developer) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.PostAsync(GetAPIEndpoint($"/v1/mods/{modId}/developers"),
                new StringContent(JsonConvert.SerializeObject(new { username = developer }), Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while adding the developer"), ephemeral: true);
                return;
            }

            await RespondAsync("✅ Successfully added developer to mod!", ephemeral: true);
        }

        [SlashCommand("remove-dev", "Remove a developer from an existing mod on the Geode index."), UsedImplicitly]
        public async Task RemoveDeveloper([Summary(null, "ID of the mod.")] string modId,
            [Summary(null, "Username of the developer.")] string developer) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.DeleteAsync(GetAPIEndpoint($"/v1/mods/{modId}/developers/{developer}"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while removing the developer"), ephemeral: true);
                return;
            }

            await RespondAsync("✅ Successfully removed developer from mod!", ephemeral: true);
        }

        [SlashCommand("pending", "View your pending mods on the Geode index."), UsedImplicitly]
        public async Task Pending() {
            await GoToPage(0, false, true);
        }

        [SlashCommand("published", "View your published mods on the Geode index."), UsedImplicitly]
        public async Task Published() {
            await GoToPage(0, true, true);
        }

        [ComponentInteraction("previous:*"), UsedImplicitly]
        public async Task PreviousPage(string command) {
            await DeferAsync(ephemeral: true);

            var message = await Context.Interaction.GetOriginalResponseAsync();

            var embeds = message.Embeds;
            if (embeds.Count == 0) return;

            var footer = embeds.ElementAt(0).Footer;
            if (footer is null) return;

            var splitFooter = footer.Value.Text.Split(' ');
            var page = int.Parse(splitFooter[1]) - 2;
            var total = int.Parse(splitFooter[3]);
            await GoToPage(page >= 0 ? page : total - 1, command.EndsWith("published"), false);
        }

        [ComponentInteraction("next:*"), UsedImplicitly]
        public async Task NextPage(string command) {
            await DeferAsync(ephemeral: true);

            var message = await Context.Interaction.GetOriginalResponseAsync();

            var embeds = message.Embeds;
            if (embeds.Count == 0) return;

            var footer = embeds.ElementAt(0).Footer;
            if (footer is null) return;

            var splitFooter = footer.Value.Text.Split(' ');
            var page = int.Parse(splitFooter[1]);
            var total = int.Parse(splitFooter[3]);
            await GoToPage(page < total ? page : 0, command.EndsWith("published"), false);
        }

        [ComponentInteraction("select:*"), UsedImplicitly]
        public async Task SelectMod(string command, string page) {
            await DeferAsync(ephemeral: true);

            await GoToPage(int.Parse(page), command.EndsWith("published"), false);
        }

        public async Task GoToPage(int page, bool published, bool respond) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                if (respond) await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var modType = published ? "published" : "pending";

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.GetAsync(
                GetAPIEndpoint($"/v1/me/mods?status={(published ? "accepted" : "pending")}"));
            if (!response.IsSuccessStatusCode) {
                if (respond) await RespondAsync(
                    await GetError(response, $"An error occurred while getting your {modType} mods"), ephemeral: true);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(json);
            if (data is null || data["payload"] is null) {
                if (respond) await RespondAsync(
                    $"❌ An error occurred while parsing your {modType} mods response.", ephemeral: true);
                return;
            }

            if (data["payload"]?.Count() == 0) {
                if (respond) await RespondAsync($"❌ You have no {modType} mods!", ephemeral: true);
                return;
            }

            var mods = data["payload"]?.ToList();
            if (mods is null) {
                if (respond) await RespondAsync(
                    $"❌ An error occurred while parsing your {modType} mods response.", ephemeral: true);
                return;
            }

            mods.Sort((a, b) => a["id"]?.ToString().CompareTo(b["id"]?.ToString()) ?? 0);

            var components = new ComponentBuilder()
                .WithButton("Previous", $"index/mods/previous:{modType}", ButtonStyle.Primary, new Emoji("◀️"))
                .WithButton("Next", $"index/mods/next:{modType}", ButtonStyle.Primary, new Emoji("▶️"))
                .WithSelectMenu($"index/mods/select:{modType}", mods.Select(mod =>
                    new SelectMenuOptionBuilder()
                        .WithLabel(mod["versions"]?[0]?["name"]?.ToString())
                        .WithValue(mods.FindIndex(m =>
                            m["id"]?.ToString() == mod["id"]?.ToString()).ToString())).ToList(), "Select a mod...");

            if (page >= mods.Count) page = mods.Count - 1;

            var mod = mods[page];
            if (mod is null) return;

            var embed = new EmbedBuilder()
                .WithTitle((mod["featured"]?.Value<bool>() ?? false ? "⭐️ " : "") + mod["versions"]?[0]?["name"]?.ToString())
                .AddField($"{modType[0].ToString().ToUpper() + modType[1..]} versions",
                    string.Join(", ", mod["versions"]?.Select(v => $"`{v["version"]}`") ?? []))
                .WithThumbnailUrl(GetAPIEndpoint($"/v1/mods/{mod["id"]}/logo"))
                .WithFooter($"Page {page + 1} of {mods.Count}")
                .WithUrl($"https://geode-sdk.org/mods/{mod["id"]}");

            if (respond) await RespondAsync(embed: embed.Build(), components: components.Build(), ephemeral: true);
            else await ModifyOriginalResponseAsync(p => p.Embeds = new[] { embed.Build() });
        }
    }
}
