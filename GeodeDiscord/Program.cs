using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace GeodeDiscord;

public class Program {
    private readonly DiscordSocketClient _client = new(new DiscordSocketConfig {
        GatewayIntents =
            GatewayIntents.GuildMessageReactions |
            GatewayIntents.GuildIntegrations |
            GatewayIntents.GuildEmojis |
            GatewayIntents.GuildMessages |
            GatewayIntents.MessageContent
    });

    private static void Main(string[] args) => new Program()
        .MainAsync()
        .GetAwaiter()
        .GetResult();

    private async Task MainAsync() {
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.ReactionAdded += ReactionAddedAsync;

        await _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
        await _client.StartAsync();
        await Task.Delay(Timeout.Infinite);
    }

    private static Task LogAsync(LogMessage log) {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync() {
        Console.WriteLine($"{_client.CurrentUser} is connected!");
        return Task.CompletedTask;
    }

    private async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel, SocketReaction reaction) {
        if (reaction.UserId == _client.CurrentUser.Id)
            return;

        // 💬
        if (reaction.Emote.Name != "\ud83d\udcac")
            return;

        IUserMessage message = await cachedMessage.GetOrDownloadAsync();
        if (message.Author.IsBot || message.Author.IsWebhook)
            return;

        IMessageChannel channel = await cachedChannel.GetOrDownloadAsync();
        await channel.SendMessageAsync(
            allowedMentions: AllowedMentions.None,
            embeds: MessageToEmbeds(message).ToArray()
        );
    }

    private static IEnumerable<Embed> MessageToEmbeds(IMessage message) {
        string authorName = message.Author.GlobalName ?? (message.Author.DiscriminatorValue == 0 ?
            message.Author.Username : $"{message.Author.Username}{message.Author.Discriminator}");
        string authorIcon = message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl();

        List<IAttachment> embeddableAttachments =
            message.Attachments.Where(att =>
                !att.IsSpoiler() && att.ContentType.StartsWith("image/", StringComparison.Ordinal)).ToList();

        yield return new EmbedBuilder()
            .WithAuthor(authorName, authorIcon)
            .WithDescription($"{message.Content}\n\n{message.GetJumpUrl()}")
            .WithImageUrl(embeddableAttachments.Count > 0 ? embeddableAttachments[0].Url : null)
            .Build();

        foreach (IAttachment attachment in embeddableAttachments.Skip(1).Take(9))
            yield return new EmbedBuilder()
                .WithImageUrl(attachment.Url)
                .Build();
    }
}
