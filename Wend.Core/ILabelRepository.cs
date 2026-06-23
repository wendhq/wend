namespace Wend.Core;

/// <summary>Persistence seam for board-scoped labels and the card↔label join.</summary>
public interface ILabelRepository
{
    Task<IReadOnlyList<Label>> GetBoardLabelsAsync(int boardId);
    Task<Label?> GetLabelAsync(int id);
    Task<Label> CreateLabelAsync(int boardId, string name, string colour);
    Task<bool> EditLabelAsync(int id, string name, string colour);
    Task<bool> DeleteLabelAsync(int id);

    // Card ↔ label (Task 3).
    Task AttachAsync(int cardId, int labelId);
    Task DetachAsync(int cardId, int labelId);
    Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId);
    Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId);
}
