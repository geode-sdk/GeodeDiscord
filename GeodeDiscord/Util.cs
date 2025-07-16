using System.Text;

using Discord;

using GeodeDiscord.Database.Entities;

using Serilog.Events;

namespace GeodeDiscord;

public static class Util {
    private static string[] GetMessageImages(IMessage message) => message.Attachments
        .Where(att => !att.IsSpoiler() && att.ContentType.StartsWith("image/", StringComparison.Ordinal))
        .Take(10)
        .Select(att => att.Url)
        .ToArray();

    private static async Task<IMessage?> GetReplyAsync(IMessage message) {
        if (message.Channel is null || message.Reference is null ||
            message.Reference.ChannelId != message.Channel.Id ||
            !message.Reference.MessageId.IsSpecified)
            return null;
        ulong refMessageId = message.Reference.MessageId.Value;
        IMessage? refMessage = await message.Channel.GetMessageAsync(refMessageId);
        return refMessage ?? null;
    }

    private static async Task<IMessage?> GetForwardedAsync(IMessage message) {
        if (message.Channel is null || message.Reference is null ||
            message.Reference.ChannelId != message.Channel.Id ||
            !message.Reference.MessageId.IsSpecified ||
            message.Reference.ReferenceType.GetValueOrDefault() != MessageReferenceType.Forward)
            return null;
        ulong refMessageId = message.Reference.MessageId.Value;
        IMessage? refMessage = await message.Channel.GetMessageAsync(refMessageId);
        return refMessage ?? null;
    }

    public static Task<Quote> MessageToQuote(ulong quoterId, string name, IMessage message, Quote? original = null) =>
        MessageToQuote(quoterId, name, message, DateTimeOffset.Now, original);

    public static async Task<Quote> MessageToQuote(ulong quoterId, string name, IMessage message,
        DateTimeOffset timestamp, Quote? original = null) {
        while (true) {
            // if we're just quoting a forwarded message, quote the forwarded message instead
            IMessage? forwarded = await GetForwardedAsync(message);
            if (forwarded is not null) {
                message = forwarded;
                continue;
            }

            string[] images = GetMessageImages(message);
            int extraAttachments = message.Attachments.Count - images.Length;
            ulong replyAuthorId = (await GetReplyAsync(message))?.Author.Id ?? 0;
            return new Quote {
                name = name,
                messageId = message.Id,
                channelId = message.Channel?.Id ?? 0,
                createdAt = original?.createdAt ?? timestamp,
                lastEditedAt = timestamp,
                quoterId = quoterId,
                authorId = message.Author.Id,
                replyAuthorId = replyAuthorId,
                jumpUrl = message.Channel is null ? null : message.GetJumpUrl(),
                images = images,
                extraAttachments = extraAttachments,
                content = message.Content
            };
        }
    }

    public static IEnumerable<Embed> QuoteToEmbeds(Quote quote) {
        StringBuilder description = new();
        description.AppendLine(quote.content);
        description.AppendLine();
        description.Append("\\- <@");
        description.Append(quote.authorId);
        description.Append('>');
        if (quote.replyAuthorId != 0) {
            description.Append(" to <@");
            description.Append(quote.replyAuthorId);
            description.Append('>');
        }
        description.Append(" in ");
        description.Append(quote.jumpUrl ?? "<unknown>");
        description.Append(" by <");
        if (quote.quoterId == 0) {
            description.Append("unknown");
        }
        else {
            description.Append('@');
            description.Append(quote.quoterId);
        }
        description.Append('>');
        if (quote.createdAt != quote.lastEditedAt) {
            description.AppendLine();
            description.AppendLine();
            description.Append("Last edited at <t:");
            description.Append(quote.lastEditedAt.ToUnixTimeSeconds());
            description.Append(":f>");
        }

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
            .WithImageUrl(quote.images.Length > 0 ? quote.images[0] : null)
            .WithTimestamp(quote.createdAt)
            .WithFooter(footer.ToString())
            .Build();

        foreach (string image in quote.images.Skip(1))
            yield return new EmbedBuilder()
                .WithImageUrl(image)
                .Build();
    }

    public static MessageReference QuoteToForward(Quote quote) => new(
        quote.messageId, quote.channelId, null, false, MessageReferenceType.Forward
    );

    public static IEnumerable<Embed> QuoteToCensoredEmbeds(Quote quote) {
        StringBuilder description = new();
        description.AppendLine(quote.content);
        description.AppendLine("\\- ????? in ????? by ?????");

        StringBuilder footer = new();
        if (quote.extraAttachments != 0) {
            footer.Append(quote.extraAttachments.ToString("+0;-#"));
            footer.Append(" attachment");
            if (quote.extraAttachments != 1)
                footer.Append('s');
        }

        yield return new EmbedBuilder()
            .WithAuthor("?????")
            .WithDescription(description.ToString())
            .WithImageUrl(quote.images.Length > 0 ? quote.images[0] : null)
            .WithFooter(footer.ToString())
            .Build();

        foreach (string image in quote.images.Skip(1))
            yield return new EmbedBuilder()
                .WithImageUrl(image)
                .Build();
    }

    public static LogEventLevel DiscordToSerilogLevel(LogSeverity x) => x switch {
        LogSeverity.Critical => LogEventLevel.Fatal,
        LogSeverity.Error => LogEventLevel.Error,
        LogSeverity.Warning => LogEventLevel.Warning,
        LogSeverity.Info => LogEventLevel.Information,
        LogSeverity.Verbose => LogEventLevel.Verbose,
        LogSeverity.Debug => LogEventLevel.Debug,
        _ => LogEventLevel.Information
    };
}
