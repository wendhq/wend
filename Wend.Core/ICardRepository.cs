namespace Wend.Core;

/// <summary>
/// Persistence seam for cards within a list. Position is a 0-based contiguous index; the
/// repository keeps it gapless on create, delete, and move.
///
/// Every method takes an explicit ownerId — a card belongs to whoever owns the board its list
/// sits on, and another user's card reads as missing rather than forbidden.
/// </summary>
public interface ICardRepository
{
    Task<IReadOnlyList<Card>> GetCardsForListAsync(int listId, string ownerId);
    Task<Card?> GetCardAsync(int id, string ownerId);
    Task<Card> CreateCardAsync(int listId, string title, string ownerId);
    Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate, string ownerId);
    Task<bool> DeleteCardAsync(int id, string ownerId);
    Task<bool> RestoreCardAsync(int id, string ownerId);
    Task<CardMoveResult> MoveCardAsync(int id, int targetListId, int position, string ownerId);
    Task<bool> SetCardCompletedAsync(int id, bool completed, string ownerId);
}
