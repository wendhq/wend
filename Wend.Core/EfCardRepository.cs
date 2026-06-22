using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfCardRepository(WendDbContext db) : ICardRepository
{
    public async Task<IReadOnlyList<Card>> GetCardsForListAsync(int listId) =>
        await db.Cards.Where(c => c.ListId == listId)
            .OrderBy(c => c.Position)
            .ToListAsync();

    public async Task<Card?> GetCardAsync(int id) => await db.Cards.FindAsync(id);

    public async Task<Card> CreateCardAsync(int listId, string title)
    {
        // Append: the next position is the current card count for this list.
        var position = await db.Cards.CountAsync(c => c.ListId == listId);
        var card = new Card
        {
            ListId = listId,
            Title = title,
            Position = position,
            CreatedAt = DateTime.UtcNow,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card;
    }

    // Edit / Delete arrive in Task 3.
    public Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate) =>
        throw new NotImplementedException();
    public Task<bool> DeleteCardAsync(int id) => throw new NotImplementedException();
}
