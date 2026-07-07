using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class LabelRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfBoardRepository _boards = null!;
    private EfListRepository _lists = null!;
    private EfCardRepository _cards = null!;
    private EfLabelRepository _labels = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WendDbContext>().UseSqlite(_connection).Options;
        _db = new WendDbContext(options);
        _db.Database.EnsureCreated();
        _boards = new EfBoardRepository(_db);
        _lists = new EfListRepository(_db);
        _cards = new EfCardRepository(_db);
        _labels = new EfLabelRepository(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task A_label_belongs_to_its_board()
    {
        var board = await _boards.CreateBoardAsync("Board");
        _db.Labels.Add(new Label { BoardId = board.Id, Name = "Urgent", Colour = "rose" });
        await _db.SaveChangesAsync();

        var label = await _db.Labels.SingleAsync();
        Assert.That(label.Id, Is.GreaterThan(0));
        Assert.That(label.BoardId, Is.EqualTo(board.Id));
        Assert.That(label.Name, Is.EqualTo("Urgent"));
        Assert.That(label.Colour, Is.EqualTo("rose"));
    }

    [Test]
    public async Task Deleting_a_board_cascades_to_its_labels()
    {
        var board = await _boards.CreateBoardAsync("Board");
        _db.Labels.Add(new Label { BoardId = board.Id, Name = "Urgent", Colour = "rose" });
        await _db.SaveChangesAsync();

        await _boards.DeleteBoardAsync(board.Id);

        Assert.That(await _db.Labels.AnyAsync(), Is.False);
    }

    [Test]
    public void Palette_keys_are_validated()
    {
        Assert.That(LabelColours.IsValid("mint"), Is.True);
        Assert.That(LabelColours.IsValid("cyan"), Is.True);
        Assert.That(LabelColours.IsValid("scarlet"), Is.False);
        Assert.That(LabelColours.IsValid(null), Is.False);
        Assert.That(LabelColours.All.Count, Is.EqualTo(6));
    }

    [Test]
    public async Task Create_returns_a_board_scoped_label()
    {
        var board = await _boards.CreateBoardAsync("Board");

        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");

        Assert.That(label.Id, Is.GreaterThan(0));
        Assert.That(label.BoardId, Is.EqualTo(board.Id));
        Assert.That(label.Name, Is.EqualTo("Urgent"));
        Assert.That(label.Colour, Is.EqualTo("rose"));
    }

    [Test]
    public async Task Get_for_board_lists_labels_in_creation_order()
    {
        var board = await _boards.CreateBoardAsync("Board");
        await _labels.CreateLabelAsync(board.Id, "First", "mint");
        await _labels.CreateLabelAsync(board.Id, "Second", "cyan");

        var labels = await _labels.GetBoardLabelsAsync(board.Id);

        Assert.That(labels.Select(l => l.Name), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Get_for_board_excludes_other_boards()
    {
        var a = await _boards.CreateBoardAsync("A");
        var b = await _boards.CreateBoardAsync("B");
        await _labels.CreateLabelAsync(a.Id, "OnA", "mint");
        await _labels.CreateLabelAsync(b.Id, "OnB", "cyan");

        var labels = await _labels.GetBoardLabelsAsync(a.Id);

        Assert.That(labels.Select(l => l.Name), Is.EqualTo(new[] { "OnA" }));
    }

    [Test]
    public async Task Get_label_returns_it_or_null()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var created = await _labels.CreateLabelAsync(board.Id, "Find me", "amber");

        Assert.That((await _labels.GetLabelAsync(created.Id))!.Name, Is.EqualTo("Find me"));
        Assert.That(await _labels.GetLabelAsync(9999), Is.Null);
    }

    [Test]
    public async Task Edit_updates_name_and_colour_and_reports_missing()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var label = await _labels.CreateLabelAsync(board.Id, "Old", "mint");

        Assert.That(await _labels.EditLabelAsync(label.Id, "New", "lilac"), Is.True);
        var saved = (await _labels.GetLabelAsync(label.Id))!;
        Assert.That(saved.Name, Is.EqualTo("New"));
        Assert.That(saved.Colour, Is.EqualTo("lilac"));

        Assert.That(await _labels.EditLabelAsync(9999, "X", "mint"), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_label_and_reports_missing()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var label = await _labels.CreateLabelAsync(board.Id, "Temp", "slate");

        Assert.That(await _labels.DeleteLabelAsync(label.Id), Is.True);
        Assert.That(await _labels.GetLabelAsync(label.Id), Is.Null);
        Assert.That(await _labels.DeleteLabelAsync(9999), Is.False);
    }

    [Test]
    public async Task Deleting_a_label_cascades_its_join_rows()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");
        _db.CardLabels.Add(new CardLabel { CardId = card.Id, LabelId = label.Id });
        await _db.SaveChangesAsync();

        await _labels.DeleteLabelAsync(label.Id);

        Assert.That(await _db.CardLabels.AnyAsync(), Is.False);
    }
        [Test]
    public async Task Attach_links_a_card_and_a_label_and_is_idempotent()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");

        await _labels.AttachAsync(card.Id, label.Id);
        await _labels.AttachAsync(card.Id, label.Id); // again — no duplicate, no throw

        Assert.That(await _db.CardLabels.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Detach_unlinks_and_is_a_no_op_when_not_attached()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");

        await _labels.DetachAsync(card.Id, label.Id); // nothing attached yet — no throw
        await _labels.AttachAsync(card.Id, label.Id);
        await _labels.DetachAsync(card.Id, label.Id);

        Assert.That(await _db.CardLabels.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Get_card_labels_returns_attached_labels_in_id_order()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var a = await _labels.CreateLabelAsync(board.Id, "A", "mint");
        var b = await _labels.CreateLabelAsync(board.Id, "B", "cyan");
        await _labels.AttachAsync(card.Id, b.Id);
        await _labels.AttachAsync(card.Id, a.Id);

        var attached = await _labels.GetCardLabelsAsync(card.Id);

        Assert.That(attached.Select(l => l.Name), Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task Soft_deleting_a_card_keeps_its_join_rows_so_undo_can_recover_labels()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");
        await _labels.AttachAsync(card.Id, label.Id);

        await _cards.DeleteCardAsync(card.Id);

        // Soft delete keeps the row, so its label links survive — a restored card keeps its labels.
        Assert.That(await _db.CardLabels.AnyAsync(), Is.True);
        Assert.That((await _labels.GetLabelAsync(label.Id)), Is.Not.Null); // the label itself survives too
    }

    [Test]
    public async Task Deleting_a_board_removes_its_labels_and_join_rows()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var label = await _labels.CreateLabelAsync(board.Id, "Urgent", "rose");
        await _labels.AttachAsync(card.Id, label.Id);

        await _boards.DeleteBoardAsync(board.Id);

        Assert.That(await _db.Labels.AnyAsync(), Is.False);
        Assert.That(await _db.CardLabels.AnyAsync(), Is.False);
    }

    [Test]
    public async Task Label_ids_by_card_groups_only_this_boards_cards()
    {
        var board = await _boards.CreateBoardAsync("Board");
        var list = await _lists.CreateListAsync(board.Id, "List");
        var card = await _cards.CreateCardAsync(list.Id, "Card");
        var a = await _labels.CreateLabelAsync(board.Id, "A", "mint");
        var b = await _labels.CreateLabelAsync(board.Id, "B", "cyan");
        await _labels.AttachAsync(card.Id, a.Id);
        await _labels.AttachAsync(card.Id, b.Id);

        var other = await _boards.CreateBoardAsync("Other");
        var otherList = await _lists.CreateListAsync(other.Id, "L");
        var otherCard = await _cards.CreateCardAsync(otherList.Id, "C");
        var c = await _labels.CreateLabelAsync(other.Id, "C", "amber");
        await _labels.AttachAsync(otherCard.Id, c.Id);

        var map = await _labels.GetLabelIdsByCardAsync(board.Id);

        Assert.That(map.Keys, Is.EquivalentTo(new[] { card.Id }));
        Assert.That(map[card.Id], Is.EquivalentTo(new[] { a.Id, b.Id }));
    }
}
