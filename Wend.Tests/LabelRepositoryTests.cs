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
}
