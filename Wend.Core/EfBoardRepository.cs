using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfBoardRepository(WendDbContext db) : IBoardRepository
{
    public async Task<IReadOnlyList<Board>> GetBoardsAsync() =>
        await db.Boards.OrderBy(b => b.Id).ToListAsync();

    public async Task<Board> CreateBoardAsync(string title)
    {
        var board = new Board { Title = title };
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        return board;
    }

    // GetBoardAsync / RenameBoardAsync / DeleteBoardAsync arrive in Task 3.
    public Task<Board?> GetBoardAsync(int id) => throw new NotImplementedException();
    public Task<bool> RenameBoardAsync(int id, string newTitle) => throw new NotImplementedException();
    public Task<bool> DeleteBoardAsync(int id) => throw new NotImplementedException();
}