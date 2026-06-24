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
    public async Task Saved_card_belongs_to_its_list_and_keeps_its_position()
    {
        var listId = await NewListAsync();

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
        var listId = await NewListAsync();
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
        var listId = await NewListAsync();
        _db.Cards.Add(new Card { ListId = listId, Title = "Visible", Position = 0, CreatedAt = DateTime.UtcNow });
        _db.Cards.Add(new Card { ListId = listId, Title = "Gone", Position = 1, CreatedAt = DateTime.UtcNow, DeletedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var titles = await _db.Cards.Select(c => c.Title).ToListAsync();
        Assert.That(titles, Is.EqualTo(new[] { "Visible" }));
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
    public async Task Edit_updates_the_fields_and_reports_missing()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Old");

        var due = new DateOnly(2026, 6, 25);
        Assert.That(await _repo.EditCardAsync(card.Id, "New", "Some notes", due), Is.True);

        var saved = (await _repo.GetCardAsync(card.Id))!;
        Assert.That(saved.Title, Is.EqualTo("New"));
        Assert.That(saved.Description, Is.EqualTo("Some notes"));
        Assert.That(saved.DueDate, Is.EqualTo(due));

        Assert.That(await _repo.EditCardAsync(9999, "X", null, null), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_card_and_resequences_the_rest()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "A");           // 0
        var b = await _repo.CreateCardAsync(listId, "B");   // 1
        await _repo.CreateCardAsync(listId, "C");           // 2

        Assert.That(await _repo.DeleteCardAsync(b.Id), Is.True);

        var cards = await _repo.GetCardsForListAsync(listId);
        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(cards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 })); // gapless
    }

    [Test]
    public async Task Delete_reports_missing()
    {
        Assert.That(await _repo.DeleteCardAsync(9999), Is.False);
    }

    [Test]
    public async Task Move_reorders_a_card_up_within_its_list()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "A");          // 0
        await _repo.CreateCardAsync(listId, "B");          // 1
        var c = await _repo.CreateCardAsync(listId, "C");  // 2

        Assert.That(await _repo.MoveCardAsync(c.Id, listId, 0), Is.EqualTo(CardMoveResult.Moved));

        var cards = await _repo.GetCardsForListAsync(listId);
        Assert.That(cards.Select(x => x.Title), Is.EqualTo(new[] { "C", "A", "B" }));
        Assert.That(cards.Select(x => x.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless
    }

    [Test]
    public async Task Move_reorders_a_card_down_within_its_list()
    {
        var listId = await NewListAsync();
        var a = await _repo.CreateCardAsync(listId, "A");  // 0
        await _repo.CreateCardAsync(listId, "B");          // 1
        await _repo.CreateCardAsync(listId, "C");          // 2

        Assert.That(await _repo.MoveCardAsync(a.Id, listId, 2), Is.EqualTo(CardMoveResult.Moved));

        var cards = await _repo.GetCardsForListAsync(listId);
        Assert.That(cards.Select(x => x.Title), Is.EqualTo(new[] { "B", "C", "A" }));
        Assert.That(cards.Select(x => x.Position), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public async Task Move_reports_a_missing_card()
    {
        var listId = await NewListAsync();
        Assert.That(await _repo.MoveCardAsync(9999, listId, 0), Is.EqualTo(CardMoveResult.NotFound));
    }
    
    [Test]
    public async Task Move_to_another_list_appends_at_its_bottom_and_resequences_both()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var todo = await _lists.CreateListAsync(board.Id, "To do");
        var doing = await _lists.CreateListAsync(board.Id, "Doing");
        await _repo.CreateCardAsync(todo.Id, "A");          // todo 0
        var b = await _repo.CreateCardAsync(todo.Id, "B");  // todo 1
        await _repo.CreateCardAsync(todo.Id, "C");          // todo 2
        await _repo.CreateCardAsync(doing.Id, "X");         // doing 0

        // position 99 overshoots — it should clamp to the bottom.
        Assert.That(await _repo.MoveCardAsync(b.Id, doing.Id, 99), Is.EqualTo(CardMoveResult.Moved));

        var todoCards = await _repo.GetCardsForListAsync(todo.Id);
        Assert.That(todoCards.Select(c => c.Title), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(todoCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 }));  // source gapless

        var doingCards = await _repo.GetCardsForListAsync(doing.Id);
        Assert.That(doingCards.Select(c => c.Title), Is.EqualTo(new[] { "X", "B" }));
        Assert.That(doingCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 })); // target gapless
    }

    [Test]
    public async Task Move_to_another_list_can_insert_at_the_top()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var todo = await _lists.CreateListAsync(board.Id, "To do");
        var doing = await _lists.CreateListAsync(board.Id, "Doing");
        var a = await _repo.CreateCardAsync(todo.Id, "A");
        await _repo.CreateCardAsync(doing.Id, "X");  // 0
        await _repo.CreateCardAsync(doing.Id, "Y");  // 1

        Assert.That(await _repo.MoveCardAsync(a.Id, doing.Id, 0), Is.EqualTo(CardMoveResult.Moved));

        var doingCards = await _repo.GetCardsForListAsync(doing.Id);
        Assert.That(doingCards.Select(c => c.Title), Is.EqualTo(new[] { "A", "X", "Y" }));
        Assert.That(doingCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1, 2 }));
    }
}
