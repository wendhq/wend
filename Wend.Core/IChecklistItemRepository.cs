namespace Wend.Core;

/// <summary>
/// Persistence seam for a card's checklist items. Position is a 0-based contiguous index
/// shared by checked AND unchecked items; the repository keeps it gapless on create,
/// delete, and move — the same algebra as cards within a list.
/// </summary>
public interface IChecklistItemRepository
{
    Task<IReadOnlyList<ChecklistItem>> GetItemsForCardAsync(int cardId, string ownerId);
    Task<ChecklistItem> AddItemAsync(int cardId, string text, string ownerId);
    Task<bool> RenameItemAsync(int id, string text, string ownerId);
    Task<bool> SetCheckedAsync(int id, bool isChecked, string ownerId);
    Task<bool> MoveItemAsync(int id, int position, string ownerId);
    Task<bool> DeleteItemAsync(int id, string ownerId);
    Task<bool> RestoreItemAsync(int id, string ownerId);
    Task<IReadOnlyDictionary<int, ChecklistCounts>> GetCountsByCardAsync(int boardId, string ownerId);
}

public record ChecklistCounts(int Done, int Total);
