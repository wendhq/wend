namespace Wend.Core;

/// <summary>
/// Persistence seam for a card's checklist items. Position is a 0-based contiguous index
/// shared by checked AND unchecked items; the repository keeps it gapless on create,
/// delete, and move — the same algebra as cards within a list.
/// </summary>
public interface IChecklistItemRepository
{
    Task<IReadOnlyList<ChecklistItem>> GetItemsForCardAsync(int cardId);
    Task<ChecklistItem> AddItemAsync(int cardId, string text);
    Task<bool> RenameItemAsync(int id, string text);
    Task<bool> SetCheckedAsync(int id, bool isChecked);
    Task<bool> MoveItemAsync(int id, int position);
    Task<bool> DeleteItemAsync(int id);
    Task<bool> RestoreItemAsync(int id);
    Task<IReadOnlyDictionary<int, ChecklistCounts>> GetCountsByCardAsync(int boardId);
}

public record ChecklistCounts(int Done, int Total);
