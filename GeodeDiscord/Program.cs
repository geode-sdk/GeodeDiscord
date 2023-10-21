using Discord;
using Discord.WebSocket;

namespace GeodeDiscord;

public class Program {
    private readonly DiscordSocketClient _client = new(new DiscordSocketConfig {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
    });

    private static void Main(string[] args) => new Program()
        .MainAsync()
        .GetAwaiter()
        .GetResult();

    private async Task MainAsync() {
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync;
        _client.InteractionCreated += InteractionCreatedAsync;

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

    private async Task MessageReceivedAsync(SocketMessage message) {
        if (message.Author.Id == _client.CurrentUser.Id)
            return;

        if (message.Content == "!ping") {
            ComponentBuilder? cb = new ComponentBuilder()
                .WithButton("Click me!", "unique-id");
            await message.Channel.SendMessageAsync("pong!", components: cb.Build());
        }
    }

    private static async Task InteractionCreatedAsync(SocketInteraction interaction) {
        if (interaction is not SocketMessageComponent component)
            return;
        if (component.Data.CustomId == "unique-id")
            await interaction.RespondAsync("Thank you for clicking my button!");
        else
            Console.WriteLine("An ID has been received that has no handler!");
    }
}
