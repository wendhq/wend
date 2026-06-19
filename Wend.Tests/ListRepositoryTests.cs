using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class ListRepositoryTests
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

    [Test]
    public async Task Saved_list_belongs_to_its_board_and_keeps_its_position()
    {
        var board = new Board { Title = "Sprint 1" };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();

        _db.Lists.Add(new List { BoardId = board.Id, Title = "To do", Position = 0 });
        await _db.SaveChangesAsync();

        var list = await _db.Lists.SingleAsync();
        Assert.That(list.Id, Is.GreaterThan(0));
        Assert.That(list.BoardId, Is.EqualTo(board.Id));
        Assert.That(list.Title, Is.EqualTo("To do"));
        Assert.That(list.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Deleting_a_board_cascades_to_its_lists()
    {
        var board = new Board { Title = "Temp" };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();
        _db.Lists.Add(new List { BoardId = board.Id, Title = "To do", Position = 0 });
        await _db.SaveChangesAsync();

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync();

        Assert.That(await _db.Lists.AnyAsync(), Is.False);
    }
}
