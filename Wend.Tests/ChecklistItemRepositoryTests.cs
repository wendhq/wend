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
    private EfChecklistItemRepository _repo = null!;

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
        _boards = new EfBoardRepository(_db);
        _lists = new EfListRepository(_db);
        _cards = new EfCardRepository(_db);
        _repo = new EfChecklistItemRepository(_db);
        // Board.OwnerId is required, so every test needs an owner to hang boards off.
        _ownerId = await TestUsers.SeedAsync(_db);
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
        var board = await _boards.CreateBoardAsync("Board", _ownerId);
        var list = await _lists.CreateListAsync(board.Id, "List", _ownerId);
        var card = await _cards.CreateCardAsync(list.Id, "Card", _ownerId);
        return card.Id;
    }

    [Test]
    public async Task Another_users_item_is_invisible_and_untouchable()
    {
        // Pinned at the repository layer as well as over HTTP: this repository's ownership helper
        // is the only one that turns query filters off, so its boundary is worth proving directly.
        var cardId = await NewCardAsync();
        var item = await _repo.AddItemAsync(cardId, "Mine", _ownerId);
        var intruder = await TestUsers.SeedAsync(_db);

        Assert.That(await _repo.GetItemsForCardAsync(cardId, intruder), Is.Empty);
        Assert.That(await _repo.RenameItemAsync(item.Id, "Theirs", intruder), Is.False);
        Assert.That(await _repo.SetCheckedAsync(item.Id, true, intruder), Is.False);
        Assert.That(await _repo.MoveItemAsync(item.Id, 0, intruder), Is.False);
        Assert.That(await _repo.DeleteItemAsync(item.Id, intruder), Is.False);

        // Soft-delete it as the owner, then confirm the intruder cannot reach it through the
        // ignore-filtered restore path either.
        Assert.That(await _repo.DeleteItemAsync(item.Id, _ownerId), Is.True);
        Assert.That(await _repo.RestoreItemAsync(item.Id, intruder), Is.False);
        Assert.That(await _repo.RestoreItemAsync(item.Id, _ownerId), Is.True);
    }

    [Test]
    public async Task Items_survive_their_cards_soft_delete()
    {
        // Traversing i.Card for ownership must NOT drag Card's soft-delete filter along:
        // card undo restores the card AND its checklist, so the items have to still be there.
        var board = await _boards.CreateBoardAsync("Board", _ownerId);
        var list = await _lists.CreateListAsync(board.Id, "To do", _ownerId);
        var card = await _cards.CreateCardAsync(list.Id, "A card", _ownerId);
        await _repo.AddItemAsync(card.Id, "Step one", _ownerId);

        await _cards.DeleteCardAsync(card.Id, _ownerId);   // soft delete

        var items = await _repo.GetItemsForCardAsync(card.Id, _ownerId);
        Assert.That(items, Has.Count.EqualTo(1));
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

    [Test]
    public async Task Add_appends_each_item_at_the_next_position()
    {
        var cardId = await NewCardAsync();

        var first = await _repo.AddItemAsync(cardId, "First", _ownerId);
        var second = await _repo.AddItemAsync(cardId, "Second", _ownerId);

        Assert.That(first.Position, Is.EqualTo(0));
        Assert.That(second.Position, Is.EqualTo(1));
    }

    [Test]
    public async Task Positions_count_from_zero_per_card()
    {
        var cardA = await NewCardAsync();
        var cardB = await NewCardAsync();

        var a1 = await _repo.AddItemAsync(cardA, "A1", _ownerId);
        var b1 = await _repo.AddItemAsync(cardB, "B1", _ownerId);

        Assert.That(a1.Position, Is.EqualTo(0));
        Assert.That(b1.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Get_items_for_card_returns_them_in_position_order()
    {
        var cardId = await NewCardAsync();
        await _repo.AddItemAsync(cardId, "First", _ownerId);
        await _repo.AddItemAsync(cardId, "Second", _ownerId);

        var items = await _repo.GetItemsForCardAsync(cardId, _ownerId);

        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Rename_updates_the_text_and_reports_missing()
    {
        var cardId = await NewCardAsync();
        var item = await _repo.AddItemAsync(cardId, "Old", _ownerId);

        Assert.That(await _repo.RenameItemAsync(item.Id, "New", _ownerId), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId, _ownerId)).Single().Text, Is.EqualTo("New"));

        Assert.That(await _repo.RenameItemAsync(9999, "X", _ownerId), Is.False);
    }

    [Test]
    public async Task Set_checked_stamps_checkedAt_and_clears_it()
    {
        var cardId = await NewCardAsync();
        var item = await _repo.AddItemAsync(cardId, "Do it", _ownerId);

        Assert.That(await _repo.SetCheckedAsync(item.Id, true, _ownerId), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId, _ownerId)).Single().CheckedAt, Is.Not.Null);

        Assert.That(await _repo.SetCheckedAsync(item.Id, false, _ownerId), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId, _ownerId)).Single().CheckedAt, Is.Null);
    }

    [Test]
    public async Task Set_checked_reports_a_missing_item()
    {
        Assert.That(await _repo.SetCheckedAsync(9999, true, _ownerId), Is.False);
    }

    [Test]
    public async Task Move_reorders_within_the_card_and_clamps_an_overshoot()
    {
        var cardId = await NewCardAsync();
        var a = await _repo.AddItemAsync(cardId, "A", _ownerId);   // 0
        await _repo.AddItemAsync(cardId, "B", _ownerId);           // 1
        var c = await _repo.AddItemAsync(cardId, "C", _ownerId);   // 2

        Assert.That(await _repo.MoveItemAsync(c.Id, 0, _ownerId), Is.True);
        var items = await _repo.GetItemsForCardAsync(cardId, _ownerId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "C", "A", "B" }));
        Assert.That(items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless

        // Position 99 overshoots — it should clamp to the bottom.
        Assert.That(await _repo.MoveItemAsync(a.Id, 99, _ownerId), Is.True);
        items = await _repo.GetItemsForCardAsync(cardId, _ownerId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "C", "B", "A" }));
    }

    [Test]
    public async Task Move_reports_a_missing_item()
    {
        Assert.That(await _repo.MoveItemAsync(9999, 0, _ownerId), Is.False);
    }

    [Test]
    public async Task Delete_soft_deletes_and_resequences_the_rest()
    {
        var cardId = await NewCardAsync();
        await _repo.AddItemAsync(cardId, "A", _ownerId);           // 0
        var b = await _repo.AddItemAsync(cardId, "B", _ownerId);   // 1
        await _repo.AddItemAsync(cardId, "C", _ownerId);           // 2

        Assert.That(await _repo.DeleteItemAsync(b.Id, _ownerId), Is.True);

        // Hidden from normal queries and the survivors close the gap…
        var items = await _repo.GetItemsForCardAsync(cardId, _ownerId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1 }));
        // …but the row still exists with DeletedAt set, so undo can bring it back.
        var row = await _db.ChecklistItems.IgnoreQueryFilters().SingleAsync(i => i.Id == b.Id);
        Assert.That(row.DeletedAt, Is.Not.Null);
    }

    [Test]
    public async Task Restore_brings_an_item_back_to_its_original_position()
    {
        var cardId = await NewCardAsync();
        await _repo.AddItemAsync(cardId, "A", _ownerId);           // 0
        var b = await _repo.AddItemAsync(cardId, "B", _ownerId);   // 1
        await _repo.AddItemAsync(cardId, "C", _ownerId);           // 2

        await _repo.DeleteItemAsync(b.Id, _ownerId);               // survivors resequence to A(0), C(1)
        Assert.That(await _repo.RestoreItemAsync(b.Id, _ownerId), Is.True);

        var items = await _repo.GetItemsForCardAsync(cardId, _ownerId);
        Assert.That(items.Select(i => i.Text), Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1, 2 })); // gapless, B back in the middle
    }

    [Test]
    public async Task Restore_works_from_a_fresh_context_not_only_a_tracked_one()
    {
        var cardId = await NewCardAsync();
        var item = await _repo.AddItemAsync(cardId, "Temp", _ownerId);
        await _repo.DeleteItemAsync(item.Id, _ownerId);

        _db.ChangeTracker.Clear(); // force a DB read, as a new HTTP request would — no tracked entity

        Assert.That(await _repo.RestoreItemAsync(item.Id, _ownerId), Is.True);
        Assert.That((await _repo.GetItemsForCardAsync(cardId, _ownerId)).Select(i => i.Text), Is.EqualTo(new[] { "Temp" }));
    }
}
