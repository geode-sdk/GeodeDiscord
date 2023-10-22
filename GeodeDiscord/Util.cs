using System.Text;

using Discord;

using GeodeDiscord.Database.Entities;

namespace GeodeDiscord;

public static class Util {
    private static string GetUserDisplayName(IUser user) => user.GlobalName ?? (user.DiscriminatorValue == 0 ?
        user.Username : $"{user.Username}{user.Discriminator}");
    private static string GetUserAvatarUrl(IUser user) => user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();

    private static List<string> GetMessageImages(IMessage message) => message.Attachments
        .Where(att => !att.IsSpoiler() && att.ContentType.StartsWith("image/", StringComparison.Ordinal))
        .Take(10)
        .Select(att => att.Url)
        .ToList();

    private static async Task<IMessage?> GetReplyAsync(IMessage message) {
        if (message.Channel is null ||
            message.Reference.ChannelId != message.Channel.Id ||
            !message.Reference.MessageId.IsSpecified)
            return null;
        ulong refMessageId = message.Reference.MessageId.Value;
        IMessage? refMessage = await message.Channel.GetMessageAsync(refMessageId);
        return refMessage ?? null;
    }

    public static async Task<Quote> MessageToQuote(string name, IMessage message) {
        string authorName = GetUserDisplayName(message.Author);
        string authorIcon = GetUserAvatarUrl(message.Author);
        List<string> images = GetMessageImages(message);
        int extraAttachments = message.Attachments.Count - images.Count;
        ulong replyAuthorId = (await GetReplyAsync(message))?.Author.Id ?? 0;
        return new Quote(
            message.Id, name, message.Channel is null ? null : message.GetJumpUrl(), DateTimeOffset.Now,
            authorName, authorIcon, message.Author.Id,
            images, extraAttachments,
            message.Content, replyAuthorId
        );
    }

    public static async IAsyncEnumerable<Embed> QuoteToEmbeds(IGuild guild, Quote quote) {
        string authorName = quote.authorName;
        string authorIcon = quote.authorIcon;
        IGuildUser? authorUser = await guild.GetUserAsync(quote.authorId);
        if (authorUser is not null) {
            authorName = authorUser.DisplayName;
            authorIcon = authorUser.GetDisplayAvatarUrl();
        }

        StringBuilder description = new();
        if (quote.replyAuthorId != 0) {
            description.Append("> *replied to <@");
            description.Append(quote.replyAuthorId);
            description.AppendLine(">*");
        }
        description.AppendLine(quote.content);
        description.AppendLine();
        description.Append(quote.jumpUrl ?? "*[ missing jump url ]*");

        StringBuilder footer = new();
        if (quote.extraAttachments != 0) {
            footer.Append(quote.extraAttachments.ToString("+0;-#"));
            footer.Append(" attachment");
            if (quote.extraAttachments != 1)
                footer.Append('s');
        }

        yield return new EmbedBuilder()
            .WithTitle($"Quote {quote.name}")
            .WithAuthor(authorName, authorIcon, $"discord:/users/{quote.authorId}")
            .WithDescription(description.ToString())
            .WithImageUrl(quote.images.Count > 0 ? quote.images[0] : null)
            .WithTimestamp(quote.timestamp)
            .WithFooter(footer.ToString())
            .Build();

        foreach (string image in quote.images.Skip(1))
            yield return new EmbedBuilder()
                .WithImageUrl(image)
                .Build();
    }
}
