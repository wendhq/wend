using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's SQLite database.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<List> Lists => Set<List>();
    public DbSet<Card> Cards => Set<Card>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Hide soft-deleted / archived cards from every query. Plans 6-7 set these timestamps;
        // until then the filter is inert (no card ever has them set).
        modelBuilder.Entity<Card>().HasQueryFilter(c => c.DeletedAt == null && c.ArchivedAt == null);
    }
}
