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

    private string _ownerId = null!;

    [SetUp]
    public async Task SetUp()
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
        // Board.OwnerId is required, so every test needs an owner to hang boards off.
        _ownerId = await TestUsers.SeedAsync(_db);
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
        var board = await _boards.CreateBoardAsync("Board", _ownerId);
        var list = await _lists.CreateListAsync(board.Id, "List", _ownerId);
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

        var first = await _repo.CreateCardAsync(listId, "First", _ownerId);
        var second = await _repo.CreateCardAsync(listId, "Second", _ownerId);

        Assert.That(first.Position, Is.EqualTo(0));
        Assert.That(second.Position, Is.EqualTo(1));
        Assert.That(first.CreatedAt, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public async Task Get_cards_for_list_returns_them_in_position_order()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "First", _ownerId);
        await _repo.CreateCardAsync(listId, "Second", _ownerId);

        var cards = await _repo.GetCardsForListAsync(listId, _ownerId);

        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Positions_count_from_zero_per_list()
    {
        var listA = await NewListAsync();
        var listB = await NewListAsync();

        var a1 = await _repo.CreateCardAsync(listA, "A1", _ownerId);
        var b1 = await _repo.CreateCardAsync(listB, "B1", _ownerId);

        Assert.That(a1.Position, Is.EqualTo(0));
        Assert.That(b1.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Get_card_returns_it_or_null()
    {
        var listId = await NewListAsync();
        var created = await _repo.CreateCardAsync(listId, "Find me", _ownerId);

        Assert.That((await _repo.GetCardAsync(created.Id, _ownerId))!.Title, Is.EqualTo("Find me"));
        Assert.That(await _repo.GetCardAsync(9999, _ownerId), Is.Null);
    }
    [Test]
    public async Task Edit_updates_the_fields_and_reports_missing()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Old", _ownerId);

        var due = new DateOnly(2026, 6, 25);
        Assert.That(await _repo.EditCardAsync(card.Id, "New", "Some notes", due, _ownerId), Is.True);

        var saved = (await _repo.GetCardAsync(card.Id, _ownerId))!;
        Assert.That(saved.Title, Is.EqualTo("New"));
        Assert.That(saved.Description, Is.EqualTo("Some notes"));
        Assert.That(saved.DueDate, Is.EqualTo(due));

        Assert.That(await _repo.EditCardAsync(9999, "X", null, null, _ownerId), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_card_and_resequences_the_rest()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "A", _ownerId);           // 0
        var b = await _repo.CreateCardAsync(listId, "B", _ownerId);   // 1
        await _repo.CreateCardAsync(listId, "C", _ownerId);           // 2

        Assert.That(await _repo.DeleteCardAsync(b.Id, _ownerId), Is.True);

        var cards = await _repo.GetCardsForListAsync(listId, _ownerId);
        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(cards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 })); // gapless
    }

    [Test]
    public async Task Delete_reports_missing()
    {
        Assert.That(await _repo.DeleteCardAsync(9999, _ownerId), Is.False);
    }

    [Test]
    public async Task Move_reorders_a_card_up_within_its_list()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "A", _ownerId);          // 0
        await _repo.CreateCardAsync(listId, "B", _ownerId);          // 1
        var c = await _repo.CreateCardAsync(listId, "C", _ownerId);  // 2

        Assert.That(await _repo.MoveCardAsync(c.Id, listId, 0, _ownerId), Is.EqualTo(CardMoveResult.Moved));

        var cards = await _repo.GetCardsForListAsync(listId, _ownerId);
        Assert.That(cards.Select(x => x.Title), Is.EqualTo(new[] { "C", "A", "B" }));
        Assert.That(cards.Select(x => x.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless
    }

    [Test]
    public async Task Move_reorders_a_card_down_within_its_list()
    {
        var listId = await NewListAsync();
        var a = await _repo.CreateCardAsync(listId, "A", _ownerId);  // 0
        await _repo.CreateCardAsync(listId, "B", _ownerId);          // 1
        await _repo.CreateCardAsync(listId, "C", _ownerId);          // 2

        Assert.That(await _repo.MoveCardAsync(a.Id, listId, 2, _ownerId), Is.EqualTo(CardMoveResult.Moved));

        var cards = await _repo.GetCardsForListAsync(listId, _ownerId);
        Assert.That(cards.Select(x => x.Title), Is.EqualTo(new[] { "B", "C", "A" }));
        Assert.That(cards.Select(x => x.Position), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public async Task Move_reports_a_missing_card()
    {
        var listId = await NewListAsync();
        Assert.That(await _repo.MoveCardAsync(9999, listId, 0, _ownerId), Is.EqualTo(CardMoveResult.NotFound));
    }

    [Test]
    public async Task Move_to_another_list_appends_at_its_bottom_and_resequences_both()
    {
        var board = await _boards.CreateBoardAsync("Board", _ownerId);
        var todo = await _lists.CreateListAsync(board.Id, "To do", _ownerId);
        var doing = await _lists.CreateListAsync(board.Id, "Doing", _ownerId);
        await _repo.CreateCardAsync(todo.Id, "A", _ownerId);          // todo 0
        var b = await _repo.CreateCardAsync(todo.Id, "B", _ownerId);  // todo 1
        await _repo.CreateCardAsync(todo.Id, "C", _ownerId);          // todo 2
        await _repo.CreateCardAsync(doing.Id, "X", _ownerId);         // doing 0

        // position 99 overshoots — it should clamp to the bottom.
        Assert.That(await _repo.MoveCardAsync(b.Id, doing.Id, 99, _ownerId), Is.EqualTo(CardMoveResult.Moved));

        var todoCards = await _repo.GetCardsForListAsync(todo.Id, _ownerId);
        Assert.That(todoCards.Select(c => c.Title), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(todoCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 }));  // source gapless

        var doingCards = await _repo.GetCardsForListAsync(doing.Id, _ownerId);
        Assert.That(doingCards.Select(c => c.Title), Is.EqualTo(new[] { "X", "B" }));
        Assert.That(doingCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 })); // target gapless
    }

    [Test]
    public async Task Move_to_another_list_can_insert_at_the_top()
    {
        var board = await _boards.CreateBoardAsync("Board", _ownerId);
        var todo = await _lists.CreateListAsync(board.Id, "To do", _ownerId);
        var doing = await _lists.CreateListAsync(board.Id, "Doing", _ownerId);
        var a = await _repo.CreateCardAsync(todo.Id, "A", _ownerId);
        await _repo.CreateCardAsync(doing.Id, "X", _ownerId);  // 0
        await _repo.CreateCardAsync(doing.Id, "Y", _ownerId);  // 1

        Assert.That(await _repo.MoveCardAsync(a.Id, doing.Id, 0, _ownerId), Is.EqualTo(CardMoveResult.Moved));

        var doingCards = await _repo.GetCardsForListAsync(doing.Id, _ownerId);
        Assert.That(doingCards.Select(c => c.Title), Is.EqualTo(new[] { "A", "X", "Y" }));
        Assert.That(doingCards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public async Task Move_reports_a_missing_target_list()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "A", _ownerId);

        Assert.That(await _repo.MoveCardAsync(card.Id, 9999, 0, _ownerId), Is.EqualTo(CardMoveResult.NotFound));
    }

    [Test]
    public async Task Move_to_a_list_on_another_board_is_rejected()
    {
        var boardA = await _boards.CreateBoardAsync("A", _ownerId);
        var listA = await _lists.CreateListAsync(boardA.Id, "A-list", _ownerId);
        var card = await _repo.CreateCardAsync(listA.Id, "Card", _ownerId);

        var boardB = await _boards.CreateBoardAsync("B", _ownerId);
        var listB = await _lists.CreateListAsync(boardB.Id, "B-list", _ownerId);

        Assert.That(await _repo.MoveCardAsync(card.Id, listB.Id, 0, _ownerId), Is.EqualTo(CardMoveResult.CrossBoard));
    }

    [Test]
    public async Task Set_completed_marks_a_card_done()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Ship Plan 6", _ownerId);

        Assert.That(await _repo.SetCardCompletedAsync(card.Id, true, _ownerId), Is.True);
        Assert.That((await _repo.GetCardAsync(card.Id, _ownerId))!.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task Set_completed_false_clears_the_done_mark()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Ship Plan 6", _ownerId);
        await _repo.SetCardCompletedAsync(card.Id, true, _ownerId);

        Assert.That(await _repo.SetCardCompletedAsync(card.Id, false, _ownerId), Is.True);
        Assert.That((await _repo.GetCardAsync(card.Id, _ownerId))!.CompletedAt, Is.Null);
    }

    [Test]
    public async Task Set_completed_reports_a_missing_card()
    {
        Assert.That(await _repo.SetCardCompletedAsync(9999, true, _ownerId), Is.False);
    }

    [Test]
    public async Task Delete_soft_deletes_so_the_row_survives_for_undo()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Temp", _ownerId);

        Assert.That(await _repo.DeleteCardAsync(card.Id, _ownerId), Is.True);

        // Hidden from normal queries…
        Assert.That(await _repo.GetCardsForListAsync(listId, _ownerId), Is.Empty);
        // …but the row still exists with DeletedAt set, so undo can bring it back.
        var row = await _db.Cards.IgnoreQueryFilters().SingleAsync(c => c.Id == card.Id);
        Assert.That(row.DeletedAt, Is.Not.Null);
    }

    [Test]
    public async Task Deleting_an_already_deleted_card_reports_missing()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Temp", _ownerId);
        await _repo.DeleteCardAsync(card.Id, _ownerId);

        Assert.That(await _repo.DeleteCardAsync(card.Id, _ownerId), Is.False);
    }

    [Test]
    public async Task Restore_brings_a_deleted_card_back_to_its_original_position()
    {
        var listId = await NewListAsync();
        await _repo.CreateCardAsync(listId, "A", _ownerId);          // 0
        var b = await _repo.CreateCardAsync(listId, "B", _ownerId);  // 1
        await _repo.CreateCardAsync(listId, "C", _ownerId);          // 2

        await _repo.DeleteCardAsync(b.Id, _ownerId);                 // survivors resequence to A(0), C(1)
        Assert.That(await _repo.RestoreCardAsync(b.Id, _ownerId), Is.True);

        var cards = await _repo.GetCardsForListAsync(listId, _ownerId);
        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(cards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless, B back in the middle
    }

    [Test]
    public async Task Restore_is_idempotent_for_a_card_that_is_not_deleted()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Here", _ownerId);

        Assert.That(await _repo.RestoreCardAsync(card.Id, _ownerId), Is.True); // no-op, still reports found
        Assert.That((await _repo.GetCardsForListAsync(listId, _ownerId)).Single().Title, Is.EqualTo("Here"));
    }

    [Test]
    public async Task Restore_reports_a_missing_card()
    {
        Assert.That(await _repo.RestoreCardAsync(9999, _ownerId), Is.False);
    }

    [Test]
    public async Task Restoring_a_done_card_keeps_it_done()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Shipped", _ownerId);
        await _repo.SetCardCompletedAsync(card.Id, true, _ownerId);
        await _repo.DeleteCardAsync(card.Id, _ownerId);

        Assert.That(await _repo.RestoreCardAsync(card.Id, _ownerId), Is.True);
        var row = await _db.Cards.IgnoreQueryFilters().SingleAsync(c => c.Id == card.Id);
        Assert.That(row.DeletedAt, Is.Null);
        Assert.That(row.CompletedAt, Is.Not.Null); // still done after undo
    }

    [Test]
    public async Task Get_card_hides_a_soft_deleted_card()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Temp", _ownerId);
        await _repo.DeleteCardAsync(card.Id, _ownerId);

        Assert.That(await _repo.GetCardAsync(card.Id, _ownerId), Is.Null);
    }

    [Test]
    public async Task Restore_works_from_a_fresh_context_not_only_a_tracked_one()
    {
        var listId = await NewListAsync();
        var card = await _repo.CreateCardAsync(listId, "Temp", _ownerId);
        await _repo.DeleteCardAsync(card.Id, _ownerId);

        _db.ChangeTracker.Clear(); // force a DB read, as a new HTTP request would — no tracked entity

        Assert.That(await _repo.RestoreCardAsync(card.Id, _ownerId), Is.True);
        Assert.That((await _repo.GetCardsForListAsync(listId, _ownerId)).Select(c => c.Title), Is.EqualTo(new[] { "Temp" }));
    }

    [Test]
    public async Task Create_refuses_a_list_owned_by_someone_else()
    {
        var stranger = await TestUsers.SeedAsync(_db);
        var theirBoard = await _boards.CreateBoardAsync("Theirs", stranger);
        var theirList = await _lists.CreateListAsync(theirBoard.Id, "Theirs", stranger);

        Assert.That(async () => await _repo.CreateCardAsync(theirList.Id, "Sneaky", _ownerId),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(await _db.Cards.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Create_refuses_a_list_that_does_not_exist()
    {
        Assert.That(async () => await _repo.CreateCardAsync(9999, "Nowhere", _ownerId),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(await _db.Cards.CountAsync(), Is.Zero);
    }
}
