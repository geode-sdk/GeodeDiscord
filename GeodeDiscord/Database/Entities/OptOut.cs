using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GeodeDiscord.Database.Entities;

public record OptOut {
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ulong userId { get; init; }
}
