using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfChecklistItemRepository(WendDbContext db) : IChecklistItemRepository
{
    public async Task<IReadOnlyList<ChecklistItem>> GetItemsForCardAsync(int cardId) =>
        await db.ChecklistItems.Where(i => i.CardId == cardId)
            .OrderBy(i => i.Position)
            .ToListAsync();

    public async Task<ChecklistItem> AddItemAsync(int cardId, string text)
    {
        // Append: the next position is the current item count for this card.
        var position = await db.ChecklistItems.CountAsync(i => i.CardId == cardId);
        var item = new ChecklistItem { CardId = cardId, Text = text, Position = position };
        db.ChecklistItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<bool> RenameItemAsync(int id, string text)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null) return false;
        item.Text = text;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetCheckedAsync(int id, bool isChecked)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null) return false;
        item.CheckedAt = isChecked ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MoveItemAsync(int id, int position)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null) return false;

        // Pull the card's items in order, lift this one out, drop it back at the clamped
        // target index, then renumber so positions stay gapless — MoveListAsync's algorithm.
        var siblings = await db.ChecklistItems.Where(i => i.CardId == item.CardId)
            .OrderBy(i => i.Position)
            .ToListAsync();
        siblings.Remove(siblings.First(i => i.Id == id));
        var target = Math.Clamp(position, 0, siblings.Count);
        siblings.Insert(target, item);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteItemAsync(int id)
    {
        var item = await db.ChecklistItems.FindAsync(id);
        if (item is null || item.DeletedAt is not null) return false; // missing or already gone
        item.DeletedAt = DateTime.UtcNow;   // soft delete — the row survives for undo
        await db.SaveChangesAsync();
        await ResequenceAsync(item.CardId); // close the gap among the survivors (filter hides this item)
        return true;
    }

    public async Task<bool> RestoreItemAsync(int id)
    {
        // IgnoreQueryFilters so the soft-deleted row is found from ANY context. FindAsync only
        // returns it while it's still tracked in the same context — the API's per-request
        // contexts read from the DB, where the filter hides it (Plan 7's restore-404 bug).
        var item = await db.ChecklistItems.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;
        if (item.DeletedAt is null) return true;   // already active — idempotent no-op

        var siblings = await db.ChecklistItems.Where(i => i.CardId == item.CardId)
            .OrderBy(i => i.Position)
            .ToListAsync();                        // active siblings only (the item is still filtered out)
        item.DeletedAt = null;
        var index = Math.Clamp(item.Position, 0, siblings.Count); // its old spot, bounded to the list today
        siblings.Insert(index, item);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyDictionary<int, ChecklistCounts>> GetCountsByCardAsync(int boardId)
    {
        var rows = await (
            from i in db.ChecklistItems
            join c in db.Cards on i.CardId equals c.Id
            join l in db.Lists on c.ListId equals l.Id
            where l.BoardId == boardId
            group i by i.CardId into g
            select new { CardId = g.Key, Done = g.Count(x => x.CheckedAt != null), Total = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.CardId, r => new ChecklistCounts(r.Done, r.Total));
    }

    // Rewrites a card's item positions to a gapless 0-based sequence in current order.
    private async Task ResequenceAsync(int cardId)
    {
        var items = await db.ChecklistItems.Where(i => i.CardId == cardId)
            .OrderBy(i => i.Position)
            .ToListAsync();
        for (var i = 0; i < items.Count; i++) items[i].Position = i;
        await db.SaveChangesAsync();
    }
}
