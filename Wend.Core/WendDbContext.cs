using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's SQLite database.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
}
