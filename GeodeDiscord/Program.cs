using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using GeodeDiscord.Database;

using Microsoft.Extensions.DependencyInjection;

namespace GeodeDiscord;

public class Program {
    private readonly IServiceProvider _services = new ServiceCollection()
        .AddDbContext<ApplicationDbContext>()
        .AddSingleton(new DiscordSocketConfig {
            GatewayIntents =
                GatewayIntents.GuildIntegrations |
                GatewayIntents.GuildMessages |
                GatewayIntents.Guilds |
                GatewayIntents.GuildMessageTyping |
                GatewayIntents.GuildMessageReactions |
                GatewayIntents.MessageContent
        })
        .AddSingleton<DiscordSocketClient>()
        .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
        .AddSingleton<InteractionHandler>()
        .BuildServiceProvider();

    private static void Main(string[] args) => new Program()
        .MainAsync()
        .GetAwaiter()
        .GetResult();

    private async Task MainAsync() {
        await _services.GetRequiredService<InteractionHandler>().InitializeAsync();

        DiscordSocketClient client = _services.GetRequiredService<DiscordSocketClient>();

        client.Log += log => {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        };
        client.Ready += () => {
            Console.WriteLine($"{client.CurrentUser} is connected!");
            return Task.CompletedTask;
        };

        await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
        await client.StartAsync();
        await Task.Delay(Timeout.Infinite);
    }
}
