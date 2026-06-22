namespace Wend.Core;

/// <summary>
/// Persistence seam for lists within a board. Position is a 0-based contiguous index;
/// the repository keeps it gapless on create, delete and move.
/// </summary>
public interface IListRepository
{
    Task<IReadOnlyList<List>> GetListsForBoardAsync(int boardId);
    Task<List> CreateListAsync(int boardId, string title);
    Task<List?> GetListAsync(int id);
    Task<bool> RenameListAsync(int id, string newTitle);
    Task<bool> DeleteListAsync(int id);
    Task<bool> MoveListAsync(int id, int position);
}
