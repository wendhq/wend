using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class ListApiTests
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

    // Test-only shapes for the JSON the API returns.
    private record BoardDto(int Id, string Title);
    private record ListDto(int Id, string Title, int Position);
    private record BoardDetailDto(int Id, string Title, List<ListDto> Lists);

    private async Task<BoardDto> CreateBoardAsync(string title)
    {
        var res = await _client.PostAsJsonAsync("/api/boards", new { title });
        return (await res.Content.ReadFromJsonAsync<BoardDto>())!;
    }

    private async Task<ListDto> CreateListAsync(int boardId, string title)
    {
        var res = await _client.PostAsJsonAsync($"/api/boards/{boardId}/lists", new { title });
        return (await res.Content.ReadFromJsonAsync<ListDto>())!;
    }

    [Test]
    public async Task Posting_a_list_creates_it_at_the_next_position()
    {
        var board = await CreateBoardAsync("Sprint 1");

        var response = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/lists", new { title = "To do" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await response.Content.ReadFromJsonAsync<ListDto>();
        Assert.That(created!.Title, Is.EqualTo("To do"));
        Assert.That(created.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Posting_a_blank_list_title_is_rejected()
    {
        var board = await CreateBoardAsync("Sprint 1");
        var response = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/lists", new { title = "   " });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_list_title_is_rejected()
    {
        var board = await CreateBoardAsync("Sprint 1");
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/lists", new { title = new string('x', 201) });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_a_list_to_a_missing_board_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/boards/9999/lists", new { title = "X" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task Board_detail_includes_its_lists_in_order()
    {
        var board = await CreateBoardAsync("Sprint");
        await CreateListAsync(board.Id, "To do");
        await CreateListAsync(board.Id, "Doing");

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");

        Assert.That(detail!.Title, Is.EqualTo("Sprint"));
        Assert.That(detail.Lists.Select(l => l.Title), Is.EqualTo(new[] { "To do", "Doing" }));
        Assert.That(detail.Lists.Select(l => l.Position), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public async Task Board_detail_for_a_missing_board_is_404()
    {
        var res = await _client.GetAsync("/api/boards/9999");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test]
    public async Task Put_renames_a_list()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "Old");

        var put = await _client.PutAsJsonAsync($"/api/lists/{list.Id}", new { title = "New" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Single().Title, Is.EqualTo("New"));
    }

    [Test]
    public async Task Put_rejects_a_blank_list_title()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "Old");

        var put = await _client.PutAsJsonAsync($"/api/lists/{list.Id}", new { title = "  " });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Put_to_a_missing_list_is_404()
    {
        var put = await _client.PutAsJsonAsync("/api/lists/9999", new { title = "X" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_removes_a_list()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "Temp");

        var del = await _client.DeleteAsync($"/api/lists/{list.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists, Is.Empty);
    }

    [Test]
    public async Task Delete_a_missing_list_is_404()
    {
        var del = await _client.DeleteAsync("/api/lists/9999");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
