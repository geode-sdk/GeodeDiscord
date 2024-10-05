using System.Net;
using System.Text.RegularExpressions;

using Discord;
using Discord.Interactions;

using FuzzySharp;

using JetBrains.Annotations;

using Serilog;

namespace GeodeDiscord.Modules;

public partial class RootModule {
    private const string DocsBaseUrl = "https://docs.geode-sdk.org";
    private static readonly TimeSpan responsesCacheDuration = TimeSpan.FromDays(1);

    // max depth == 6
    private static readonly List<string>[] cachedResponses = [[], [], [], [], [], [], []];
    private static DateTime _lastResponsesUpdate = DateTime.MinValue;

    [GeneratedRegex(@"return navigate\('/(.*?)'\)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex DocsPageRegex();

    private static async Task UpdateDocsResponsesCacheIfNeeded() {
        if (DateTime.UtcNow - _lastResponsesUpdate < responsesCacheDuration)
            return;
        foreach (List<string> responses in cachedResponses)
            responses.Clear();
        HttpResponseMessage res = await new HttpClient().GetAsync(DocsBaseUrl);
        if (res.StatusCode != HttpStatusCode.OK) {
            cachedResponses[0].Add(res.ReasonPhrase ?? "fuck you");
            return;
        }
        (string? x, int i)[] lastBeginnings = new (string? x, int i)[cachedResponses.Length - 1];
        foreach (Match match in DocsPageRegex().Matches(await res.Content.ReadAsStringAsync())) {
            string value = match.Groups[1].Value;
            string[] s = value.TrimStart('/').Split('/');
            for (int i = 0; i < cachedResponses.Length; i++)
                CachePage(i, value, s);
        }
        return;

        void CachePage(int i, string value, IReadOnlyList<string> s) {
            if (i == 0 || s.Count < i ||
                !string.IsNullOrEmpty(lastBeginnings[i - 1].x) && s[i - 1] == lastBeginnings[i - 1].x) {
                cachedResponses[i].Add(value);
                return;
            }
            cachedResponses[i].Insert(lastBeginnings[i - 1].i, value);
            lastBeginnings[i - 1] = (s[i - 1], lastBeginnings[i - 1].i + 1);
        }
    }

    [SlashCommand("docs", "Quickly get a docs page with auto-complete."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task Docs([Autocomplete(typeof(DocsAutocompleteHandler))] string page) {
        await RespondAsync($"{DocsBaseUrl}/{page}");
    }

    [SlashCommand("docs-invalidate-cache", "Forcefully invalidates docs cache. (normally expires daily)"),
     CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task DocsClearCache() {
        _lastResponsesUpdate = DateTime.MinValue;
        await RespondAsync("Successfully invalidated docs cache.", ephemeral: true);
    }

    [UsedImplicitly]
    public class DocsAutocompleteHandler : AutocompleteHandler {
        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) {
            try {
                await UpdateDocsResponsesCacheIfNeeded();
                string value = autocompleteInteraction.Data.Current.Value as string ?? "";
                string[] s = value.TrimStart('/').Split('/');
                if (s.Length >= cachedResponses.Length)
                    return AutocompletionResult.FromSuccess();
                return AutocompletionResult.FromSuccess(Process.ExtractTop(value, cachedResponses[s.Length], limit: 10)
                    .Select(x => new AutocompleteResult(x.Value, x.Value)));
            }
            catch (Exception ex) {
                Log.Error(ex, "Docs autocomplete failed");
                return AutocompletionResult.FromError(ex);
            }
        }
    }
}
