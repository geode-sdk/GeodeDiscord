using System.ComponentModel.DataAnnotations;

namespace GeodeDiscord.Database.Entities;

public class Quote {
    // basic
    [Key]
    public required string name { get; init; }
    public required ulong messageId { get; init; }
    public required ulong channelId { get; init; }
    public required DateTimeOffset createdAt { get; init; }
    public required DateTimeOffset lastEditedAt { get; init; }
    public required ulong quoterId { get; init; }

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
        quoterId = quoterId,
        authorId = authorId,
        replyAuthorId = replyAuthorId,
        jumpUrl = jumpUrl,
        images = images,
        extraAttachments = extraAttachments,
        content = content
    };

    public Quote WithQuoter(ulong quoterId) => new() {
        name = name,
        messageId = messageId,
        channelId = channelId,
        createdAt = createdAt,
        lastEditedAt = lastEditedAt,
        quoterId = quoterId,
        authorId = authorId,
        replyAuthorId = replyAuthorId,
        jumpUrl = jumpUrl,
        images = images,
        extraAttachments = extraAttachments,
        content = content
    };
}
