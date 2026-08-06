using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfListRepository(WendDbContext db) : IListRepository
{
    // The single place list ownership is expressed: a list belongs to whoever owns its board.
    private IQueryable<List> Owned(string ownerId) => db.Lists.Where(l => l.Board.OwnerId == ownerId);

    public async Task<List?> GetListAsync(int id, string ownerId) =>
        await Owned(ownerId).FirstOrDefaultAsync(l => l.Id == id);

    public async Task<IReadOnlyList<List>> GetListsForBoardAsync(int boardId, string ownerId) =>
        await Owned(ownerId).Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Position)
            .ToListAsync();

    public async Task<List> CreateListAsync(int boardId, string title, string ownerId)
    {
        // Callers reach this only after the board itself has been ownership-checked (the endpoint
        // 404s on someone else's board first), so this scopes the position count rather than
        // re-authorising. Append: the next position is the current count for this board.
        var position = await Owned(ownerId).CountAsync(l => l.BoardId == boardId);
        var list = new List { BoardId = boardId, Title = title, Position = position };
        db.Lists.Add(list);
        await db.SaveChangesAsync();
        return list;
    }

    public async Task<bool> RenameListAsync(int id, string newTitle, string ownerId)
    {
        var list = await Owned(ownerId).FirstOrDefaultAsync(l => l.Id == id);
        if (list is null) return false;
        list.Title = newTitle;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteListAsync(int id, string ownerId)
    {
        var list = await Owned(ownerId).FirstOrDefaultAsync(l => l.Id == id);
        if (list is null) return false;
        db.Lists.Remove(list);
        await db.SaveChangesAsync();
        await ResequenceAsync(list.BoardId); // keep the survivors gapless (0,1,2,…)
        return true;
    }

    // Rewrites a board's list positions to a gapless 0-based sequence in current order.
    // No ownerId: only reached after an owner-scoped lookup has already succeeded.
    private async Task ResequenceAsync(int boardId)
    {
        var lists = await db.Lists.Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Position)
            .ToListAsync();
        for (var i = 0; i < lists.Count; i++) lists[i].Position = i;
        await db.SaveChangesAsync();
    }

    public async Task<bool> MoveListAsync(int id, int position, string ownerId)
    {
        var list = await Owned(ownerId).FirstOrDefaultAsync(l => l.Id == id);
        if (list is null) return false;

        // Pull the board's lists in order, lift this one out, drop it back at the
        // clamped target index, then renumber so positions stay gapless.
        var siblings = await db.Lists.Where(l => l.BoardId == list.BoardId)
            .OrderBy(l => l.Position)
            .ToListAsync();
        siblings.Remove(siblings.First(l => l.Id == id));
        var target = Math.Clamp(position, 0, siblings.Count);
        siblings.Insert(target, list);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }
}
