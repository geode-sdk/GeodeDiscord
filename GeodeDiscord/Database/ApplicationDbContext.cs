using GeodeDiscord.Database.Entities;

using Microsoft.EntityFrameworkCore;

namespace GeodeDiscord.Database;

public class ApplicationDbContext : DbContext {
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Quote> quotes { get; set; } = null!;
}
