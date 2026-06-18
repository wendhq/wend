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

    public async Task<Board?> GetBoardAsync(int id) =>
        await db.Boards.FindAsync(id);

    public async Task<bool> RenameBoardAsync(int id, string newTitle)
    {
        var board = await db.Boards.FindAsync(id);
        if (board is null) return false;
        board.Title = newTitle;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteBoardAsync(int id)
    {
        var board = await db.Boards.FindAsync(id);
        if (board is null) return false;
        db.Boards.Remove(board);
        await db.SaveChangesAsync();
        return true;
    }
}