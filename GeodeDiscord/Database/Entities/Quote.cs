using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[PrimaryKey(nameof(messageId), nameof(name))]
public class Quote {
    // basic
    public required ulong messageId { get; init; }
    public required string name { get; set; }
    public string? jumpUrl { get; init; }
    public required DateTimeOffset timestamp { get; init; }

    // author
    public required string authorName { get; init; }
    public required string authorIcon { get; init; }
    public required ulong authorId { get; init; }

    // attachments
    public required string images { get; init; }
    public required int extraAttachments { get; init; }

    // content
    public required string content { get; init; }
    public required ulong replyAuthorId { get; init; }
}
