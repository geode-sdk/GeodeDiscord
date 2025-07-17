using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[Index(nameof(id), IsUnique = true), Index(nameof(name)), Index(nameof(authorId))]
public record Quote {
    // basic
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ulong messageId { get; init; }
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required int id { get; init; }
    public string name { get; set; } = "";
    public required ulong channelId { get; init; }
    public required DateTimeOffset createdAt { get; init; }
    public required DateTimeOffset lastEditedAt { get; init; }
    public required ulong quoterId { get; init; }

    // author
    public required ulong authorId { get; init; }
    public required ulong replyAuthorId { get; init; }
    public string? jumpUrl { get; init; }

    // attachments
    public required string[] images { get; init; }
    public required int extraAttachments { get; init; }

    // content
    public required string content { get; init; }

    public string GetFullName() => string.IsNullOrWhiteSpace(name) ? id.ToString() : $"{id}: {name}";
}
