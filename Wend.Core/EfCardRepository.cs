using Microsoft.EntityFrameworkCore;

namespace Wend.Core;

public class EfCardRepository(WendDbContext db) : ICardRepository
{
    // The single place card ownership is expressed: a card belongs to whoever owns the board its
    // list sits on. Ownership lives in this Where clause, NOT in a global query filter — which is
    // why IgnoreQueryFilters() in RestoreCardAsync cannot accidentally drop it.
    private IQueryable<Card> Owned(string ownerId) => db.Cards.Where(c => c.List.Board.OwnerId == ownerId);

    public async Task<IReadOnlyList<Card>> GetCardsForListAsync(int listId, string ownerId) =>
        await Owned(ownerId).Where(c => c.ListId == listId)
            .OrderBy(c => c.Position)
            .ToListAsync();

    public async Task<Card?> GetCardAsync(int id, string ownerId) =>
        await Owned(ownerId).FirstOrDefaultAsync(c => c.Id == id); // goes through the filter → deleted cards read as gone

    public async Task<Card> CreateCardAsync(int listId, string title, string ownerId)
    {
        // Defence in depth, matching CreateLabelAsync. The endpoint 404s on someone else's list
        // before reaching here, so the throw means a caller skipped that check — a programming
        // error, not a user-reachable state. Scoping the position count is not enough on its own:
        // a foreign list simply counts 0 and the card would be created in it. Lists carry no query
        // filter, so this traversal reaches every list its owner really has.
        var ownsList = await db.Lists.AnyAsync(l => l.Id == listId && l.Board.OwnerId == ownerId);
        if (!ownsList)
            throw new InvalidOperationException($"List {listId} does not belong to this owner.");

        // Append: the next position is the current card count for this list.
        var position = await Owned(ownerId).CountAsync(c => c.ListId == listId);
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

    public async Task<bool> EditCardAsync(int id, string title, string? description, DateOnly? dueDate,
        string ownerId)
    {
        var card = await Owned(ownerId).FirstOrDefaultAsync(c => c.Id == id);
        if (card is null) return false;
        card.Title = title;
        card.Description = description;
        card.DueDate = dueDate;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteCardAsync(int id, string ownerId)
    {
        var card = await Owned(ownerId).FirstOrDefaultAsync(c => c.Id == id);
        if (card is null || card.DeletedAt is not null) return false; // missing or already gone
        card.DeletedAt = DateTime.UtcNow;   // soft delete — the row survives for undo
        await db.SaveChangesAsync();
        await ResequenceAsync(card.ListId); // close the gap among the survivors (filter hides this card)
        return true;
    }

    public async Task<bool> RestoreCardAsync(int id, string ownerId)
    {
        // IgnoreQueryFilters so the soft-deleted row is found from ANY context. FindAsync only
        // returns it while it's still tracked in the same context — the API's per-request contexts
        // read from the DB, where the filter hides it (that was the restore 404 the repo tests missed).
        // Ownership survives this call because it is an explicit Where, not a query filter.
        var card = await Owned(ownerId).IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (card is null) return false;
        if (card.DeletedAt is null) return true;   // already active — idempotent no-op

        var siblings = await db.Cards.Where(c => c.ListId == card.ListId)
            .OrderBy(c => c.Position)
            .ToListAsync();                        // active siblings only (the card is still filtered out)
        card.DeletedAt = null;
        var index = Math.Clamp(card.Position, 0, siblings.Count); // its old spot, bounded to the list today
        siblings.Insert(index, card);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Position = i;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<CardMoveResult> MoveCardAsync(int id, int targetListId, int position, string ownerId)
    {
        var card = await Owned(ownerId).FirstOrDefaultAsync(c => c.Id == id);
        if (card is null) return CardMoveResult.NotFound;

        // Owner-scoped list lookups: another user's list resolves as MISSING, so the caller gets
        // NotFound rather than CrossBoard. A 400 here would confirm that their list exists on a
        // different board — the enumeration leak the 404-not-403 rule exists to prevent.
        var targetList = await db.Lists.FirstOrDefaultAsync(l => l.Id == targetListId && l.Board.OwnerId == ownerId);
        var sourceList = await db.Lists.FirstOrDefaultAsync(l => l.Id == card.ListId && l.Board.OwnerId == ownerId);
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

    public async Task<bool> SetCardCompletedAsync(int id, bool completed, string ownerId)
    {
        var card = await Owned(ownerId).FirstOrDefaultAsync(c => c.Id == id);
        if (card is null) return false;
        card.CompletedAt = completed ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        return true;
    }

    // Rewrites a list's card positions to a gapless 0-based sequence in current order.
    // No ownerId: only reached after an owner-scoped lookup has already succeeded.
    private async Task ResequenceAsync(int listId)
    {
        var cards = await db.Cards.Where(c => c.ListId == listId)
            .OrderBy(c => c.Position)
            .ToListAsync();
        for (var i = 0; i < cards.Count; i++) cards[i].Position = i;
        await db.SaveChangesAsync();
    }
}
