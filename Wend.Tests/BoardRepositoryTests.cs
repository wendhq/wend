using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class BoardRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfBoardRepository _repo = null!;

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
        _repo = new EfBoardRepository(_db);
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
    
    [Test]
    public async Task Create_adds_a_board_and_list_returns_it()
    {
        var created = await _repo.CreateBoardAsync("Sprint 1");

        var all = await _repo.GetBoardsAsync();

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(all.Select(b => b.Title), Is.EqualTo(new[] { "Sprint 1" }));
    }

    [Test]
    public async Task Get_returns_the_board_or_null()
    {
        var created = await _repo.CreateBoardAsync("Sprint 1");

        Assert.That((await _repo.GetBoardAsync(created.Id))?.Title, Is.EqualTo("Sprint 1"));
        Assert.That(await _repo.GetBoardAsync(9999), Is.Null);
    }

    [Test]
    public async Task Rename_changes_the_title_and_reports_missing()
    {
        var created = await _repo.CreateBoardAsync("Old");

        Assert.That(await _repo.RenameBoardAsync(created.Id, "New"), Is.True);
        Assert.That((await _repo.GetBoardAsync(created.Id))!.Title, Is.EqualTo("New"));
        Assert.That(await _repo.RenameBoardAsync(9999, "X"), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_board_and_reports_missing()
    {
        var created = await _repo.CreateBoardAsync("Temp");

        Assert.That(await _repo.DeleteBoardAsync(created.Id), Is.True);
        Assert.That(await _repo.GetBoardsAsync(), Is.Empty);
        Assert.That(await _repo.DeleteBoardAsync(9999), Is.False);
    }
}
