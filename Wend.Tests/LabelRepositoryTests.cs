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
}
