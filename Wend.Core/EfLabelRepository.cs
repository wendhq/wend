using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfLabelRepository(WendDbContext db) : ILabelRepository
{
    public async Task<IReadOnlyList<Label>> GetBoardLabelsAsync(int boardId) =>
        await db.Labels.Where(l => l.BoardId == boardId).OrderBy(l => l.Id).ToListAsync();

    public async Task<Label?> GetLabelAsync(int id) => await db.Labels.FindAsync(id);

    public async Task<Label> CreateLabelAsync(int boardId, string name, string colour)
    {
        var label = new Label { BoardId = boardId, Name = name, Colour = colour };
        db.Labels.Add(label);
        await db.SaveChangesAsync();
        return label;
    }

    public async Task<bool> EditLabelAsync(int id, string name, string colour)
    {
        var label = await db.Labels.FindAsync(id);
        if (label is null) return false;
        label.Name = name;
        label.Colour = colour;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteLabelAsync(int id)
    {
        var label = await db.Labels.FindAsync(id);
        if (label is null) return false;
        db.Labels.Remove(label);
        await db.SaveChangesAsync(); // CardLabel rows cascade at the DB level
        return true;
    }

    public async Task AttachAsync(int cardId, int labelId)
    {
        var exists = await db.CardLabels.AnyAsync(cl => cl.CardId == cardId && cl.LabelId == labelId);
        if (exists) return; // idempotent — already attached
        db.CardLabels.Add(new CardLabel { CardId = cardId, LabelId = labelId });
        await db.SaveChangesAsync();
    }

    public async Task DetachAsync(int cardId, int labelId)
    {
        var row = await db.CardLabels.FirstOrDefaultAsync(cl => cl.CardId == cardId && cl.LabelId == labelId);
        if (row is null) return; // idempotent — nothing to remove
        db.CardLabels.Remove(row);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId) =>
        await (from cl in db.CardLabels
            where cl.CardId == cardId
            join l in db.Labels on cl.LabelId equals l.Id
            orderby l.Id
            select l).ToListAsync();

    public async Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId)
    {
        // All (cardId, labelId) pairs for visible cards on this board, grouped per card.
        var pairs = await (
            from cl in db.CardLabels
            join card in db.Cards on cl.CardId equals card.Id
            join list in db.Lists on card.ListId equals list.Id
            where list.BoardId == boardId
            select new { cl.CardId, cl.LabelId }).ToListAsync();

        return pairs.GroupBy(p => p.CardId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.LabelId).ToList());
    }
}
