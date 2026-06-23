using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class LabelApiTests
{
    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WendApiFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private record BoardDto(int Id, string Title);
    private record ListDto(int Id, string Title, int Position);
    private record CardDto(int Id, string Title, int Position);
    private record LabelDto(int Id, string Name, string Colour);

    private async Task<BoardDto> CreateBoardAsync(string title) =>
        (await (await _client.PostAsJsonAsync("/api/boards", new { title })).Content.ReadFromJsonAsync<BoardDto>())!;

    private async Task<ListDto> CreateListAsync(int boardId, string title) =>
        (await (await _client.PostAsJsonAsync($"/api/boards/{boardId}/lists", new { title })).Content.ReadFromJsonAsync<ListDto>())!;

    private async Task<CardDto> CreateCardAsync(int listId, string title) =>
        (await (await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title })).Content.ReadFromJsonAsync<CardDto>())!;

    private async Task<LabelDto> CreateLabelAsync(int boardId, string name, string colour) =>
        (await (await _client.PostAsJsonAsync($"/api/boards/{boardId}/labels", new { name, colour })).Content.ReadFromJsonAsync<LabelDto>())!;

    [Test]
    public async Task Posting_a_label_creates_it_on_the_board()
    {
        var board = await CreateBoardAsync("Board");

        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = "Urgent", colour = "rose" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await res.Content.ReadFromJsonAsync<LabelDto>();
        Assert.That(created!.Name, Is.EqualTo("Urgent"));
        Assert.That(created.Colour, Is.EqualTo("rose"));
    }

    [Test]
    public async Task Get_lists_the_boards_palette_in_order()
    {
        var board = await CreateBoardAsync("Board");
        await CreateLabelAsync(board.Id, "First", "mint");
        await CreateLabelAsync(board.Id, "Second", "cyan");

        var palette = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");

        Assert.That(palette!.Select(l => l.Name), Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public async Task Posting_a_blank_label_name_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = "  ", colour = "mint" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_label_name_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = new string('x', 51), colour = "mint" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_unknown_colour_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var res = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/labels", new { name = "Urgent", colour = "scarlet" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Labels_for_a_missing_board_are_404()
    {
        var get = await _client.GetAsync("/api/boards/9999/labels");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var post = await _client.PostAsJsonAsync("/api/boards/9999/labels", new { name = "X", colour = "mint" });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
        [Test]
    public async Task Put_edits_a_labels_name_and_colour()
    {
        var board = await CreateBoardAsync("Board");
        var label = await CreateLabelAsync(board.Id, "Old", "mint");

        var put = await _client.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = "New", colour = "lilac" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var palette = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        Assert.That(palette!.Single().Name, Is.EqualTo("New"));
        Assert.That(palette.Single().Colour, Is.EqualTo("lilac"));
    }

    [Test]
    public async Task Put_rejects_a_bad_name_or_colour()
    {
        var board = await CreateBoardAsync("Board");
        var label = await CreateLabelAsync(board.Id, "Old", "mint");

        var blank = await _client.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = " ", colour = "mint" });
        Assert.That(blank.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var badColour = await _client.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = "Ok", colour = "scarlet" });
        Assert.That(badColour.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Put_to_a_missing_label_is_404()
    {
        var put = await _client.PutAsJsonAsync("/api/labels/9999", new { name = "X", colour = "mint" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_removes_a_label()
    {
        var board = await CreateBoardAsync("Board");
        var label = await CreateLabelAsync(board.Id, "Temp", "slate");

        var del = await _client.DeleteAsync($"/api/labels/{label.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var palette = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        Assert.That(palette, Is.Empty);
    }

    [Test]
    public async Task Delete_a_missing_label_is_404()
    {
        var del = await _client.DeleteAsync("/api/labels/9999");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
