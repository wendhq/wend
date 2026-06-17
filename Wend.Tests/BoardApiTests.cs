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
}
