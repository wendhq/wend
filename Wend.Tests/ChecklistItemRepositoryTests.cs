using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class ChecklistItemRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfBoardRepository _boards = null!;
    private EfListRepository _lists = null!;
    private EfCardRepository _cards = null!;

    [SetUp]
    public void SetUp()
    {
        // In-memory SQLite lives only as long as the connection is open.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WendDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new WendDbContext(options);
        _db.Database.EnsureCreated();
        _boards = new EfBoardRepository(_db);
        _lists = new EfListRepository(_db);
        _cards = new EfCardRepository(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // Adds a board + list + card directly, returning the card id, so item tests have a parent.
    private async Task<int> NewCardAsync()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        return card.Id;
    }

    [Test]
    public async Task Saved_item_belongs_to_its_card_and_keeps_its_position()
    {
        var cardId = await NewCardAsync();

        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Write intro", Position = 0 });
        await _db.SaveChangesAsync();

        var item = await _db.ChecklistItems.SingleAsync();
        Assert.That(item.Id, Is.GreaterThan(0));
        Assert.That(item.CardId, Is.EqualTo(cardId));
        Assert.That(item.Text, Is.EqualTo("Write intro"));
        Assert.That(item.Position, Is.EqualTo(0));
        Assert.That(item.CheckedAt, Is.Null);
    }

    [Test]
    public async Task Deleting_a_card_row_cascades_to_its_items()
    {
        var cardId = await NewCardAsync();
        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Item", Position = 0 });
        await _db.SaveChangesAsync();

        var card = await _db.Cards.SingleAsync(c => c.Id == cardId);
        _db.Cards.Remove(card); // hard delete (the future Trash empty) — DB-level cascade
        await _db.SaveChangesAsync();

        Assert.That(await _db.ChecklistItems.IgnoreQueryFilters().AnyAsync(), Is.False);
    }

    [Test]
    public async Task Deleted_items_are_hidden_from_queries()
    {
        var cardId = await NewCardAsync();
        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Visible", Position = 0 });
        _db.ChecklistItems.Add(new ChecklistItem { CardId = cardId, Text = "Gone", Position = 1, DeletedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var texts = await _db.ChecklistItems.Select(i => i.Text).ToListAsync();
        Assert.That(texts, Is.EqualTo(new[] { "Visible" }));
    }
}
