using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[PrimaryKey(nameof(userId), nameof(roleId)), Index(nameof(userId)), Index(nameof(roleId))]
public record StickyRole {
    public required ulong userId { get; init; }
    public required ulong roleId { get; init; }
}
