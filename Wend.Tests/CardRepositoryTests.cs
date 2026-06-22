using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class CardRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;

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
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // Adds a board + one list directly, returning the list id, so card tests have a parent.
    private async Task<int> SeedListAsync()
    {
        var board = new Board { Title = "Board" };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();
        var list = new List { BoardId = board.Id, Title = "List", Position = 0 };
        _db.Lists.Add(list);
        await _db.SaveChangesAsync();
        return list.Id;
    }

    [Test]
    public async Task Saved_card_belongs_to_its_list_and_keeps_its_position()
    {
        var listId = await SeedListAsync();

        _db.Cards.Add(new Card { ListId = listId, Title = "Email Rebecka", Position = 0, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var card = await _db.Cards.SingleAsync();
        Assert.That(card.Id, Is.GreaterThan(0));
        Assert.That(card.ListId, Is.EqualTo(listId));
        Assert.That(card.Title, Is.EqualTo("Email Rebecka"));
        Assert.That(card.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Deleting_a_list_cascades_to_its_cards()
    {
        var listId = await SeedListAsync();
        _db.Cards.Add(new Card { ListId = listId, Title = "Card", Position = 0, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var list = await _db.Lists.SingleAsync(l => l.Id == listId);
        _db.Lists.Remove(list);
        await _db.SaveChangesAsync();

        Assert.That(await _db.Cards.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Deleted_or_archived_cards_are_hidden_from_queries()
    {
        var listId = await SeedListAsync();
        _db.Cards.Add(new Card { ListId = listId, Title = "Visible", Position = 0, CreatedAt = DateTime.UtcNow });
        _db.Cards.Add(new Card { ListId = listId, Title = "Gone", Position = 1, CreatedAt = DateTime.UtcNow, DeletedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var titles = await _db.Cards.Select(c => c.Title).ToListAsync();
        Assert.That(titles, Is.EqualTo(new[] { "Visible" }));
    }
}
