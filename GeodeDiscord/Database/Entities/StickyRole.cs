using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[Index(nameof(userId)), Index(nameof(roleId))]
public record StickyRole
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ulong userId { get; init; }
    public required ulong roleId { get; init; }
}
