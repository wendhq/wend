using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfLabelRepository(WendDbContext db) : ILabelRepository
{
    // The single place label ownership is expressed: a label belongs to whoever owns its board.
    private IQueryable<Label> Owned(string ownerId) => db.Labels.Where(l => l.Board.OwnerId == ownerId);

    // Cards are reached through their list's board — the same path EfCardRepository uses.
    private IQueryable<Card> OwnedCards(string ownerId) =>
        db.Cards.Where(c => c.List.Board.OwnerId == ownerId);

    public async Task<IReadOnlyList<Label>> GetBoardLabelsAsync(int boardId, string ownerId) =>
        await Owned(ownerId).Where(l => l.BoardId == boardId).OrderBy(l => l.Id).ToListAsync();

    public async Task<Label?> GetLabelAsync(int id, string ownerId) =>
        await Owned(ownerId).FirstOrDefaultAsync(l => l.Id == id);

    public async Task<Label> CreateLabelAsync(int boardId, string name, string colour, string ownerId)
    {
        // Defence in depth. The endpoint 404s on someone else's board before reaching here, but
        // unlike lists and cards there is no position count to scope, so ownership is asserted
        // explicitly rather than leaving ownerId unused. Reaching the throw means a caller skipped
        // the board check — a programming error, not a user-reachable state.
        var ownsBoard = await db.Boards.AnyAsync(b => b.Id == boardId && b.OwnerId == ownerId);
        if (!ownsBoard)
            throw new InvalidOperationException($"Board {boardId} does not belong to this owner.");

        var label = new Label { BoardId = boardId, Name = name, Colour = colour };
        db.Labels.Add(label);
        await db.SaveChangesAsync();
        return label;
    }

    public async Task<bool> EditLabelAsync(int id, string name, string colour, string ownerId)
    {
        var label = await Owned(ownerId).FirstOrDefaultAsync(l => l.Id == id);
        if (label is null) return false;
        label.Name = name;
        label.Colour = colour;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteLabelAsync(int id, string ownerId)
    {
        var label = await Owned(ownerId).FirstOrDefaultAsync(l => l.Id == id);
        if (label is null) return false;
        db.Labels.Remove(label);
        await db.SaveChangesAsync(); // CardLabel rows cascade at the DB level
        return true;
    }

    public async Task AttachAsync(int cardId, int labelId, string ownerId)
    {
        // Both ends must belong to this owner. A foreign card or label is simply not there, so
        // this becomes the same silent no-op as "already attached" — no leak, no exception.
        var ownsBoth = await OwnedCards(ownerId).AnyAsync(c => c.Id == cardId)
                       && await Owned(ownerId).AnyAsync(l => l.Id == labelId);
        if (!ownsBoth) return;

        var exists = await db.CardLabels.AnyAsync(cl => cl.CardId == cardId && cl.LabelId == labelId);
        if (exists) return; // idempotent — already attached
        db.CardLabels.Add(new CardLabel { CardId = cardId, LabelId = labelId });
        await db.SaveChangesAsync();
    }

    public async Task DetachAsync(int cardId, int labelId, string ownerId)
    {
        var ownsLabel = await Owned(ownerId).AnyAsync(l => l.Id == labelId);
        if (!ownsLabel) return; // not this owner's label — nothing to remove, same as unattached

        var row = await db.CardLabels.FirstOrDefaultAsync(cl => cl.CardId == cardId && cl.LabelId == labelId);
        if (row is null) return; // idempotent — nothing to remove
        db.CardLabels.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId, string ownerId) =>
        // Labels are board-scoped, so filtering the label side is sufficient to scope the join.
        await (from cl in db.CardLabels
            where cl.CardId == cardId
            join l in Owned(ownerId) on cl.LabelId equals l.Id
            orderby l.Id
            select l).ToListAsync();

    public async Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId, string ownerId)
    {
        // All (cardId, labelId) pairs for visible cards on this board, grouped per card.
        var pairs = await (
            from cl in db.CardLabels
            join card in db.Cards on cl.CardId equals card.Id
            join list in db.Lists on card.ListId equals list.Id
            where list.BoardId == boardId && list.Board.OwnerId == ownerId
            select new { cl.CardId, cl.LabelId }).ToListAsync();

        return pairs.GroupBy(p => p.CardId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.LabelId).ToList());
    }
}
