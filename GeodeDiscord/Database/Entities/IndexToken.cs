using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[Index(nameof(userId), IsUnique = true)]
public record IndexToken
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ulong userId { get; init; }
    public required string indexToken { get; set; }
}
