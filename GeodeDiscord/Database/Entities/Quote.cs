using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Discord;
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
    public string? jumpUrl { get; init; }

    // attachments
    public virtual required ICollection<Attachment> attachments { get; init; }
    public virtual required ICollection<Embed> embeds { get; init; }

    // content
    public required string content { get; init; }

    // reply
    public required ulong replyAuthorId { get; init; }
    public required ulong replyMessageId { get; init; }
    public required string replyContent { get; init; }

    public string GetFullName() {
        string idStr = id == 0 ? "tbd" : id.ToString();
        return string.IsNullOrWhiteSpace(name) ? idStr : $"{idStr}: {name}";
    }

    [Owned]
    public record Attachment {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public required ulong id { get; init; }
        public required string name { get; init; }
        public required int size { get; init; }
        public required string url { get; init; }
        public required string? contentType { get; init; }
        public required string? description { get; init; }
        public required bool isSpoiler { get; init; }
    }

    [Owned]
    public record Embed {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong id { get; private set; }

        // probably useless?
        public required EmbedType type { get; init; }

        // useless in this context but ill keep it anyway just in case,
        // storing one extra int isnt expensive
        public required Color? color { get; init; }

        // properties sorted in order of and grouped by how they generally appear in visually
        // left to right, top to bottom

        public required string? providerName { get; init; }
        public required string? providerUrl { get; init; }

        public required string? authorIconUrl { get; init; }
        public required string? authorName { get; init; }
        public required string? authorUrl { get; init; }

        public required string? title { get; init; }
        public required string? url { get; init; }

        public required string? thumbnailUrl { get; init; }

        public required string? description { get; init; }

        public virtual required ICollection<Field> fields { get; init; }

        public required string? videoUrl { get; init; }
        public required string? imageUrl { get; init; }

        public required string? footerIconUrl { get; init; }
        public required string? footerText { get; init; }
        public required DateTimeOffset? timestamp { get; init; }

        [Owned]
        public record Field {
            [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public ulong id { get; private set; }

            public required string name { get; init; }
            public required string value { get; init; }
            public required bool inline { get; init; }
        }
    }
}
