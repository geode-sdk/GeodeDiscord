using Discord;
using Discord.Interactions;
using JetBrains.Annotations;

using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.Text;

namespace GeodeDiscord.Modules;

public partial class IndexModule {
    private static string[] UnauthorizedResponses = [
        "❌ [BUZZER]",
		"❌ Your princess is in another castle",
		"❌ Absolutely not",
		"❌ Get lost",
		"❌ Sucks to be you",
		"❌ No admin, laugh at this user",
		"❌ Admin dashboard",
		"❌ Why are we here? Just to suffer?",
		"❌ You hacked the mainframe! Congrats.",
		"❌ You're an admin, Harry",
    ];

    [Group("admin", "Administrate the Geode mod index."), UsedImplicitly]
    public class AdminModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        public async Task<bool> CheckAdmin(HttpClient httpClient, bool respond) {
            using var response = await httpClient.GetAsync(GetAPIEndpoint("/v1/me"));
            if (!response.IsSuccessStatusCode) {
                if (respond) await RespondAsync(
                    await GetError(response, "An error occurred while checking your admin status"), ephemeral: true);
                else await ModifyOriginalResponseAsync(async p =>
                    p.Content = await GetError(response, "An error occurred while checking your admin status"));
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(json);
            if (data is null || data["payload"] is null || data["payload"]?["admin"] is null) {
                if (respond) await RespondAsync("❌ An error occurred while parsing your user response.", ephemeral: true);
                else await ModifyOriginalResponseAsync(p =>
                    p.Content = "❌ An error occurred while parsing your user response.");
                return false;
            }

            if (!data["payload"]?["admin"]?.Value<bool>() ?? false) {
                if (respond) await RespondAsync(
                    UnauthorizedResponses[new Random().Next(UnauthorizedResponses.Length)], ephemeral: true);
                else await ModifyOriginalResponseAsync(p =>
                    p.Content = UnauthorizedResponses[new Random().Next(UnauthorizedResponses.Length)]);
                return false;
            }

            return true;
        }

        [SlashCommand("verify", "Verify/Unverify a developer on the Geode mod index."), UsedImplicitly]
        public async Task Verify([Summary(null, "Developer to verify/unverify.")] string developer,
            [Summary(null, "Verifcation status.")] bool verified) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");

            if (!await CheckAdmin(httpClient, true)) return;

            using var devResponse = await httpClient.GetAsync(GetAPIEndpoint($"/v1/developers?query={developer}"));
            if (!devResponse.IsSuccessStatusCode) {
                await RespondAsync(await GetError(devResponse, "An error occurred while getting the developer data"), ephemeral: true);
                return;
            }

            var devJson = await devResponse.Content.ReadAsStringAsync();
            var devData = JsonConvert.DeserializeObject<JObject>(devJson);
            if (devData is null || devData["payload"] is null || devData["payload"]?["data"] is null ||
                devData["payload"]?["data"]?.Count() == 0) {
                await RespondAsync("❌ An error occurred while parsing the developer response.", ephemeral: true);
                return;
            }

            var dev = devData["payload"]?["data"]?[0];
            if (dev?["verified"]?.Value<bool>() == verified) {
                await RespondAsync($"❌ Developer is already {(verified ? "" : "un")}verified.", ephemeral: true);
                return;
            }

            using var verifyResponse = await httpClient.PutAsync(GetAPIEndpoint($"/v1/developers/{dev?["id"]}"),
                new StringContent(JsonConvert.SerializeObject(new { verified }), Encoding.UTF8, "application/json"));
            if (!verifyResponse.IsSuccessStatusCode) {
                await RespondAsync(
                    await GetError(verifyResponse, $"An error occurred while {(verified ? "" : "un")}verifying the developer"),
                    ephemeral: true);
                return;
            }

            await RespondAsync($"✅ Successfully {(verified ? "" : "un")}verified developer **" + dev?["display_name"] + "**!");
        }

        [SlashCommand("update", "Update a mod version's status on the Geode mod index."), UsedImplicitly]
        public async Task Update([Summary(null, "Mod ID.")] string id, [Summary(null, "Mod version.")] string version,
            [Summary(null, "New status."), Autocomplete(typeof(StatusAutocompleteHandler))] string status,
            [Summary(null, "Reason for status change.")] string? reason = null) {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");

            if (!await CheckAdmin(httpClient, true)) return;

            using var response = await httpClient.PutAsync(GetAPIEndpoint($"/v1/mods/{id}/versions/{version}"),
                new StringContent(JsonConvert.SerializeObject(new { status, info = reason }), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode) {
                await RespondAsync(
                    await GetError(response, "An error occurred while updating the mod version status"), ephemeral: true);
                return;
            }

            await RespondAsync($"✅ Successfully {status.Replace("ing", "ed")} mod version **{version}**!");
        }

        public class StatusAutocompleteHandler : AutocompleteHandler {
            public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
                IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
                IEnumerable<AutocompleteResult> statuses = [
                    new AutocompleteResult("accepted", "accepted"),
                    new AutocompleteResult("pending", "pending"),
                    new AutocompleteResult("rejected", "rejected"),
                    new AutocompleteResult("unlisted", "unlisted")
                ];

                return Task.FromResult(AutocompletionResult.FromSuccess(statuses.Take(25)));
            }
        }

        [JsonObject]
        public class GDVersion {
            [JsonProperty("win")]
            public string? Windows { get; set; }
            [JsonProperty("android32")]
            public string? Android32 { get; set; }
            [JsonProperty("android64")]
            public string? Android64 { get; set; }
            [JsonProperty("mac-intel")]
            public string? MacIntel { get; set; }
            [JsonProperty("mac-arm")]
            public string? MacArm { get; set; }
        }

        [JsonObject]
        public class ModDependency {
            [JsonProperty("mod_id")]
            public required string ModId { get; set; }
            [JsonProperty("version")]
            public required string Version { get; set; }
            [JsonProperty("importance")]
            public required string Importance { get; set; }
        }

        [JsonObject]
        public class ModDeveloper {
            [JsonProperty("id")]
            public required string Id { get; set; }
            [JsonProperty("username")]
            public required string Username { get; set; }
            [JsonProperty("display_name")]
            public required string DisplayName { get; set; }
            [JsonProperty("is_owner")]
            public bool IsOwner { get; set; }
        }

        [JsonObject]
        public class ModVersion {
            [JsonProperty("version")]
            public required string Version { get; set; }
            [JsonProperty("name")]
            public required string Name { get; set; }
            [JsonProperty("description")]
            public required string Description { get; set; }
            [JsonProperty("direct_download_link")]
            public required string DirectDownloadLink { get; set; }
            [JsonProperty("hash")]
            public required string Hash { get; set; }
            [JsonProperty("geode")]
            public required string Geode { get; set; }
            [JsonProperty("gd")]
            public required GDVersion GD { get; set; }
            [JsonProperty("early_load")]
            public bool EarlyLoad { get; set; }
            [JsonProperty("api")]
            public bool API { get; set; }
            [JsonProperty("dependencies")]
            public required List<ModDependency> Dependencies { get; set; }
            [JsonProperty("incompatibilities")]
            public required List<ModDependency> Incompatibilities { get; set; }
        }

        [JsonObject]
        public class ModLinks {
            [JsonProperty("source")]
            public string? Source { get; set; }
            [JsonProperty("community")]
            public string? Community { get; set; }
            [JsonProperty("homepage")]
            public string? Homepage { get; set; }
        }

        [JsonObject]
        public class Mod {
            [JsonProperty("id")]
            public required string Id { get; set; }
            [JsonProperty("repository")]
            public string? Repository { get; set; }
            [JsonProperty("featured")]
            public bool Featured { get; set; }
            [JsonProperty("developers")]
            public required List<ModDeveloper> Developers { get; set; }
            [JsonProperty("tags")]
            public required List<string> Tags { get; set; }
            [JsonProperty("links")]
            public ModLinks? Links { get; set; }
        }

        [SlashCommand("pending", "List pending mods on the Geode mod index."), UsedImplicitly]
        public async Task Pending() {
            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await RespondAsync("❌ You must log in to your Geode account first.", ephemeral: true);
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");

            if (!await CheckAdmin(httpClient, true)) return;

            await GetPage(1, indexToken, httpClient, true);
        }

        public async Task GetPage(int page, IndexToken? indexToken, HttpClient? httpClient, bool initial) {
            indexToken ??= await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) return;

            httpClient ??= new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");
            using var response = await httpClient.GetAsync(GetAPIEndpoint($"/v1/mods?status=pending&page={page}&per_page=1"));
            if (!response.IsSuccessStatusCode) {
                if (initial) await RespondAsync(
                    await GetError(response, "An error occurred while getting the pending mods"), ephemeral: true);
                else await ModifyOriginalResponseAsync(async p =>
                    p.Content = await GetError(response, "An error occurred while getting the pending mods"));
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(json);
            if (data is null || data["payload"] is null || data["payload"]?["data"] is null) {
                if (initial) await RespondAsync("❌ An error occurred while parsing the pending mods response.", ephemeral: true);
                else await ModifyOriginalResponseAsync(p =>
                    p.Content = "❌ An error occurred while parsing the pending mods response.");
                return;
            }

            if (data["payload"]?["data"]?.Count() == 0) {
                if (page > 1) await GetPage(page - 1, indexToken, httpClient, initial);
                else if (initial) await RespondAsync("❌ No pending mods.", ephemeral: true);
                else await ModifyOriginalResponseAsync(p => p.Content = "❌ No pending mods.");
                return;
            }

            var mod = data["payload"]?["data"]?[0]?.ToObject<Mod>();

            using var versionResponse = await httpClient.GetAsync(
                GetAPIEndpoint($"/v1/mods/{mod?.Id}/versions/{data["payload"]?["data"]?[0]?["versions"]?[0]?["version"]}"));
            if (!versionResponse.IsSuccessStatusCode) {
                if (initial) await RespondAsync(
                    await GetError(versionResponse, "An error occurred while getting the pending mod version"), ephemeral: true);
                else await ModifyOriginalResponseAsync(async p =>
                    p.Content = await GetError(versionResponse, "An error occurred while getting the pending mod version"));
                return;
            }

            var versionJson = await versionResponse.Content.ReadAsStringAsync();
            var versionData = JsonConvert.DeserializeObject<JObject>(versionJson);
            if (versionData is null || versionData["payload"] is null) {
                if (initial) await RespondAsync(
                    "❌ An error occurred while parsing the pending mod version response.", ephemeral: true);
                else await ModifyOriginalResponseAsync(p =>
                    p.Content = "❌ An error occurred while parsing the pending mod version response.");
                return;
            }

            var version = versionData["payload"]?.ToObject<ModVersion>();
            if (mod is null || version is null) {
                if (initial) await RespondAsync("❌ An error occurred while parsing the pending mod data.", ephemeral: true);
                else await ModifyOriginalResponseAsync(p => p.Content = "❌ An error occurred while parsing the pending mod data.");
                return;
            }

            var total = data["payload"]?["count"]?.Value<int>() ?? 1;

            var embed = new EmbedBuilder()
                .WithTitle(mod.Featured ? "⭐️ " : "" + version.Name)
                .WithDescription(version.Description)
                .WithUrl(GetWebsiteEndpoint($"/mods/{mod.Id}?version={version.Version}"))
                .WithFooter($"Page {page} of {total}")
                .WithThumbnailUrl(GetAPIEndpoint($"/v1/mods/{mod.Id}/logo"))
                .AddField("ID", mod.Id, true)
                .AddField("Version", version.Version, true)
                .AddField("Geode", version.Geode, true)
                .AddField("Early Load", version.EarlyLoad ? "Yes" : "No", true)
                .AddField("API", version.API ? "Yes" : "No", true)
                .AddField("Developers", string.Join(", ", mod.Developers.Select(d =>
                    $"[{(d.IsOwner ? "**" : "")}{d.DisplayName}{(d.IsOwner ? "**" : "")}]({GetWebsiteEndpoint(
                        $"/mods?developer={d.Username}")})")), true)
                .AddField("Geometry Dash", $"Windows: {version.GD.Windows ?? "N/A"}\n" +
                    $"Android (32-bit): {version.GD.Android32 ?? "N/A"}\n" +
                    $"Android (64-bit): {version.GD.Android64 ?? "N/A"}\n" +
                    $"macOS (Intel): {version.GD.MacIntel ?? "N/A"}\n" +
                    $"macOS (ARM): {version.GD.MacArm ?? "N/A"}", false)
                .AddField("Dependencies", version.Dependencies.Count == 0 ? "None" :
                    $"`{string.Join("`\n`", version.Dependencies.Select(d =>
                        $"{d.ModId} {d.Version} ({d.Importance})"))}`", false)
                .AddField("Incompatibilities", version.Incompatibilities.Count == 0 ? "None" :
                    $"`{string.Join("`\n`", version.Incompatibilities.Select(d =>
                        $"{d.ModId} {d.Version} ({d.Importance})"))}`", false)
                .AddField("Source", mod.Links is not null && mod.Links.Source is not null ? mod.Links.Source : mod.Repository ?? "N/A", true)
                .AddField("Community", mod.Links is not null ? mod.Links.Community ?? "N/A" : "N/A", true)
                .AddField("Homepage", mod.Links is not null ? mod.Links.Homepage ?? "N/A" : "N/A", true)
                .AddField("Hash", $"`{version.Hash}`", true)
                .AddField("Download", version.DirectDownloadLink, true)
                .AddField("Tags", mod.Tags.Count == 0 ? "None" : $"`{string.Join("`, `", mod.Tags)}`", true);

            var components = new ComponentBuilder()
                .WithButton("Previous", "index/admin/previous", ButtonStyle.Primary, new Emoji("◀️"))
                .WithButton("Accept", "index/admin/accept", ButtonStyle.Success, new Emoji("✅"))
                .WithButton("Reject", "index/admin/reject", ButtonStyle.Danger, new Emoji("❌"))
                .WithButton("Next", "index/admin/next", ButtonStyle.Primary, new Emoji("▶️"))
                .WithSelectMenu("index/admin/page", Enumerable.Range(1, total)
                    .Select(i => new SelectMenuOptionBuilder().WithLabel(i.ToString()).WithValue(i.ToString()))
                    .ToList(), "Go to page...");

            if (initial) await RespondAsync(embed: embed.Build(), components: components.Build(), ephemeral: true);
            else await ModifyOriginalResponseAsync(p => {
                p.Embeds = new[] { embed.Build() };
                p.Components = components.Build();
            });
        }

        [ComponentInteraction("previous"), UsedImplicitly]
        public async Task Previous() {
            await DeferAsync(ephemeral: true);

            var message = await GetOriginalResponseAsync();

            var embed = message.Embeds.ElementAt(0);
            if (embed is null) return;

            var footer = embed.Footer;
            if (footer is null || !footer.HasValue) return;

            var splitFooter = footer.Value.Text.Split(' ');
            var page = int.Parse(splitFooter[1]);
            var count = int.Parse(splitFooter[3]);

            await ModifyOriginalResponseAsync(p => p.Content = "\u200b");

            await GetPage(page > 1 ? page - 1 : count, null, null, false);
        }

        [ComponentInteraction("next"), UsedImplicitly]
        public async Task Next() {
            await DeferAsync(ephemeral: true);

            var message = await GetOriginalResponseAsync();

            var embed = message.Embeds.ElementAt(0);
            if (embed is null) return;

            var footer = embed.Footer;
            if (footer is null || !footer.HasValue) return;

            var splitFooter = footer.Value.Text.Split(' ');
            var page = int.Parse(splitFooter[1]);
            var count = int.Parse(splitFooter[3]);

            await ModifyOriginalResponseAsync(p => p.Content = "\u200b");

            await GetPage(page < count ? page + 1 : 1, null, null, false);
        }

        [ComponentInteraction("accept"), UsedImplicitly]
        public async Task Accept() {
            var modal = new ModalBuilder()
                .WithTitle("Accept Mod")
                .WithCustomId("index/admin/confirm:accepted")
                .AddTextInput("Reason", "reason", TextInputStyle.Paragraph, "Leave blank for no reason", required: false)
                .Build();

            await RespondWithModalAsync(modal);
        }

        [ComponentInteraction("reject"), UsedImplicitly]
        public async Task Reject() {
            var modal = new ModalBuilder()
                .WithTitle("Reject Mod")
                .WithCustomId("index/admin/confirm:rejected")
                .AddTextInput("Reason", "reason", TextInputStyle.Paragraph, "Leave blank for no reason", required: false)
                .Build();

            await RespondWithModalAsync(modal);
        }

        [ComponentInteraction("page"), UsedImplicitly]
        public async Task Page(string page) {
            await DeferAsync(ephemeral: true);

            await ModifyOriginalResponseAsync(p => p.Content = "\u200b");

            await GetPage(int.Parse(page), null, null, false);
        }

        public class ModStatusModal : IModal {
            public string Title => string.Empty;

            [ModalTextInput("reason")]
            public string? Reason { get; set; }
        }

        [ModalInteraction("confirm:*"), UsedImplicitly]
        public async Task UpdateModStatus(string status, ModStatusModal modal) {
            await DeferAsync(ephemeral: true);

            var message = await GetOriginalResponseAsync();

            var embed = message.Embeds.ElementAt(0);
            if (embed is null) return;

            var name = embed.Title.Contains("⭐️") ? string.Join(' ', embed.Title.Split(' ').Skip(1)) : embed.Title;

            var id = embed.Fields.ToList().Find(f => f.Name == "ID").Value;
            if (id is null) return;

            var version = embed.Fields.ToList().Find(f => f.Name == "Version").Value;
            if (version is null) return;

            var footer = embed.Footer;
            if (footer is null || !footer.HasValue) return;

            var page = int.Parse(footer.Value.Text.Split(' ')[1]);

            var indexToken = await db.indexTokens.FindAsync(Context.User.Id);
            if (indexToken is null) {
                await ModifyOriginalResponseAsync(p => p.Content = "❌ You must log in to your Geode account first.");
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", indexToken.indexToken);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GeodeDiscord");

            if (!await CheckAdmin(httpClient, false)) return;

            using var response = await httpClient.PutAsync(GetAPIEndpoint($"/v1/mods/{id}/versions/{version}"),
                new StringContent(JsonConvert.SerializeObject(new { status, info = modal.Reason }),
                Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode) {
                await ModifyOriginalResponseAsync(async p =>
                    p.Content = await GetError(response, $"An error occurred while {status.Replace("ed", "ing")} the mod"));
                return;
            }

            await ModifyOriginalResponseAsync(p =>
                p.Content = $"✅ Successfully {status.Replace("ing", "ed")} **{name} {version}**!");

            await GetPage(page, indexToken, httpClient, false);
        }
    }
}
