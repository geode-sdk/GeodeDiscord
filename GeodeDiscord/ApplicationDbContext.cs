using GeodeDiscord.Entities;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord;

public class ApplicationDbContext : DbContext {
    public DbSet<Quote> quotes { get; set; } = null!;

    public string dbPath { get; } =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeodeDiscord.db");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
}
