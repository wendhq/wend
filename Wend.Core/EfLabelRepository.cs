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

    // Attach / detach / reads arrive in Task 3.
    public Task AttachAsync(int cardId, int labelId) => throw new NotImplementedException();
    public Task DetachAsync(int cardId, int labelId) => throw new NotImplementedException();
    public Task<IReadOnlyList<Label>> GetCardLabelsAsync(int cardId) => throw new NotImplementedException();
    public Task<Dictionary<int, List<int>>> GetLabelIdsByCardAsync(int boardId) => throw new NotImplementedException();
}
