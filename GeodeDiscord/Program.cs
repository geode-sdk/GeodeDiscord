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
                GatewayIntents.MessageContent
        })
        .AddSingleton<DiscordSocketClient>()
        .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig {
                InteractionCustomIdDelimiters = new[] { '/' }
            }))
        .AddSingleton<InteractionHandler>()
        .BuildServiceProvider();

    private static void Main(string[] args) => new Program()
        .MainAsync()
        .GetAwaiter()
        .GetResult();

    private async Task MainAsync() {
        DiscordSocketClient client = _services.GetRequiredService<DiscordSocketClient>();

        client.Log += log => {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        };
        client.Ready += () => {
            Console.WriteLine($"{client.CurrentUser} is connected!");
            return Task.CompletedTask;
        };
        client.ApplicationCommandCreated += c => {
            Console.WriteLine(c.Name);
            return Task.CompletedTask;
        };

        await _services.GetRequiredService<InteractionHandler>().InitializeAsync();

        await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
        await client.StartAsync();
        await Task.Delay(Timeout.Infinite);
    }
}
