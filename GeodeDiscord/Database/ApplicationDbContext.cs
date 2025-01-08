﻿using GeodeDiscord.Database.Entities;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database;

public class ApplicationDbContext : DbContext {
    public DbSet<Quote> quotes { get; set; } = null!;
    public DbSet<StickyRole> stickyRoles { get; set; } = null!;
    public DbSet<IndexToken> indexTokens { get; set; } = null!;

    public string dbPath { get; } = Environment.GetEnvironmentVariable("GEODE_BOT_DB_PATH") ??
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeodeDiscord.db");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
}
