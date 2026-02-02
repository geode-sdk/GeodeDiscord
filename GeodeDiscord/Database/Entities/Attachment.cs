using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[Index(nameof(url), IsUnique = true)]
public record Attachment {
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int id { get; private set; }
    public required string url { get; init; }
    public required string? contentType { get; init; }

    [ForeignKey("fileInQuoteMessageId")]
    public virtual Quote? fileIn { get; private set; }
    [ForeignKey("embedInQuoteMessageId")]
    public virtual ICollection<Quote> embedIn { get; private set; } = [];
}
