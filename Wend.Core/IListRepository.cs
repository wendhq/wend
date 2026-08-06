namespace Wend.Core;

/// <summary>
/// Persistence seam for lists within a board. Position is a 0-based contiguous index;
/// the repository keeps it gapless on create, delete and move.
///
/// Every method takes an explicit ownerId — a list belongs to whoever owns its board, and a list
/// on someone else's board reads as missing rather than forbidden.
/// </summary>
public interface IListRepository
{
    Task<IReadOnlyList<List>> GetListsForBoardAsync(int boardId, string ownerId);
    Task<List> CreateListAsync(int boardId, string title, string ownerId);
    Task<List?> GetListAsync(int id, string ownerId);
    Task<bool> RenameListAsync(int id, string newTitle, string ownerId);
    Task<bool> DeleteListAsync(int id, string ownerId);
    Task<bool> MoveListAsync(int id, int position, string ownerId);
}
