using Discord;

namespace GeodeDiscord;

public static class Util {
    public static IEnumerable<Embed> MessageToEmbeds(IMessage message) {
        string authorName = message.Author.GlobalName ?? (message.Author.DiscriminatorValue == 0 ?
            message.Author.Username : $"{message.Author.Username}{message.Author.Discriminator}");
        string authorIcon = message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl();

        List<IAttachment> embeddableAttachments =
            message.Attachments.Where(att =>
                !att.IsSpoiler() && att.ContentType.StartsWith("image/", StringComparison.Ordinal)).ToList();

        yield return new EmbedBuilder()
            .WithAuthor(authorName, authorIcon)
            .WithDescription($"{message.Content}\n\n{(message.Channel is null ? "" : message.GetJumpUrl())}")
            .WithImageUrl(embeddableAttachments.Count > 0 ? embeddableAttachments[0].Url : null)
            .Build();

        foreach (IAttachment attachment in embeddableAttachments.Skip(1).Take(9))
            yield return new EmbedBuilder()
                .WithImageUrl(attachment.Url)
                .Build();
    }
}
