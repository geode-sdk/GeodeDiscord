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

        [JsonObject]
        public class SimpleModVersion {
            [JsonProperty("name")]
            public required string Name { get; set; }
            [JsonProperty("version")]
            public required string Version { get; set; }
        }

        [JsonObject]
        public class SimpleMod {
            [JsonProperty("id")]
            public required string Id { get; set; }
            [JsonProperty("featured")]
            public bool Featured { get; set; }
            [JsonProperty("versions")]
            public required List<SimpleModVersion> Versions { get; set; }
        }

        private static readonly Dictionary<ulong, List<SimpleMod>> MessageData = [];

        [SlashCommand("pending", "View your pending mods on the Geode index."), UsedImplicitly]
        public async Task Pending() {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.GetAsync(GetAPIEndpoint("/v1/me/mods?status=pending"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while getting your pending mods"), ephemeral: true);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(json);
            if (data is null || data["payload"] is null) {
                await RespondAsync("❌ An error occurred while parsing your pending mods response.", ephemeral: true);
                return;
            }

            if (data["payload"]?.Count() == 0) {
                await RespondAsync("❌ You have no pending mods!", ephemeral: true);
                return;
            }

            var mods = data["payload"]?.ToObject<List<SimpleMod>>();
            if (mods is null) {
                await RespondAsync("❌ An error occurred while parsing your pending mods response.", ephemeral: true);
                return;
            }

            var component = new ComponentBuilder()
                .WithButton("Previous", "index/mods/previous:pending", ButtonStyle.Primary, new Emoji("◀️"))
                .WithButton("Next", "index/mods/next:pending", ButtonStyle.Primary, new Emoji("▶️"))
                .WithSelectMenu("index/mods/select:pending", mods.Select(mod =>
                    new SelectMenuOptionBuilder()
                        .WithLabel(mod.Versions[0].Name)
                        .WithValue(mods.FindIndex(m => m.Id == mod.Id).ToString())).ToList(), "Select a mod...");

            await RespondAsync(components: component.Build(), ephemeral: true);

            var message = await Context.Interaction.GetOriginalResponseAsync();

            MessageData[message.Id] = mods;

            await GoToPage(0, false);
        }

        [SlashCommand("published", "View your published mods on the Geode index."), UsedImplicitly]
        public async Task Published() {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.GetAsync(GetAPIEndpoint("/v1/me/mods?status=accepted"));
            if (!response.IsSuccessStatusCode) {
                await RespondAsync(await GetError(response, "An error occurred while getting your published mods"), ephemeral: true);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(json);
            if (data is null || data["payload"] is null) {
                await RespondAsync("❌ An error occurred while parsing your published mods response.", ephemeral: true);
                return;
            }

            if (data["payload"]?.Count() == 0) {
                await RespondAsync("❌ You have no published mods!", ephemeral: true);
                return;
            }

            var mods = data["payload"]?.ToObject<List<SimpleMod>>();
            if (mods is null) {
                await RespondAsync("❌ An error occurred while parsing your published mods response.", ephemeral: true);
                return;
            }

            var component = new ComponentBuilder()
                .WithButton("Previous", "index/mods/previous:published", ButtonStyle.Primary, new Emoji("◀️"))
                .WithButton("Next", "index/mods/next:published", ButtonStyle.Primary, new Emoji("▶️"))
                .WithSelectMenu("index/mods/select:published", mods.Select(mod =>
                    new SelectMenuOptionBuilder()
                        .WithLabel(mod.Versions[0].Name)
                        .WithValue(mods.FindIndex(m => m.Id == mod.Id).ToString())).ToList(), "Select a mod...");

            await RespondAsync(components: component.Build(), ephemeral: true);

            var message = await Context.Interaction.GetOriginalResponseAsync();

            MessageData[message.Id] = mods;

            await GoToPage(0, true);
        }

        [ComponentInteraction("previous:*"), UsedImplicitly]
        public async Task PreviousPage(string command) {
            await DeferAsync(ephemeral: true);

            var message = await Context.Interaction.GetOriginalResponseAsync();
            if (!MessageData.TryGetValue(message.Id, out List<SimpleMod>? mods)) return;

            var embeds = message.Embeds;
            if (embeds.Count == 0) return;

            var footer = embeds.ElementAt(0).Footer;
            if (footer is null) return;

            var page = int.Parse(footer.Value.Text.Split(' ')[1]) - 2;
            await GoToPage(page >= 0 ? page : mods.Count - 1, command.EndsWith("published"));
        }

        [ComponentInteraction("next:*"), UsedImplicitly]
        public async Task NextPage(string command) {
            await DeferAsync(ephemeral: true);

            var message = await Context.Interaction.GetOriginalResponseAsync();
            if (!MessageData.TryGetValue(message.Id, out List<SimpleMod>? mods)) return;

            var embeds = message.Embeds;
            if (embeds.Count == 0) return;

            var footer = embeds.ElementAt(0).Footer;
            if (footer is null) return;

            var page = int.Parse(footer.Value.Text.Split(' ')[1]);
            await GoToPage(page < mods.Count ? page : 0, command.EndsWith("published"));
        }

        [ComponentInteraction("select:*"), UsedImplicitly]
        public async Task SelectMod(string command, string page) {
            await DeferAsync(ephemeral: true);

            await GoToPage(int.Parse(page), command.EndsWith("published"));
        }

        public async Task GoToPage(int page, bool published) {
            var message = await Context.Interaction.GetOriginalResponseAsync();
            var mods = MessageData[message.Id];
            if (mods is null) return;

            var mod = mods[page];
            if (mod is null) return;

            var embed = new EmbedBuilder()
                .WithTitle((mod.Featured ? "⭐️ " : "") + mod.Versions[0].Name)
                .AddField($"{(published ? "Published" : "Pending")} versions",
                    $"`{string.Join("`, `", mod.Versions.Select(version => version.Version))}`")
                .WithThumbnailUrl(GetAPIEndpoint($"/v1/mods/{mod.Id}/logo"))
                .WithFooter($"Page {page + 1} of {mods.Count}")
                .WithUrl(GetWebsiteEndpoint($"/mods/{mod.Id}"));

            await ModifyOriginalResponseAsync(props => props.Embeds = new[] { embed.Build() });
        }
    }
}
