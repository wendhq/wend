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

    public async Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate)
    {
        var card = await db.Cards.FindAsync(id);
        if (card is null) return false;
        card.Title = title;
        card.Description = description;
        card.DueDate = dueDate;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteCardAsync(int id)
    {
        var card = await db.Cards.FindAsync(id);
        if (card is null) return false;
        db.Cards.Remove(card);
        await db.SaveChangesAsync();
        await ResequenceAsync(card.ListId); // keep the survivors gapless (0,1,2,…)
        return true;
    }

    public async Task<CardMoveResult> MoveCardAsync(int id, int targetListId, int position)
    {
        var card = await db.Cards.FindAsync(id);
        if (card is null) return CardMoveResult.NotFound;

        var targetList = await db.Lists.FindAsync(targetListId);
        var sourceList = await db.Lists.FindAsync(card.ListId);
        if (targetList is null || sourceList is null) return CardMoveResult.NotFound;
        if (targetList.BoardId != sourceList.BoardId) return CardMoveResult.CrossBoard;

        if (targetListId == card.ListId)
        {
            // Reorder within the list: lift out of the ordered cards, clamp, re-insert, renumber.
            var cards = await db.Cards.Where(c => c.ListId == card.ListId)
                .OrderBy(c => c.Position)
                .ToListAsync();
            cards.Remove(cards.First(c => c.Id == id));
            var index = Math.Clamp(position, 0, cards.Count);
            cards.Insert(index, card);
            for (var i = 0; i < cards.Count; i++) cards[i].Position = i;
            await db.SaveChangesAsync();
            return CardMoveResult.Moved;
        }

        // Move to another list: re-home the card, insert into the target at the clamped
        // position, renumber the target, then close the gap left behind in the source.
        var sourceListId = card.ListId;
        var targetCards = await db.Cards.Where(c => c.ListId == targetListId)
            .OrderBy(c => c.Position)
            .ToListAsync();
        card.ListId = targetListId;
        var pos = Math.Clamp(position, 0, targetCards.Count);
        targetCards.Insert(pos, card);
        for (var i = 0; i < targetCards.Count; i++) targetCards[i].Position = i;
        await db.SaveChangesAsync();
        await ResequenceAsync(sourceListId);
        return CardMoveResult.Moved;
    }

    public async Task<bool> SetCardCompletedAsync(int id, bool completed)
    {
        var card = await db.Cards.FindAsync(id);
        if (card is null) return false;
        card.CompletedAt = completed ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        return true;
    }

    // Rewrites a list's card positions to a gapless 0-based sequence in current order.
    private async Task ResequenceAsync(int listId)
    {
        var cards = await db.Cards.Where(c => c.ListId == listId)
            .OrderBy(c => c.Position)
            .ToListAsync();
        for (var i = 0; i < cards.Count; i++) cards[i].Position = i;
        await db.SaveChangesAsync();
    }
}
