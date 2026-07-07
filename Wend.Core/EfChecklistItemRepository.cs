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
}
