using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class BoardRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfBoardRepository _repo = null!;
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
        _repo = new EfBoardRepository(_db);
        // Board.OwnerId is required, so every test needs an owner to hang boards off.
        _ownerId = await TestUsers.SeedAsync(_db);
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
        _db.Boards.Add(new Board { Title = "Sprint 1", OwnerId = _ownerId });
        await _db.SaveChangesAsync();

        var board = await _db.Boards.SingleAsync();

        Assert.That(board.Id, Is.GreaterThan(0));
        Assert.That(board.Title, Is.EqualTo("Sprint 1"));
    }

    [Test]
    public async Task Create_adds_a_board_and_list_returns_it()
    {
        var created = await _repo.CreateBoardAsync("Sprint 1", _ownerId);

        var all = await _repo.GetBoardsAsync(_ownerId);

        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.OwnerId, Is.EqualTo(_ownerId));
        Assert.That(all.Select(b => b.Title), Is.EqualTo(new[] { "Sprint 1" }));
    }

    [Test]
    public async Task Get_returns_the_board_or_null()
    {
        var created = await _repo.CreateBoardAsync("Sprint 1", _ownerId);

        Assert.That((await _repo.GetBoardAsync(created.Id, _ownerId))?.Title, Is.EqualTo("Sprint 1"));
        Assert.That(await _repo.GetBoardAsync(9999, _ownerId), Is.Null);
    }

    [Test]
    public async Task Rename_changes_the_title_and_reports_missing()
    {
        var created = await _repo.CreateBoardAsync("Old", _ownerId);

        Assert.That(await _repo.RenameBoardAsync(created.Id, "New", _ownerId), Is.True);
        Assert.That((await _repo.GetBoardAsync(created.Id, _ownerId))!.Title, Is.EqualTo("New"));
        Assert.That(await _repo.RenameBoardAsync(9999, "X", _ownerId), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_board_and_reports_missing()
    {
        var created = await _repo.CreateBoardAsync("Temp", _ownerId);

        Assert.That(await _repo.DeleteBoardAsync(created.Id, _ownerId), Is.True);
        Assert.That(await _repo.GetBoardsAsync(_ownerId), Is.Empty);
        Assert.That(await _repo.DeleteBoardAsync(9999, _ownerId), Is.False);
    }

    [Test]
    public async Task Another_users_board_is_invisible_and_untouchable()
    {
        var mine = await _repo.CreateBoardAsync("Mine", _ownerId);
        var otherOwnerId = await TestUsers.SeedAsync(_db);

        // Every read and write scoped to the other user must behave as if the board is missing.
        Assert.That(await _repo.GetBoardAsync(mine.Id, otherOwnerId), Is.Null);
        Assert.That(await _repo.GetBoardsAsync(otherOwnerId), Is.Empty);
        Assert.That(await _repo.RenameBoardAsync(mine.Id, "Theirs", otherOwnerId), Is.False);
        Assert.That(await _repo.DeleteBoardAsync(mine.Id, otherOwnerId), Is.False);

        // ...and it is genuinely still there for its owner.
        Assert.That((await _repo.GetBoardAsync(mine.Id, _ownerId))!.Title, Is.EqualTo("Mine"));
    }
}
