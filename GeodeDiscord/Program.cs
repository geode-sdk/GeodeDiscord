using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

using GeodeDiscord.Database;
using GeodeDiscord.Modules;

using Microsoft.EntityFrameworkCore.Sqlite.Query.Internal;
using Microsoft.Extensions.DependencyInjection;

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GeodeDiscord;

public static class Program {
    public static IReadOnlyList<LogEvent> log => logs;
    private static readonly List<LogEvent> logs = [];

    private static readonly IServiceProvider services = new ServiceCollection()
        .AddDbContext<ApplicationDbContext>()
        .AddSingleton(new DiscordSocketConfig {
            GatewayIntents =
                GatewayIntents.GuildIntegrations |
                GatewayIntents.GuildMessages |
                GatewayIntents.GuildMembers |
                GatewayIntents.Guilds |
                GatewayIntents.MessageContent
        })
        .AddSingleton<DiscordSocketClient>()
        .AddSingleton<IRestClientProvider>(x => x.GetRequiredService<DiscordSocketClient>())
        .AddSingleton(new InteractionServiceConfig {
            InteractionCustomIdDelimiters = ['/']
        })
        .AddSingleton<InteractionService>()
        .AddSingleton<InteractionHandler>()
        .BuildServiceProvider();

    private class ListSink : ILogEventSink {
        public void Emit(LogEvent log) {
            if (logs.Count >= 100)
                logs.RemoveAt(0);
            logs.Add(log);
        }
    }

    private static async Task Main() {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Sink<ListSink>()
            .CreateLogger();

        PatchEfCoreSqlite();

        DiscordSocketClient client = services.GetRequiredService<DiscordSocketClient>();

        client.Log += message => {
            Log.Write(Util.DiscordToSerilogLevel(message.Severity), message.Exception, "[{Source}] {Message}",
                message.Source, message.Message);
            return Task.CompletedTask;
        };

        client.UserJoined += async user => {
            if (user.Guild.Id != 911701438269386882)
                return;
            await StickyModule.OnUserJoined(user, services.GetRequiredService<ApplicationDbContext>());
        };

        client.Ready += async () => {
            Log.Information("Caching all users");
            await client.DownloadUsersAsync(client.Guilds);
        };
        client.GuildMembersDownloaded += guild => {
            Log.Information("{Count} members downloaded for guild {Guild}", guild.DownloadedMemberCount, guild.Name);
            return Task.CompletedTask;
        };

        await services.GetRequiredService<InteractionHandler>().InitializeAsync();

        await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
        await client.StartAsync();
        await Task.Delay(Timeout.Infinite);
    }

    // 🔥
    private static void PatchEfCoreSqlite() {
#pragma warning disable EF1001
        Type type = typeof(SqliteObjectToStringTranslator);
        FieldInfo? field = type.GetField("TypeMapping", BindingFlags.NonPublic | BindingFlags.Static);
#pragma warning restore EF1001
        if (field is null) {
            Log.Error("[EF Core Sqlite patch] Could not find TypeMapping");
            return;
        }
        if (field.GetValue(null) is not HashSet<Type> typeMapping) {
            Log.Error("[EF Core Sqlite patch] TypeMapping is not HashSet<Type>");
            return;
        }
        bool res = typeMapping.Add(typeof(ulong));
        if (!res) {
            Log.Warning("[EF Core Sqlite patch] TypeMapping already contains ulong, the patch is no longer needed! 🥳");
        }
    }
}
