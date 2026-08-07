using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wend.Core;

namespace Wend.Tests;

public class ListRepositoryTests
{
    private SqliteConnection _connection = null!;
    private WendDbContext _db = null!;
    private EfListRepository _repo = null!;
    private EfBoardRepository _boards = null!;

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
        _repo = new EfListRepository(_db);
        _boards = new EfBoardRepository(_db);
        // Board.OwnerId is required, so every test needs an owner to hang boards off.
        _ownerId = await TestUsers.SeedAsync(_db);
    }
    
    private async Task<int> NewBoardAsync(string title = "Board") =>
        (await _boards.CreateBoardAsync(title, _ownerId)).Id;

    [Test]
    public async Task Create_appends_each_list_at_the_next_position()
    {
        var boardId = await NewBoardAsync();

        var first = await _repo.CreateListAsync(boardId, "To do", _ownerId);
        var second = await _repo.CreateListAsync(boardId, "Doing", _ownerId);

        Assert.That(first.Position, Is.EqualTo(0));
        Assert.That(second.Position, Is.EqualTo(1));
    }

    [Test]
    public async Task Get_lists_for_board_returns_them_in_position_order()
    {
        var boardId = await NewBoardAsync();
        await _repo.CreateListAsync(boardId, "To do", _ownerId);
        await _repo.CreateListAsync(boardId, "Doing", _ownerId);

        var lists = await _repo.GetListsForBoardAsync(boardId, _ownerId);

        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "To do", "Doing" }));
    }

    [Test]
    public async Task Positions_count_from_zero_per_board()
    {
        var boardA = await NewBoardAsync("A");
        var boardB = await NewBoardAsync("B");

        var a1 = await _repo.CreateListAsync(boardA, "A1", _ownerId);
        var b1 = await _repo.CreateListAsync(boardB, "B1", _ownerId);

        Assert.That(a1.Position, Is.EqualTo(0));
        Assert.That(b1.Position, Is.EqualTo(0));
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
        var board = new Board { Title = "Sprint 1", OwnerId = _ownerId };
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
        var board = new Board { Title = "Temp", OwnerId = _ownerId };
        _db.Boards.Add(board);
        await _db.SaveChangesAsync();
        _db.Lists.Add(new List { BoardId = board.Id, Title = "To do", Position = 0 });
        await _db.SaveChangesAsync();

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync();

        Assert.That(await _db.Lists.AnyAsync(), Is.False);
    }
    [Test]
    public async Task Rename_changes_the_title_and_reports_missing()
    {
        var boardId = await NewBoardAsync();
        var list = await _repo.CreateListAsync(boardId, "Old", _ownerId);

        Assert.That(await _repo.RenameListAsync(list.Id, "New", _ownerId), Is.True);
        var lists = await _repo.GetListsForBoardAsync(boardId, _ownerId);
        Assert.That(lists.Single().Title, Is.EqualTo("New"));
        Assert.That(await _repo.RenameListAsync(9999, "X", _ownerId), Is.False);
    }

    [Test]
    public async Task Delete_removes_the_list_and_resequences_the_rest()
    {
        var boardId = await NewBoardAsync();
        await _repo.CreateListAsync(boardId, "A", _ownerId);           // 0
        var b = await _repo.CreateListAsync(boardId, "B", _ownerId);   // 1
        await _repo.CreateListAsync(boardId, "C", _ownerId);           // 2

        Assert.That(await _repo.DeleteListAsync(b.Id, _ownerId), Is.True);

        var lists = await _repo.GetListsForBoardAsync(boardId, _ownerId);
        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "A", "C" }));
        Assert.That(lists.Select(l => l.Position), Is.EqualTo(new[] { 0, 1 })); // gapless
    }

    [Test]
    public async Task Delete_reports_missing()
    {
        Assert.That(await _repo.DeleteListAsync(9999, _ownerId), Is.False);
    }
    
    [Test]
    public async Task Move_reorders_within_the_board_and_resequences()
    {
        var boardId = await NewBoardAsync();
        var a = await _repo.CreateListAsync(boardId, "A", _ownerId); // 0
        await _repo.CreateListAsync(boardId, "B", _ownerId);          // 1
        await _repo.CreateListAsync(boardId, "C", _ownerId);          // 2

        Assert.That(await _repo.MoveListAsync(a.Id, 2, _ownerId), Is.True);

        var lists = await _repo.GetListsForBoardAsync(boardId, _ownerId);
        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "B", "C", "A" }));
        Assert.That(lists.Select(l => l.Position), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public async Task Move_clamps_an_out_of_range_position()
    {
        var boardId = await NewBoardAsync();
        var a = await _repo.CreateListAsync(boardId, "A", _ownerId);
        await _repo.CreateListAsync(boardId, "B", _ownerId);

        Assert.That(await _repo.MoveListAsync(a.Id, 99, _ownerId), Is.True);

        var lists = await _repo.GetListsForBoardAsync(boardId, _ownerId);
        Assert.That(lists.Select(l => l.Title), Is.EqualTo(new[] { "B", "A" }));
    }

    [Test]
    public async Task Move_reports_missing_list()
    {
        Assert.That(await _repo.MoveListAsync(9999, 0, _ownerId), Is.False);
    }

    [Test]
    public async Task Create_refuses_a_board_owned_by_someone_else()
    {
        var stranger = await TestUsers.SeedAsync(_db);
        var theirBoard = (await _boards.CreateBoardAsync("Theirs", stranger)).Id;

        Assert.That(async () => await _repo.CreateListAsync(theirBoard, "Sneaky", _ownerId),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(await _db.Lists.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Create_refuses_a_board_that_does_not_exist()
    {
        Assert.That(async () => await _repo.CreateListAsync(9999, "Nowhere", _ownerId),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(await _db.Lists.CountAsync(), Is.Zero);
    }
}
