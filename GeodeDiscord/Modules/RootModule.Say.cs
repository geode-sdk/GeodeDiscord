using System.Text;
using Discord;
using Discord.Interactions;
using JetBrains.Annotations;

namespace GeodeDiscord.Modules;

public partial class RootModule {
    [SlashCommand("say", "Make the bot say something as you."),
     CommandContextType(InteractionContextType.Guild),
     DefaultMemberPermissions(GuildPermission.Administrator),
     UsedImplicitly]
    public async Task Say(string message, Attachment? a0 = null, Attachment? a1 = null, Attachment? a2 = null) {
        IUser? user = await Util.GetUserAsync(Context.Client, Context.User.Id);
        string content = $"`@{user?.GlobalName ?? Context.User.Id.ToString()}`: {message}";
        List<Attachment> attachments = Enumerable.Empty<Attachment?>()
            .Append(a0).Append(a1).Append(a2)
            .Where(x => x is not null)
            .Cast<Attachment>()
            .ToList();
        if (attachments.Count == 0) {
            await RespondAsync(content);
            return;
        }
        StringBuilder attachmentsText = new();
        foreach (Attachment attachment in attachments) {
            string name = string.IsNullOrWhiteSpace(attachment.Title) ? attachment.Filename :
                attachment.Title + Path.GetExtension(attachment.Filename);
            if (attachment.IsSpoiler())
                attachmentsText.Append("||");
            attachmentsText.Append($"`{name}`:");
            attachmentsText.Append($" `{Util.FormatSize(attachment.Size)}`");
            attachmentsText.Append($" [download]({attachment.Url})");
            if (attachment.Description is not null)
                attachmentsText.Append($" (`{attachment.Description}`)");
            if (attachment.IsSpoiler())
                attachmentsText.Append("||");
            attachmentsText.AppendLine();
        }
        await RespondAsync($"{content}\n{attachmentsText}");
    }
}
