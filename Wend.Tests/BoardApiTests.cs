using System.Net;
using System.Net.Http.Json;
using Wend.Core;

namespace Wend.Tests;

public class BoardApiTests
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

    [Test]
    public async Task Boards_start_empty()
    {
        var boards = await _client.GetFromJsonAsync<List<Board>>("/api/boards");

        Assert.That(boards, Is.Empty);
    }
    
    [Test]
    public async Task Posting_a_board_creates_it()
    {
        var response = await _client.PostAsJsonAsync("/api/boards", new { title = "Sprint 1" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var boards = await _client.GetFromJsonAsync<List<Board>>("/api/boards");
        Assert.That(boards!.Single().Title, Is.EqualTo("Sprint 1"));
    }

    [Test]
    public async Task Posting_a_blank_title_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/boards", new { title = "   " });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_title_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/boards", new { title = new string('x', 201) });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [Test]
    public async Task Get_one_returns_the_board_or_404()
    {
        var created = await (await _client.PostAsJsonAsync("/api/boards", new { title = "A" }))
            .Content.ReadFromJsonAsync<Board>();

        var found = await _client.GetAsync($"/api/boards/{created!.Id}");
        Assert.That(found.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var missing = await _client.GetAsync("/api/boards/9999");
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Put_renames_the_board()
    {
        var created = await (await _client.PostAsJsonAsync("/api/boards", new { title = "Old" }))
            .Content.ReadFromJsonAsync<Board>();

        var put = await _client.PutAsJsonAsync($"/api/boards/{created!.Id}", new { title = "New" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var board = await _client.GetFromJsonAsync<Board>($"/api/boards/{created.Id}");
        Assert.That(board!.Title, Is.EqualTo("New"));
    }

    [Test]
    public async Task Delete_removes_the_board()
    {
        var created = await (await _client.PostAsJsonAsync("/api/boards", new { title = "Temp" }))
            .Content.ReadFromJsonAsync<Board>();

        var deleted = await _client.DeleteAsync($"/api/boards/{created!.Id}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var boards = await _client.GetFromJsonAsync<List<Board>>("/api/boards");
        Assert.That(boards, Is.Empty);
    }
}
