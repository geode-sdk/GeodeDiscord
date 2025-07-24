using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[PrimaryKey(nameof(messageId)), Index(nameof(guessedAt)), Index(nameof(userId)), Index(nameof(quote))]
public record Guess {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ulong messageId { get; init; }
    public required DateTimeOffset guessedAt { get; init; }
    public required ulong userId { get; init; }
    public required ulong guessId { get; init; }
    [ForeignKey("quoteMessageId")]
    public required Quote quote { get; init; }
}
