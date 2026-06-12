using System.Globalization;
using System.Reflection;

using Discord;
using Discord.API.Gateway;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

using GeodeDiscord.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite.Query.Internal;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
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
                GatewayIntents.Guilds |
                GatewayIntents.MessageContent
        })
        .AddSingleton<DiscordSocketClient>()
        .AddSingleton<IRestClientProvider>(x => x.GetRequiredService<DiscordSocketClient>())
        .AddSingleton(new InteractionServiceConfig {
            InteractionCustomIdDelimiters = ['/'],
            DefaultRunMode = RunMode.Sync, // i wanna handle my errors myself so i implement async myself :-)
            AutoServiceScopes = false
        })
        .AddSingleton<InteractionService>()
        .AddSingleton<InteractionHandler>()
        .AddScoped<InteractionProvider>()
        .AddScoped<SocketInteractionContext>(x => {
            InteractionProvider interactionProvider = x.GetRequiredService<InteractionProvider>();
            if (interactionProvider.interaction is null)
                return null!; // i dont care lol
            return new SocketInteractionContext(
                x.GetRequiredService<DiscordSocketClient>(),
                interactionProvider.interaction
            );
        })
        .AddScoped<QuoteEditor>()
        .AddSingleton<QuoteRenderer>()
        .BuildServiceProvider();

    private class ListSink : ILogEventSink {
        public void Emit(LogEvent log) {
            if (logs.Count >= 100)
                logs.RemoveAt(0);
            logs.Add(log);
        }
    }

    private static async Task Main() {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Sink<ListSink>()
            .CreateLogger();

        PatchEfCoreSqlite();

        ApplicationDbContext db = services.GetRequiredService<ApplicationDbContext>();
        DiscordSocketClient client = services.GetRequiredService<DiscordSocketClient>();

        db.SavedChanges += (_, args) => {
            Log.Information(
                "Saved {Count} {EntityNoun}",
                args.EntitiesSavedCount,
                args.EntitiesSavedCount == 1 ? "entity" : "entities"
            );
        };

        client.Log += message => {
            Log.Write(Util.DiscordToSerilogLevel(message.Severity), message.Exception, "[{Source}] {Message}",
                message.Source, message.Message);
            return Task.CompletedTask;
        };

        client.Ready += async () => await CacheQuotedUsers(db, client);

        client.MessageReceived += async message => {
            if (message.Channel.Id != 1202076732346077235)
                return;
            if (message is not SocketUserMessage userMessage)
                return;
            if (!message.Author.IsWebhook)
                return;
            await userMessage.CrosspostAsync();
        };

        InteractionHandler interactionHandler = services.GetRequiredService<InteractionHandler>();
        await interactionHandler.InitializeAsync();

        await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
        await client.StartAsync();

        Console.CancelKeyPress += (_, args) => {
            args.Cancel = true;
            interactionHandler.TeardownAsync().Wait();
            client.StopAsync().Wait();
            Environment.Exit(0);
        };

        await Task.Delay(Timeout.Infinite);
    }

    private static async Task CacheQuotedUsers(ApplicationDbContext db, DiscordSocketClient client) {
        List<ulong> userIds = await db.quotes.Select(x => x.authorId).Distinct().ToListAsync();
        HashSet<ulong> missingUserIds = userIds.ToHashSet();

        Log.Information("Caching {Count} quoted users", userIds.Count);

        Dictionary<ulong, (TaskCompletionSource completion, int count, int total, int members)> progresses = [];

        client.ApiClient.ReceivedGatewayEvent += OnReceivedGatewayEvent;

        foreach (SocketGuild guild in client.Guilds) {
            Log.Information("Downloading members for guild {Guild}", guild);
            await client.ApiClient.SendGatewayAsync(GatewayOpCode.RequestGuildMembers, new {
                guild_id = guild.Id,
                user_ids = userIds
            }, new RequestOptions());
            progresses[guild.Id] = (new TaskCompletionSource(), 0, int.MaxValue, 0);
        }

        await Task.WhenAll(progresses.Values.Select(x => x.completion.Task));

        client.ApiClient.ReceivedGatewayEvent -= OnReceivedGatewayEvent;

        foreach (ulong id in missingUserIds.ToArray()) {
            if (await Util.GetUserAsync(client, id) is not null)
                missingUserIds.Remove(id);
        }
        return;

        Task OnReceivedGatewayEvent(GatewayOpCode opCode, int? _, string type, object payloadObj) {
            if (opCode != GatewayOpCode.Dispatch || type != "GUILD_MEMBERS_CHUNK")
                return Task.CompletedTask;
            JToken payload = (JToken)payloadObj;
            SocketGuild guild = client.GetGuild(payload.Value<ulong>("guild_id"));
            JArray members = payload.Value<JArray>("members")!;
            foreach (ulong id in members.Select(x => x["user"]!.Value<ulong>("id")))
                missingUserIds.Remove(id);
            int index = payload.Value<int>("chunk_index");
            int count = payload.Value<int>("chunk_count");
            (TaskCompletionSource completion, int count, int total, int members) progress = progresses[guild.Id];
            progress.count++;
            progress.total = count;
            progress.members += members.Count;
            progresses[guild.Id] = progress;
            Log.Information(
                "Downloaded members chunk {Index} for guild {Guild}: {Count}/{Total} ({Members} members)",
                index, guild, progress.count, progress.total, members.Count
            );
            if (progress.count < progress.total)
                return Task.CompletedTask;
            Log.Information("{Count} members downloaded for guild {Guild}", progress.members, guild);
            progress.completion.SetResult();
            return Task.CompletedTask;
        }
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
