using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfListRepository(WendDbContext db) : IListRepository
{
    public async Task<IReadOnlyList<List>> GetListsForBoardAsync(int boardId) =>
        await db.Lists.Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Position)
            .ToListAsync();

    public async Task<List> CreateListAsync(int boardId, string title)
    {
        // Append: the next position is the current count for this board.
        var position = await db.Lists.CountAsync(l => l.BoardId == boardId);
        var list = new List { BoardId = boardId, Title = title, Position = position };
        db.Lists.Add(list);
        await db.SaveChangesAsync();
        return list;
    }

    // Rename / Delete / Move arrive in Tasks 3-4 (stubbed so the interface compiles).
    public Task<bool> RenameListAsync(int id, string newTitle) => throw new NotImplementedException();
    public Task<bool> DeleteListAsync(int id) => throw new NotImplementedException();
    public Task<bool> MoveListAsync(int id, int position) => throw new NotImplementedException();
}
