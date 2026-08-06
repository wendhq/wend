namespace Wend.Core;

/// <summary>
/// Persistence seam for the board domain. Slice 1 implements this with EF Core → PostgreSQL;
/// the API depends only on this interface, so storage can be swapped without touching board logic.
///
/// Every method takes an explicit ownerId: ownership is part of the contract, so a method added
/// later cannot silently skip it, and non-owner callers (Slice 2b sharing) can pass a different id.
/// </summary>
public interface IBoardRepository
{
    Task<IReadOnlyList<Board>> GetBoardsAsync(string ownerId);
    Task<Board?> GetBoardAsync(int id, string ownerId);
    Task<Board> CreateBoardAsync(string title, string ownerId);
    Task<bool> RenameBoardAsync(int id, string newTitle, string ownerId);
    Task<bool> DeleteBoardAsync(int id, string ownerId);
}
