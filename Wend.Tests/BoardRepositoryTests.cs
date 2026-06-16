using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class BoardRepositoryTests
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
    public async Task Saved_board_can_be_read_back()
    {
        _db.Boards.Add(new Board { Title = "Sprint 1" });
        await _db.SaveChangesAsync();

        var board = await _db.Boards.SingleAsync();

        Assert.That(board.Id, Is.GreaterThan(0));
        Assert.That(board.Title, Is.EqualTo("Sprint 1"));
    }
}
