using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfChecklistItemRepository(WendDbContext db) : IChecklistItemRepository
{
    // The deepest ownership traversal in the codebase: item → card → list → board → owner.
    //
    // Ownership must not depend on the card's *delete* state. The obvious spelling,
    // `db.ChecklistItems.Where(i => i.Card.List.Board.OwnerId == ownerId)`, does: EF applies
    // Card's soft-delete query filter to the traversed navigation, so a soft-deleted card's items
    // disappear — silently breaking card undo, which restores the card AND its checklist.
    // Measured, not assumed: that spelling returned 0 items where 1 survives.
    //
    // IgnoreQueryFilters is query-wide in EF — it cannot drop Card's filter alone — so it also
    // switches off ChecklistItem's. That is why the item's own soft-delete rule is re-stated
    // explicitly in Owned() below rather than left to the global filter.
    // Guarded by Items_survive_their_cards_soft_delete.
    private IQueryable<ChecklistItem> OwnedIncludingDeleted(string ownerId) =>
        db.ChecklistItems.IgnoreQueryFilters()
            .Where(i => db.Cards.Any(c => c.Id == i.CardId && c.List.Board.OwnerId == ownerId));

    // Live items on boards this user owns — the normal path. The DeletedAt test restores what the
    // global filter would have done; dropping it resurrects soft-deleted items.
    // Guarded by Delete_soft_deletes_and_resequences_the_rest.
    private IQueryable<ChecklistItem> Owned(string ownerId) =>
        OwnedIncludingDeleted(ownerId).Where(i => i.DeletedAt == null);

    public async Task<IReadOnlyList<ChecklistItem>> GetItemsForCardAsync(int cardId, string ownerId) =>
        await Owned(ownerId).Where(i => i.CardId == cardId)
            .OrderBy(i => i.Position)
            .ToListAsync();

    public async Task<ChecklistItem> AddItemAsync(int cardId, string text, string ownerId)
    {
        // Append: the next position is the current item count for this card.
        var position = await Owned(ownerId).CountAsync(i => i.CardId == cardId);
        var item = new ChecklistItem { CardId = cardId, Text = text, Position = position };
        db.ChecklistItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    // FindAsync cannot carry a predicate, so ownership forces FirstOrDefaultAsync here and below.
    public async Task<bool> RenameItemAsync(int id, string text, string ownerId)
    {
        var item = await Owned(ownerId).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;
        item.Text = text;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetCheckedAsync(int id, bool isChecked, string ownerId)
    {
        var item = await Owned(ownerId).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;
        item.CheckedAt = isChecked ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MoveItemAsync(int id, int position, string ownerId)
    {
        var item = await Owned(ownerId).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;

        // Pull the card's items in order, lift this one out, drop it back at the clamped
        // target index, then renumber so positions stay gapless — MoveListAsync's algorithm.
        var siblings = await Owned(ownerId).Where(i => i.CardId == item.CardId)
            .OrderBy(i => i.Position)
            .ToListAsync();
        siblings.Remove(siblings.First(i => i.Id == id));
        var target = Math.Clamp(position, 0, siblings.Count);
        siblings.Insert(target, item);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteItemAsync(int id, string ownerId)
    {
        var item = await Owned(ownerId).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null || item.DeletedAt is not null) return false; // missing or already gone
        item.DeletedAt = DateTime.UtcNow;   // soft delete — the row survives for undo
        await db.SaveChangesAsync();
        await ResequenceAsync(item.CardId, ownerId); // close the gap among the survivors (filter hides this item)
        return true;
    }

    public async Task<bool> RestoreItemAsync(int id, string ownerId)
    {
        // Deleted items are in scope here so the soft-deleted row is found from ANY context.
        // FindAsync only returns it while it's still tracked in the same context — the API's
        // per-request contexts read from the DB, where the filter hides it (Plan 7's restore-404
        // bug). Ownership lives in the Where clause, not a query filter, so it is NOT dropped —
        // that is the whole reason ownership was not implemented as a global query filter.
        var item = await OwnedIncludingDeleted(ownerId).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;
        if (item.DeletedAt is null) return true;   // already active — idempotent no-op

        var siblings = await Owned(ownerId).Where(i => i.CardId == item.CardId)
            .OrderBy(i => i.Position)
            .ToListAsync();                        // active siblings only (the item is still filtered out)
        item.DeletedAt = null;
        var index = Math.Clamp(item.Position, 0, siblings.Count); // its old spot, bounded to the list today
        siblings.Insert(index, item);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyDictionary<int, ChecklistCounts>> GetCountsByCardAsync(int boardId, string ownerId)
    {
        var rows = await (
            from i in Owned(ownerId)
            join c in db.Cards on i.CardId equals c.Id
            join l in db.Lists on c.ListId equals l.Id
            where l.BoardId == boardId
            group i by i.CardId into g
            select new { CardId = g.Key, Done = g.Count(x => x.CheckedAt != null), Total = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.CardId, r => new ChecklistCounts(r.Done, r.Total));
    }

    // Rewrites a card's item positions to a gapless 0-based sequence in current order.
    private async Task ResequenceAsync(int cardId, string ownerId)
    {
        var items = await Owned(ownerId).Where(i => i.CardId == cardId)
            .OrderBy(i => i.Position)
            .ToListAsync();
        for (var i = 0; i < items.Count; i++) items[i].Position = i;
        await db.SaveChangesAsync();
    }
}
