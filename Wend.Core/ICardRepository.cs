namespace Wend.Core;

/// <summary>
/// Persistence seam for cards within a list. Position is a 0-based contiguous index; the
/// repository keeps it gapless on create, delete, and move.
/// </summary>
public interface ICardRepository
{
    Task<IReadOnlyList<Card>> GetCardsForListAsync(int listId);
    Task<Card?> GetCardAsync(int id);
    Task<Card> CreateCardAsync(int listId, string title);
    Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate);
    Task<bool> DeleteCardAsync(int id);
    Task<CardMoveResult> MoveCardAsync(int id, int targetListId, int position);
}
