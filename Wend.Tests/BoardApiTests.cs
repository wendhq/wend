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
}
