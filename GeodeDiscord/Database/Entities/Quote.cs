namespace GeodeDiscord.Database.Entities;

public record Quote(
    ulong messageId, string name, string? jumpUrl, DateTimeOffset timestamp, // basic
    string authorName, string authorIcon, ulong authorId, // author
    List<string> images, int extraAttachments, // attachments
    string content, ulong replyAuthorId // content
);
