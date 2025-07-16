using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database.Entities;

[PrimaryKey(nameof(userId))]
public record GuessStats {
    public required ulong userId { get; init; }
    public ulong total { get; set; }
    public ulong correct { get; set; }
    public ulong streak { get; set; }
    public ulong maxStreak { get; set; }
}
