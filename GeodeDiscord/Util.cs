using System.Text;

using Discord;

using GeodeDiscord.Database.Entities;

namespace GeodeDiscord;

public static class Util {
    private static List<string> GetMessageImages(IMessage message) => message.Attachments
        .Where(att => !att.IsSpoiler() && att.ContentType.StartsWith("image/", StringComparison.Ordinal))
        .Take(10)
        .Select(att => att.Url)
        .ToList();

    private static async Task<IMessage?> GetReplyAsync(IMessage message) {
        if (message.Channel is null || message.Reference is null ||
            message.Reference.ChannelId != message.Channel.Id ||
            !message.Reference.MessageId.IsSpecified)
            return null;
        ulong refMessageId = message.Reference.MessageId.Value;
        IMessage? refMessage = await message.Channel.GetMessageAsync(refMessageId);
        return refMessage ?? null;
    }

    public static async Task<Quote> MessageToQuote(string name, IMessage message, Quote? original = null) {
        List<string> images = GetMessageImages(message);
        int extraAttachments = message.Attachments.Count - images.Count;
        ulong replyAuthorId = (await GetReplyAsync(message))?.Author.Id ?? 0;
        DateTimeOffset timestamp = DateTimeOffset.Now;
        return new Quote {
            name = name,
            messageId = message.Id,
            channelId = message.Channel?.Id ?? 0,
            createdAt = original?.createdAt ?? timestamp,
            lastEditedAt = timestamp,

            authorId = message.Author.Id,
            replyAuthorId = replyAuthorId,
            jumpUrl = message.Channel is null ? null : message.GetJumpUrl(),

            images = string.Join('|', images),
            extraAttachments = extraAttachments,
            content = message.Content
        };
    }

    public static IEnumerable<Embed> QuoteToEmbeds(Quote quote) {
        StringBuilder description = new();
        description.AppendLine(quote.content);
        description.AppendLine();
        description.Append("\\- <@");
        description.Append(quote.authorId);
        description.Append('>');
        if (quote.replyAuthorId != 0) {
            description.Append(" in response to <@");
            description.Append(quote.replyAuthorId);
            description.Append('>');
        }
        description.Append(" in ");
        description.Append(quote.jumpUrl ?? "*[ missing jump url ]*");
        if (quote.createdAt != quote.lastEditedAt) {
            description.AppendLine();
            description.AppendLine();
            description.Append("Last edited at <t:");
            description.Append(quote.lastEditedAt.ToUnixTimeSeconds());
            description.Append(":f>");
        }

        string[] images = quote.images.Split('|');

        StringBuilder footer = new();
        if (quote.extraAttachments != 0) {
            footer.Append(quote.extraAttachments.ToString("+0;-#"));
            footer.Append(" attachment");
            if (quote.extraAttachments != 1)
                footer.Append('s');
        }

        yield return new EmbedBuilder()
            .WithAuthor(quote.name)
            .WithDescription(description.ToString())
            .WithImageUrl(images.Length > 0 ? images[0] : null)
            .WithTimestamp(quote.createdAt)
            .WithFooter(footer.ToString())
            .Build();

        foreach (string image in images.Skip(1))
            yield return new EmbedBuilder()
                .WithImageUrl(image)
                .Build();
    }
}
