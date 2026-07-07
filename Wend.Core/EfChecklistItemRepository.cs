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
}
