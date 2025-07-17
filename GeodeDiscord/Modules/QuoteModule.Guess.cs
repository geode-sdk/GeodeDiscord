using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace GeodeDiscord.Modules;

public partial class QuoteModule {
    private static readonly ConcurrentDictionary<ulong, IUser?> extraUserCache = [];

    private async Task<IUser?> GetUserAsync(ulong id) {
        IUser? user = Context.Client.GetUser(id);
        if (user is not null || extraUserCache.TryGetValue(id, out user))
            return user;
        user = await Context.Client.GetUserAsync(id);
        extraUserCache[id] = user;
        Log.Information("Added user {User} ({Username}, {Id}) to extra cache", user?.GlobalName, user?.Username, id);
        return user;
    }

    private async Task<string?> GetUserNameAsync(ulong id) {
        IUser? user = await GetUserAsync(id);
        return user?.GlobalName ?? user?.Username ?? null;
    }

    private enum GuessResult { Timeout, Correct, Incorrect }

    [SlashCommand("guess", "Try to guess who said this!"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    [ComponentInteraction("guess-again")]
    public async Task Guess() {
        await DeferAsync();

        IQueryable<Quote> quotes = db.quotes
            .Where(x => x.extraAttachments == 0) // extra attachments require forwarding right now :<
            .OrderBy(_ => EF.Functions.Random())
            .Take(10); // surely 10 is gonna be enough to find one

        Quote? quote = null;
        string? quoteAuthorName = null;
        foreach (Quote x in quotes) {
            string? name = await GetUserNameAsync(x.authorId);
            if (name is null)
                continue;
            quote = x;
            quoteAuthorName = name;
            break;
        }

        if (quote is null || quoteAuthorName is null) {
            await RespondAsync("❌ Couldn't find a quote!", ephemeral: true);
            return;
        }

        HashSet<(ulong id, string name)> selectedUsers = [ (quote.authorId, quoteAuthorName) ];

        List<(ulong id, string name, int weight)> leaderboard = await db.quotes
            .GroupBy(x => x.authorId)
            .Select(x => new { id = x.Key, weight = x.Count() })
            .OrderByDescending(x => x.weight)
            .AsAsyncEnumerable()
            .SelectAwait(async x => (x.id, name: await GetUserNameAsync(x.id), x.weight))
            .Where(x => x.name is not null)
            .Select(x => (x.id, name: x.name!, x.weight))
            .ToListAsync();

        int minIndex = leaderboard.FindIndex(x => x.id == quote.authorId);
        leaderboard.RemoveAt(minIndex);

        const int leaderboardRange = 5;
        const int guessOptions = 5;

        minIndex = Math.Max(minIndex - leaderboardRange, 0);
        int maxIndex = Math.Min(minIndex + leaderboardRange * 2, leaderboard.Count);
        leaderboard = leaderboard[minIndex..maxIndex];

        int totalWeight = leaderboard.Sum(x => x.weight);
        while (leaderboard.Count > 0 && selectedUsers.Count < guessOptions) {
            int rand = Random.Shared.Next(0, totalWeight);
            for (int j = 0; j < leaderboard.Count; j++) {
                rand -= leaderboard[j].weight;
                if (rand > 0)
                    continue;
                selectedUsers.Add((leaderboard[j].id, leaderboard[j].name));
                totalWeight -= leaderboard[j].weight;
                leaderboard.RemoveAt(j);
                break;
            }
        }

        (ulong id, string name)[] users = selectedUsers.ToArray();
        Random.Shared.Shuffle(users);

        ComponentBuilder components = new();
        foreach ((ulong id, string name) in users) {
            components.WithButton(
                name,
                $"quote/guess-button:{id}"
            );
        }
        IUserMessage response = await FollowupAsync(
            text: "## Who said this?",
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToCensoredEmbeds(quote).ToArray(),
            components: components.Build()
        );

        SocketMessageComponent? interaction = await InteractionUtility.WaitForInteractionAsync(
            Context.Client,
            TimeSpan.FromSeconds(60d),
            inter =>
                inter.Type == InteractionType.MessageComponent &&
                inter is SocketMessageComponent msg &&
                msg.Message.Id == response.Id &&
                msg.Data.CustomId.StartsWith("quote/guess-button:")
        ) as SocketMessageComponent;
        ulong guessId = ulong.Parse(interaction?.Data.CustomId["quote/guess-button:".Length..] ?? "0");

        GuessResult result = guessId == 0 || interaction is null ? GuessResult.Timeout :
            quote.authorId == guessId ? GuessResult.Correct : GuessResult.Incorrect;
        SocketUser guesser = result switch {
            GuessResult.Timeout => Context.User,
            _ => interaction!.User
        };

        GuessStats? stats = await db.guessStats.FindAsync(guesser.Id);
        if (stats is null) {
            stats = new GuessStats { userId = guesser.Id };
            db.Add(stats);
        }
        ulong prevStreak = stats.streak;
        bool newBestStreak = false;
        stats.total++;
        switch (result) {
            case GuessResult.Correct:
                stats.correct++;
                stats.streak++;
                ulong prevMaxStreak = stats.maxStreak;
                stats.maxStreak = Math.Max(stats.streak, stats.maxStreak);
                newBestStreak = stats.maxStreak > prevMaxStreak;
                break;
            default:
                stats.streak = 0;
                break;
        }

        bool statsSaveFail = false;
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save stats");
            statsSaveFail = true;
        }

        string resultText = result switch {
            GuessResult.Timeout => $"🕛 {guesser.Mention} took too long!",
            GuessResult.Correct => $"✅ {guesser.Mention} guessed correctly!",
            GuessResult.Incorrect => $"❌ {guesser.Mention} guessed incorrectly!",
            _ => "?????"
        };
        StringBuilder statsText = new();
        switch (result) {
            case GuessResult.Correct:
                if (stats.streak > 1) {
                    statsText.Append("They're on a **");
                    statsText.Append(stats.streak);
                    statsText.Append("x** streak");
                    if (newBestStreak)
                        statsText.Append(", **new best**");
                    statsText.Append("! 🔥");
                    statsText.AppendLine();
                }
                break;
            default:
                if (prevStreak > 1) {
                    statsText.Append("They broke a **");
                    statsText.Append(prevStreak);
                    statsText.Append("x** streak... 💔");
                    statsText.AppendLine();
                }
                break;
        }
        statsText.Append("**");
        statsText.Append(stats.correct);
        statsText.Append("**/**");
        statsText.Append(stats.total);
        statsText.Append("** correct guesses total (**");
        statsText.Append(CultureInfo.InvariantCulture, $"{(float)stats.correct / stats.total * 100.0f:F1}");
        statsText.Append("%**).");
        if (!(result == GuessResult.Correct && stats.streak > 1 && newBestStreak)) {
            statsText.Append(" Best streak: **");
            statsText.Append(stats.maxStreak);
            statsText.Append("x**.");
        }
        if (statsSaveFail) {
            // hopefully nobody ever sees this :-)
            statsText.AppendLine("-# ⚠️ Failed to save stats, sorry... :<");
        }

        Embed[] quoteEmbeds = Util.QuoteToEmbeds(quote).ToArray();
        await response.ModifyAsync(x => {
            x.Content = result switch {
                GuessResult.Incorrect => $"### {resultText} This quote is not by <@{guessId}>.\n{statsText}",
                _ => $"### {resultText} This quote is by <@{quote.authorId}>.\n{statsText}"
            };
            x.AllowedMentions = AllowedMentions.None;
            x.Embeds = quoteEmbeds;
            ComponentBuilder component = new ComponentBuilder()
                .WithButton("Guess again!", "quote/guess-again", ButtonStyle.Primary, new Emoji("❓"));
            if (guessId != 0)
                component.WithButton("Fix names", "quote/guess-fix-names", ButtonStyle.Secondary, new Emoji("🔧"));
            x.Components = component.Build();
        });
    }

    [ComponentInteraction("guess-button:*"), UsedImplicitly]
    private async Task GuessButton(string guessId) => await DeferAsync();

    [ComponentInteraction("guess-fix-names"), UsedImplicitly]
    private async Task GuessFixNames() {
        if (Context.Interaction is not SocketMessageComponent interaction) {
            await RespondAsync("❌ Failed to fix names: interaction is not from a message..?", ephemeral: true);
            return;
        }

        SocketUserMessage msg = interaction.Message;
        Embed? embed = msg.Embeds.FirstOrDefault();
        if (embed?.Author?.Name is null) {
            await RespondAsync("❌ Failed to fix names: unable to detect quote!", ephemeral: true);
            return;
        }

        Match? authorMatch = AuthorIdRegex().Matches(embed.Description).LastOrDefault();
        if (authorMatch is null) {
            await RespondAsync("❌ Failed to fix names: unable to detect quote author!", ephemeral: true);
            return;
        }

        if (!ulong.TryParse(authorMatch.ValueSpan, out ulong authorId)) {
            await RespondAsync("❌ Failed to fix names: unable to parse quote author ID!", ephemeral: true);
            return;
        }

        Match match = ShowedIdRegex().Match(msg.Content);
        if (!match.Groups[1].Success) {
            await RespondAsync("❌ Failed to fix names: unable to detect showed ID!", ephemeral: true);
            return;
        }

        if (!ulong.TryParse(match.Groups[1].ValueSpan, out ulong showedId)) {
            await RespondAsync("❌ Failed to fix names: unable to parse showed ID!", ephemeral: true);
            return;
        }

        string correctName = await GetUserNameAsync(authorId) ?? authorId.ToString();
        string replacedName = authorId == showedId ?
            $"by `{correctName}`" :
            $"by `{correctName}`, not `{await GetUserNameAsync(showedId) ?? showedId.ToString()}`";

        await msg.ModifyAsync(x => {
            x.Content = $"{msg.Content[..match.Index]}{replacedName}{msg.Content[(match.Index + match.Length)..]}";
            x.AllowedMentions = AllowedMentions.None;
            x.Embeds = msg.Embeds.ToArray();
            x.Components = new ComponentBuilder()
                .WithButton("Guess again!", "quote/guess-again", ButtonStyle.Primary, new Emoji("❓"))
                .Build();
        });
    }

    [GeneratedRegex(@"\\- <@(.*)>")]
    private static partial Regex AuthorIdRegex();

    [GeneratedRegex("(?:not )?by <@(.*)>")]
    private static partial Regex ShowedIdRegex();
}
