using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class CardRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfCardRepository _repo = null!;
    private EfBoardRepository _boards = null!;
    private EfListRepository _lists = null!;

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
        _repo = new EfCardRepository(_db);
        _boards = new EfBoardRepository(_db);
        _lists = new EfListRepository(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // Adds a board + one list directly, returning the list id, so card tests have a parent.
    private async Task<int> NewListAsync()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        return list.Id;
    }

    [Test]
    public async Task Create_appends_each_card_at_the_next_position()
    {
        var listId = await NewListAsync();

        var first = await _repo.CreateCardAsync(listId, "First");
        var second = await _repo.CreateCardAsync(listId, "Second");

        Assert.That(first.Position, Is.EqualTo(0));
        Assert.That(second.Position, Is.EqualTo(1));
        Assert.That(first.CreatedAt, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public async Task Get_cards_for_list_returns_them_in_position_order()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "First");
        await _repo.CreateCardAsync(listId, "Second");

        var cards = await _repo.GetCardsForListAsync(listId);

        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Positions_count_from_zero_per_list()
    {
        var listA = await NewListAsync();
        var listB = await NewListAsync();

        var a1 = await _repo.CreateCardAsync(listA, "A1");
        var b1 = await _repo.CreateCardAsync(listB, "B1");

        Assert.That(a1.Position, Is.EqualTo(0));
        Assert.That(b1.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Get_card_returns_it_or_null()
    {
        var listId = await NewListAsync();
        var created = await _repo.CreateCardAsync(listId, "Find me");

        Assert.That((await _repo.GetCardAsync(created.Id))!.Title, Is.EqualTo("Find me"));
        Assert.That(await _repo.GetCardAsync(9999), Is.Null);
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
