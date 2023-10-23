using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace GeodeDiscord.Entities;

public class Quote {
    // basic
    [Key]
    public required string name { get; init; }
    public required ulong messageId { get; init; }
    public required ulong channelId { get; init; }
    public required DateTimeOffset createdAt { get; init; }
    public required DateTimeOffset lastEditedAt { get; init; }

    // author
    public required ulong authorId { get; init; }
    public required ulong replyAuthorId { get; init; }
    public string? jumpUrl { get; init; }

    // attachments
    public required string images { get; init; }
    public required int extraAttachments { get; init; }

    // content
    public required string content { get; init; }

    public Quote WithName(string name) => new() {
        name = name,
        messageId = messageId,
        channelId = channelId,
        createdAt = createdAt,
        lastEditedAt = lastEditedAt,
        authorId = authorId,
        replyAuthorId = replyAuthorId,
        jumpUrl = jumpUrl,
        images = images,
        extraAttachments = extraAttachments,
        content = content
    };

    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    public static string GetRandomName() {
        const string possibleChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        Span<char> chars = stackalloc char[7];
        for (int i = 0; i < 7; i++)
            chars[i] = possibleChars[Random.Shared.Next(possibleChars.Length)];
        return new string(chars);
    }
}
