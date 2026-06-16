namespace Wend.Core;

/// <summary>
/// Persistence seam for the board domain. Slice 1 implements this with EF Core → SQLite;
/// the API depends only on this interface, so storage can be swapped without touching board logic.
/// </summary>
public interface IBoardRepository
{
    Task<IReadOnlyList<Board>> GetBoardsAsync();
    Task<Board?> GetBoardAsync(int id);
    Task<Board> CreateBoardAsync(string title);
    Task<bool> RenameBoardAsync(int id, string newTitle);
    Task<bool> DeleteBoardAsync(int id);
}
