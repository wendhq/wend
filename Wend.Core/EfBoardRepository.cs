using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfBoardRepository(WendDbContext db) : IBoardRepository
{
    // The single place board ownership is expressed. Every query starts here, so a board belonging
    // to another user is simply absent — which is what makes the API answer 404 rather than 403.
    private IQueryable<Board> Owned(string ownerId) => db.Boards.Where(b => b.OwnerId == ownerId);

    public async Task<IReadOnlyList<Board>> GetBoardsAsync(string ownerId) =>
        await Owned(ownerId).OrderBy(b => b.Id).ToListAsync();

    public async Task<Board> CreateBoardAsync(string title, string ownerId)
    {
        var board = new Board { Title = title, OwnerId = ownerId };
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        return board;
    }

    // FindAsync cannot carry a predicate, so ownership forces FirstOrDefaultAsync here and below.
    public async Task<Board?> GetBoardAsync(int id, string ownerId) =>
        await Owned(ownerId).FirstOrDefaultAsync(b => b.Id == id);

    public async Task<bool> RenameBoardAsync(int id, string newTitle, string ownerId)
    {
        var board = await Owned(ownerId).FirstOrDefaultAsync(b => b.Id == id);
        if (board is null) return false;
        board.Title = newTitle;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteBoardAsync(int id, string ownerId)
    {
        var board = await Owned(ownerId).FirstOrDefaultAsync(b => b.Id == id);
        if (board is null) return false;
        db.Boards.Remove(board);
        await db.SaveChangesAsync();
        return true;
    }
}
