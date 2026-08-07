namespace Wend.Core;

/// <summary>
/// Persistence seam for board-scoped labels and the card↔label join.
///
/// Every method takes an explicit ownerId — a label belongs to whoever owns its board, and
/// another user's label reads as missing rather than forbidden.
/// </summary>
public interface ILabelRepository
{
    Task<IReadOnlyList<Label>> GetBoardLabelsAsync(int boardId, string ownerId);
    Task<Label?> GetLabelAsync(int id, string ownerId);
    Task<Label> CreateLabelAsync(int boardId, string name, string colour, string ownerId);
    Task<bool> EditLabelAsync(int id, string name, string colour, string ownerId);
    Task<bool> DeleteLabelAsync(int id, string ownerId);

    // Card ↔ label. Both ends are owner-checked; a foreign card or label is a silent no-op.
    Task AttachAsync(int cardId, int labelId, string ownerId);
    Task DetachAsync(int cardId, int labelId, string ownerId);
    Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId, string ownerId);
    Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId, string ownerId);
}
