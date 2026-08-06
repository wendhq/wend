using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

/// <summary>EF Core context for Wend's PostgreSQL database, including ASP.NET Identity's schema.</summary>
public class WendDbContext(DbContextOptions<WendDbContext> options)
    : IdentityDbContext<WendUser>(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<List> Lists => Set<List>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<CardLabel> CardLabels => Set<CardLabel>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own mapping must run first; Wend's configuration layers on top.
        base.OnModelCreating(modelBuilder);

        // DisplayName is user-controlled content rendered on other users' boards in Slice 2b.
        // The column is capped here; the write-time validation and escaping arrive with
        // registration in Plan 3, which is the first thing that can write it.
        modelBuilder.Entity<WendUser>().Property(u => u.DisplayName).HasMaxLength(100);

        // Every board belongs to one user; deleting the user erases their boards and everything
        // beneath them via the existing required FKs.
        modelBuilder.Entity<Board>()
            .HasOne<WendUser>()
            .WithMany()
            .HasForeignKey(b => b.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Hide soft-deleted / archived cards from every query. Plans 6-7 set these timestamps;
        // until then the filter is inert (no card ever has them set).
        modelBuilder.Entity<Card>().HasQueryFilter(c => c.DeletedAt == null && c.ArchivedAt == null);

        // Join table: composite key + two required FKs. Each principal (card, label) cascades
        // its join rows on delete (EF default for required relationships).
        modelBuilder.Entity<CardLabel>().HasKey(cl => new { cl.CardId, cl.LabelId });
        modelBuilder.Entity<CardLabel>().HasOne<Card>().WithMany().HasForeignKey(cl => cl.CardId);
        modelBuilder.Entity<CardLabel>().HasOne<Label>().WithMany().HasForeignKey(cl => cl.LabelId);

        // Hide soft-deleted checklist items from every query — mirrors the Card filter. Items
        // carry their own filter so the required relationship to the filtered Card doesn't warn.
        modelBuilder.Entity<ChecklistItem>().HasQueryFilter(i => i.DeletedAt == null);
    }
}
