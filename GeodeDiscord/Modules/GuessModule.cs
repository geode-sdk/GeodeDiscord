using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;

using JetBrains.Annotations;

using Microsoft.EntityFrameworkCore;

using Serilog;

namespace GeodeDiscord.Modules;

[Group("guess", "Play guess with quotes!"), UsedImplicitly]
public partial class GuessModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
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

    private enum GuessResult { Timeout, Incorrect, Correct }

    [SlashCommand("play", "Try to guess who said this!"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
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
                $"guess/guess-button:{id}"
            );
        }
        IUserMessage response = await FollowupAsync(
            text: $"## {Context.User.Mention}, who said this?",
            allowedMentions: AllowedMentions.None,
            embeds: Util.QuoteToCensoredEmbeds(quote).ToArray(),
            components: components.Build()
        );

        SocketMessageComponent? interaction = await InteractionUtility.WaitForInteractionAsync(
            Context.Client,
            TimeSpan.FromSeconds(60d),
            inter =>
                inter.User.Id == Context.User.Id &&
                inter.Type == InteractionType.MessageComponent &&
                inter is SocketMessageComponent msg &&
                msg.Message.Id == response.Id &&
                msg.Data.CustomId.StartsWith("guess/guess-button:")
        ) as SocketMessageComponent;
        ulong guessId = ulong.Parse(interaction?.Data.CustomId["guess/guess-button:".Length..] ?? "0");

        int prevMaxStreak = await QueryStreaks(db, Context.User.Id)
            .Where(x => x.isCorrect)
            .MaxAsync(x => (int?)x.count) ?? 0;

        int prevStreak = await QueryStreaks(db, Context.User.Id)
            .OrderByDescending(x => x.id)
            .Take(1)
            .Where(x => x.isCorrect)
            .Select(x => (int?)x.count)
            .FirstOrDefaultAsync() ?? 0;

        db.Add(new Guess {
            messageId = response.Id,
            guessedAt = interaction?.CreatedAt ?? response.CreatedAt + TimeSpan.FromSeconds(60d),
            userId = Context.User.Id,
            guessId = guessId,
            quote = quote
        });

        bool saveFail = false;
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) {
            Log.Error(ex, "Failed to save stats");
            saveFail = true;
        }

        int total = await db.guesses
            .Where(x => x.userId == Context.User.Id)
            .CountAsync();
        int correct = await db.guesses
            .Where(x => x.userId == Context.User.Id && x.guessId == x.quote.authorId)
            .CountAsync();

        GuessResult result = guessId == 0 || interaction is null ? GuessResult.Timeout :
            quote.authorId == guessId ? GuessResult.Correct : GuessResult.Incorrect;

        int streak = result == GuessResult.Correct ? prevStreak + 1 : 0;
        int maxStreak = Math.Max(streak, prevMaxStreak);
        bool newBestStreak = maxStreak > prevMaxStreak;

        StringBuilder content = new("### ");

        // emote
        content.Append(result switch {
            GuessResult.Timeout or GuessResult.Incorrect when prevStreak > 1 => "💔 ",
            GuessResult.Timeout => "🕛 ",
            GuessResult.Incorrect => "❌ ",
            GuessResult.Correct when streak > 1 && newBestStreak => "🔥 ",
            GuessResult.Correct => "✅ ",
            _ => "❓ "
        });

        // streak
        if (streak > 1) {
            content.Append($"{streak}x");
            if (newBestStreak)
                content.Append(", new best");
            content.Append("! ");
        }

        // message
        switch (result) {
            case GuessResult.Timeout:
                content.AppendLine($"YOUR TAKING TOO LONG... {Context.User.Mention}, this quote is by <@{quote.authorId}>...");
                break;
            case GuessResult.Incorrect:
                content.AppendLine($"Good guess, {Context.User.Mention}, but this quote is not by <@{guessId}>...");
                break;
            case GuessResult.Correct when streak > 1:
                content.AppendLine($"Keep it going, {Context.User.Mention}, this quote is by <@{quote.authorId}>!");
                break;
            case GuessResult.Correct:
                content.AppendLine($"Good job, {Context.User.Mention}, this quote is by <@{quote.authorId}>!");
                break;
            default:
                content.AppendLine($"{Context.User.Mention}?????");
                break;
        }

        // stats
        float correctPercent = (float)correct / total * 100.0f;
        content.Append($"-# You have made **{correct}**/**{total}** (**{correctPercent:F1}%**) correct guesses in total");
        if (!(streak > 1 && newBestStreak))
            content.Append($" with a best streak of **{maxStreak}** in a row");
        content.AppendLine(".");
        if (saveFail) {
            // hopefully nobody ever sees this :-)
            content.AppendLine("-# ⚠️ Failed to save stats, sorry... :<");
        }

        Embed[] quoteEmbeds = Util.QuoteToEmbeds(quote).ToArray();
        await response.ModifyAsync(x => {
            x.Content = content.ToString();
            x.AllowedMentions = AllowedMentions.None;
            x.Embeds = quoteEmbeds;
            ComponentBuilder component = new ComponentBuilder()
                .WithButton("Guess again!", "guess/guess-again", ButtonStyle.Primary, new Emoji("❓"));
            if (guessId != 0)
                component.WithButton("Fix names", "guess/guess-fix-names", ButtonStyle.Secondary, new Emoji("🔧"));
            x.Components = component.Build();
        });
    }

    [ComponentInteraction("guess-button:*"), UsedImplicitly]
    private async Task GuessButton(string guessId) {
        if (Context.Interaction is not SocketMessageComponent interaction) {
            await RespondAsync(
                text: "❌ Failed to check interaction user: interaction is not from a message..?",
                ephemeral: true
            );
            return;
        }
        if (interaction.User.Id != interaction.Message.InteractionMetadata.User.Id) {
            await RespondAsync(
                text: "❌ Only the user that started the game can make a guess!",
                ephemeral: true
            );
            return;
        }
        // let Guess do its thing
        await DeferAsync();
    }

    [ComponentInteraction("guess-fix-names"), UsedImplicitly]
    public async Task GuessFixNames() {
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
        if (authorMatch is null || !authorMatch.Success || !authorMatch.Groups[1].Success) {
            await RespondAsync("❌ Failed to fix names: unable to detect quote author!", ephemeral: true);
            return;
        }

        if (!ulong.TryParse(authorMatch.Groups[1].ValueSpan, out ulong authorId)) {
            await RespondAsync("❌ Failed to fix names: unable to parse quote author ID!", ephemeral: true);
            return;
        }

        Match match = ShowedIdRegex().Match(msg.Content);
        if (!match.Success || !match.Groups[1].Success) {
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
                .WithButton("Guess again!", "guess/guess-again", ButtonStyle.Primary, new Emoji("❓"))
                .Build();
        });
    }

    [SlashCommand("stats", "Shows some quote related stats."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    public async Task GetStats(IUser? user = null) {
        await DeferAsync();

        user ??= Context.User;

        StringBuilder stats = new();

        int quotedCount = await db.quotes.CountAsync(x => x.authorId == user.Id);

        int total = await db.guesses.Where(x => x.userId == user.Id).CountAsync();
        int correct = await db.guesses.Where(x => x.userId == user.Id && x.guessId == x.quote.authorId).CountAsync();
        int maxStreak = await QueryStreaks(db, user.Id).Where(x => x.isCorrect).MaxAsync(x => x.count);

        int totalGuessed = await db.guesses.Where(x => x.guessId == user.Id).CountAsync();
        int totalOther = await db.guesses.Where(x => x.quote.authorId == user.Id).CountAsync();
        int correctOther = await db.guesses.Where(x => x.quote.authorId == user.Id && x.guessId == x.quote.authorId).CountAsync();

        int totalSelf = await db.guesses.Where(x => x.userId == user.Id && x.quote.authorId == user.Id).CountAsync();
        int incorrectSelf = await db.guesses
            .Where(x => x.userId == user.Id && x.quote.authorId == user.Id && x.guessId != x.quote.authorId)
            .CountAsync();

        if (quotedCount > 0)
            stats.AppendLine($"- Has been quoted **{quotedCount}** time{Suffix(quotedCount, "s")}.");
        if (total > 0) {
            stats.AppendLine($"- Has made **{total}** total quote guess{Suffix(total, "es")}...");
            if (correct > 0) {
                float correctPercent = (float)correct / total * 100.0f;
                stats.AppendLine($"  - ...**{correct}** (**{correctPercent:F1}%**) of which {Choose(correct, "was", "were")} correct.");
            }
            else {
                stats.AppendLine("  - ...none of which were correct.");
            }
            if (maxStreak > 1)
                stats.AppendLine($"- Achieved a maximum streak of **{maxStreak}** correct guesses in a row.");
        }
        if (totalOther > 0) {
            stats.AppendLine($"- Has been the correct guess **{totalOther}** time{Suffix(totalOther, "s")}...");
            if (correctOther > 0) {
                float correctPercent = (float)correctOther / totalOther * 100.0f;
                if (correctPercent > 60.0f)
                    stats.AppendLine($"  - ...and guessed correctly **{correctOther}** time{Suffix(correctOther, "s")} (**{correctPercent:F1}%**).");
                else
                    stats.AppendLine($"  - ...but only guessed correctly **{correctOther}** time{Suffix(correctOther, "s")} (**{correctPercent:F1}%**).");
            }
            else {
                stats.AppendLine("  - ...but never guessed correctly.");
            }
        }
        if (totalGuessed > 0) {
            stats.AppendLine($"- Has been guessed **{totalGuessed}** time{Suffix(totalGuessed, "s")}...");
            if (correctOther > 0) {
                float correctPercent = (float)correctOther / totalGuessed * 100.0f;
                if (correctPercent > 60.0f)
                    stats.AppendLine($"  - ...and **{correctOther}** (**{correctPercent:F1}%**) of the guesses {Choose(correctOther, "was", "were")} correct.");
                else
                    stats.AppendLine($"  - ...but only **{correctOther}** (**{correctPercent:F1}%**) of the guesses {Choose(correctOther, "was", "were")} correct.");
            }
            else {
                stats.AppendLine("  - ...but none of the guesses were correct.");
            }
        }
        if (totalSelf > 0) {
            if (incorrectSelf > 0) {
                stats.AppendLine($"- Has gotten to guess themselves **{totalSelf}** time{Suffix(totalSelf, "s")}!..");
                stats.AppendLine($"  - ...and somehow failed **{incorrectSelf}** time{Suffix(incorrectSelf, "s")}.");
            }
            else {
                stats.AppendLine($"- Has gotten to guess themselves **{totalSelf}** time{Suffix(totalSelf, "s")}!");
            }
        }

        if (stats.Length == 0) {
            await RespondAsync("❌ No stats to show... :<", ephemeral: true);
            return;
        }

        await FollowupAsync(
            allowedMentions: AllowedMentions.None,
            embed: new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder()
                    .WithName(user.GlobalName)
                    .WithIconUrl(user.GetDisplayAvatarUrl()))
                .WithDescription(stats.ToString())
                .Build()
        );

        return;

        static string Suffix(int x, string suffix) => x == 1 ? "" : suffix;
        static string Choose(int x, string singular, string plural) => x == 1 ? singular : plural;
    }

    [Group("leaderboards", "Guess leaderboards.")]
    public class LeaderboardsModule(ApplicationDbContext db) : InteractionModuleBase<SocketInteractionContext> {
        [SlashCommand("correct", "Shows top 10 most correct guesses."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
        public async Task GetCorrect() {
            await DeferAsync();
            IEnumerable<string> lines = db.guesses
                .GroupBy(x => x.userId)
                .Select(x => new { userId = x.Key, correct = x.Count(y => y.guessId == y.quote.authorId) })
                .OrderByDescending(x => x.correct)
                .Take(10)
                .AsEnumerable()
                .Select((x, i) => $"{i + 1}. <@{x.userId}> - **{x.correct}** correct guesses");
            await FollowupAsync(
                text: $"## 🏆 10 most correct guesses:\n{string.Join("\n", lines)}",
                allowedMentions: AllowedMentions.None
            );
        }

        [SlashCommand("streak", "Shows top 10 highest guess streaks."), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
        public async Task GetStreak() {
            await DeferAsync();
            IEnumerable<string> lines = QueryStreaks(db, 0)
                .Where(x => x.isCorrect)
                .GroupBy(x => x.userId)
                .Select(x => new { userId = x.Key, maxStreak = x.Max(y => y.count) })
                .OrderByDescending(x => x.maxStreak)
                .Take(10)
                .AsEnumerable()
                .Select((x, i) => $"{i + 1}. <@{x.userId}> - **{x.maxStreak}** correct guesses in a row");
            await FollowupAsync(
                text: $"## 🏆 10 highest guess streaks:\n{string.Join("\n", lines)}",
                allowedMentions: AllowedMentions.None
            );
        }
    }

    private record Streak(int id, int count, bool isCorrect, ulong userId);

#pragma warning disable EF1002
    private static IQueryable<Streak> QueryStreaks(ApplicationDbContext db, ulong userId) =>
        db.Database.SqlQueryRaw<Streak>($"""
            SELECT group_id as id, COUNT() AS count, is_correct as isCorrect, userId
            FROM (
                SELECT *, SUM(is_new_group) OVER (ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS group_id
                FROM (
                    SELECT *, is_correct != LAG(is_correct, 1, is_correct) OVER () AS is_new_group
                    FROM (
                        SELECT *, guessId == (SELECT authorId FROM quotes WHERE messageId = quoteMessageId) AS is_correct
                        FROM guesses
                        {(userId == 0 ? "" : $"WHERE userId = {userId}")}
                    )
                )
            )
            GROUP BY id
            """);
#pragma warning restore EF1002

    [GeneratedRegex(@"\\- <@(.*?)>")]
    private static partial Regex AuthorIdRegex();

    [GeneratedRegex("(?:not )?by <@(.*?)>")]
    private static partial Regex ShowedIdRegex();
}
