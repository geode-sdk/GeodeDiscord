using System.Diagnostics;

using Discord;
using Discord.Interactions;

using JetBrains.Annotations;

using Serilog;

namespace GeodeDiscord.Modules;

public partial class RootModule {
    [MessageCommand("Stackwalk"), EnabledInDm(false), UsedImplicitly]
    public async Task StackwalkMessage(IMessage message) {
        IAttachment? dump =
            message.Attachments.FirstOrDefault(x => x?.Filename.EndsWith(".dmp", StringComparison.Ordinal) ?? false,
                null);
        if (dump is null) {
            await RespondAsync("❌ No dump attached to this message", ephemeral: true);
            return;
        }
        await Stackwalk(dump);
    }

    [SlashCommand("stackwalk", "Run minidump-stackwalk with the attached files"), EnabledInDm(false),
     UsedImplicitly]
    public Task StackwalkCommand(Attachment dump, Attachment? sym1 = null, Attachment? sym2 = null) =>
        Stackwalk(dump, sym1, sym2);

    private async Task Stackwalk(IAttachment dump, IAttachment? sym1 = null, IAttachment? sym2 = null) {
        string dir = Path.GetFullPath($"stackwalk-{dump.Id}");
        string logPath = Path.Combine(dir, $"{dump.Filename}.txt");
        string dumpPath = Path.Combine(dir, $"{dump.Filename}");
        string sym1Path = sym1 is null ? "" : Path.Combine(dir, $"{sym1.Filename}");
        string sym2Path = sym2 is null ? "" : Path.Combine(dir, $"{sym2.Filename}");

        try {
            await DeferAsync();

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string symbolsStr = sym1 is null ? "" :
                sym2 is null ? $" and symbols {sym1.Filename}" :
                $" and symbols {sym1.Filename} and {sym2.Filename}";
            await FollowupAsync($"⬇️ Downloading dump {dump.Filename}{symbolsStr}...");

            using (HttpClient client = new()) {
                await File.WriteAllBytesAsync(dumpPath, await client.GetByteArrayAsync(dump.Url));
                if (sym1 is not null)
                    await File.WriteAllBytesAsync(sym1Path, await client.GetByteArrayAsync(sym1.Url));
                if (sym2 is not null)
                    await File.WriteAllBytesAsync(sym2Path, await client.GetByteArrayAsync(sym2.Url));
            }

            await ModifyOriginalResponseAsync(x => x.Content = $"🔥 Processing dump {dump.Filename}...");
            Process process = new();
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.FileName = "./minidump-stackwalk";
            process.StartInfo.Arguments =
                $"--output-file {logPath} --symbols-url https://symbols.xyze.dev/ {dumpPath} {sym1Path} {sym2Path}";
            process.Start();
            await process.WaitForExitAsync();

            using FileAttachment attachment = new(logPath);
            await ModifyOriginalResponseAsync(x => {
                x.Content = "✅ Done! :3";
                x.Attachments = new[] { attachment };
            });
        }
        catch (Exception ex) {
            await ModifyOriginalResponseAsync(x => {
                x.Content = $"❌ Fail :<\n{ex}";
            });
            Log.Error(ex, "Stackwalk fail");
        }
        finally {
            // sleep for 2 seconds cuz im too lazy to write actual logic for waiting for the file to be available
            Thread.Sleep(TimeSpan.FromSeconds(2));
            try {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex) {
                Log.Error(ex, "Cleanup fail");
                await FollowupAsync($"⚠️ Failed cleanup :<\n{ex}");
            }
        }
    }
}
