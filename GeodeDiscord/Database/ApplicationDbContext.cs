using GeodeDiscord.Database.Entities;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database;

public class ApplicationDbContext : DbContext {
    public DbSet<Quote> quotes { get; set; } = null!;
    public DbSet<Guess> guesses { get; set; } = null!;
    public DbSet<StickyRole> stickyRoles { get; set; } = null!;

    public string dbPath { get; } = Environment.GetEnvironmentVariable("GEODE_BOT_DB_PATH") ??
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeodeDiscord.db");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseLazyLoadingProxies()
            .UseSqlite($"Data Source={dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Quote>()
            .OwnsMany(x => x.attachments);
        modelBuilder.Entity<Quote>()
            .OwnsMany(x => x.embeds);

        modelBuilder.Entity<Quote>()
            .OwnsMany(x => x.embeds)
            .Property(x => x.type)
            .HasConversion<string>();
        modelBuilder.Entity<Quote>()
            .OwnsMany(x => x.embeds)
            .Property(x => x.color)
            .HasConversion<uint?>(x => x, x => x);

        modelBuilder.Entity<Quote>()
            .OwnsMany(x => x.embeds)
            .OwnsMany(x => x.fields);

        modelBuilder.Entity<Guess>()
            .HasOne(x => x.quote)
            .WithMany();
    }
}
