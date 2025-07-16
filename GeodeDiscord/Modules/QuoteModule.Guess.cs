using System.Collections.Concurrent;
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

    [SlashCommand("guess", "Try to guess who said this!"), CommandContextType(InteractionContextType.Guild), UsedImplicitly]
    [ComponentInteraction("guess-again")]
    public async Task Guess() {
        await DeferAsync();
        Quote? quote = null;
        string? quoteAuthorName = null;
        IQueryable<Quote> quotes = db.quotes
            .Where(x => x.extraAttachments == 0) // extra attachments require forwarding right now :<
            .GroupBy(x => x.authorId)
            .OrderBy(_ => EF.Functions.Random())
            .Select(x => x.OrderBy(_ => EF.Functions.Random()).First());
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
        minIndex = Math.Max(minIndex - 5, 0);
        int maxIndex = Math.Min(minIndex + 10, leaderboard.Count);
        leaderboard = leaderboard[minIndex..maxIndex];
        int totalWeight = leaderboard.Sum(x => x.weight);
        while (leaderboard.Count > 0 && selectedUsers.Count < 5) {
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
                $"quote/guess-button:{id}",
                ButtonStyle.Secondary
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
        string? guessId = interaction?.Data.CustomId["quote/guess-button:".Length..];
        await response.ModifyAsync(x => {
            x.Content = guessId is null ? "### Time's up!" : quote.authorId == ulong.Parse(guessId) ?
                $"### ✅ {Context.User.Mention} guessed correctly! This quote is by <@{guessId}>:" :
                $"### ❌ {Context.User.Mention} guessed incorrectly! This quote is not by <@{guessId}>:";
            x.AllowedMentions = AllowedMentions.None;
            x.Embeds = Util.QuoteToEmbeds(quote).ToArray();
            x.Components = new ComponentBuilder()
                .WithButton("Guess again!", "quote/guess-again", ButtonStyle.Secondary)
                .Build();
        });
    }

    [ComponentInteraction("guess-button:*"), UsedImplicitly]
    private async Task GuessButton(string guessId) => await DeferAsync();
}
