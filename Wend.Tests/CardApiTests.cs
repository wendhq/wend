using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class CardApiTests
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
    private record CardDto(int Id, string Title, int Position);
    private record CardSummaryDto(int Id, string Title, string? DueDate, int Position);
    private record ListWithCardsDto(int Id, string Title, int Position, List<CardSummaryDto> Cards);
    private record BoardWithCardsDto(int Id, string Title, List<ListWithCardsDto> Lists);
    private record CardDetailDto(int Id, int ListId, string ListTitle, string Title, string? Description, string? DueDate, int Position);

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

    private async Task<CardDto> CreateCardAsync(int listId, string title)
    {
        var res = await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title });
        return (await res.Content.ReadFromJsonAsync<CardDto>())!;
    }

    private async Task<int> NewListAsync()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        return list.Id;
    }

    [Test]
    public async Task Posting_a_card_creates_it_at_the_next_position()
    {
        var listId = await NewListAsync();

        var response = await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title = "Email Rebecka" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = await response.Content.ReadFromJsonAsync<CardDto>();
        Assert.That(created!.Title, Is.EqualTo("Email Rebecka"));
        Assert.That(created.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Posting_a_blank_card_title_is_rejected()
    {
        var listId = await NewListAsync();
        var response = await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title = "   " });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_over_long_card_title_is_rejected()
    {
        var listId = await NewListAsync();
        var response = await _client.PostAsJsonAsync(
            $"/api/lists/{listId}/cards", new { title = new string('x', 201) });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_a_card_to_a_missing_list_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/lists/9999/cards", new { title = "X" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Board_detail_nests_each_lists_cards_in_order()
    {
        var board = await CreateBoardAsync("Sprint");
        var list = await CreateListAsync(board.Id, "To do");
        await CreateCardAsync(list.Id, "First");
        await CreateCardAsync(list.Id, "Second");

        var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");

        var cards = detail!.Lists.Single().Cards;
        Assert.That(cards.Select(c => c.Title), Is.EqualTo(new[] { "First", "Second" }));
        Assert.That(cards.Select(c => c.Position), Is.EqualTo(new[] { 0, 1 }));
    }
}
