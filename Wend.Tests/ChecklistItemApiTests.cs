using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class ChecklistItemApiTests
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
    private record ItemDto(int Id, string Text, DateTime? CheckedAt, int Position);
    private record CardDetailDto(int Id, string Title, List<ItemDto> Items);
    private record CardSummaryDto(int Id, string Title, int ChecklistDone, int ChecklistTotal);
    private record ListWithCardsDto(int Id, string Title, List<CardSummaryDto> Cards);
    private record BoardWithCardsDto(int Id, string Title, List<ListWithCardsDto> Lists);

    private async Task<BoardDto> CreateBoardAsync(string title)
    {
        var res = await _client.PostAsJsonAsync("/api/boards", new { title });
        return (await res.Content.ReadFromJsonAsync<BoardDto>())!;
    }

    private async Task<int> NewCardAsync()
    {
        var board = await CreateBoardAsync("Board");
        var listRes = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/lists", new { title = "List" });
        var list = (await listRes.Content.ReadFromJsonAsync<ListDto>())!;
        var cardRes = await _client.PostAsJsonAsync($"/api/lists/{list.Id}/cards", new { title = "Card" });
        return (await cardRes.Content.ReadFromJsonAsync<CardDto>())!.Id;
    }

    private async Task<ItemDto> AddItemAsync(int cardId, string text)
    {
        var res = await _client.PostAsJsonAsync($"/api/cards/{cardId}/checklist-items", new { text });
        return (await res.Content.ReadFromJsonAsync<ItemDto>())!;
    }

    [Test]
    public async Task Posting_an_item_creates_it_at_the_next_position()
    {
        var cardId = await NewCardAsync();

        var response = await _client.PostAsJsonAsync($"/api/cards/{cardId}/checklist-items", new { text = "Write intro" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var created = (await response.Content.ReadFromJsonAsync<ItemDto>())!;
        Assert.That(created.Text, Is.EqualTo("Write intro"));
        Assert.That(created.Position, Is.EqualTo(0));
    }

    [Test]
    public async Task Posting_a_blank_item_text_is_rejected()
    {
        var cardId = await NewCardAsync();
        var response = await _client.PostAsJsonAsync($"/api/cards/{cardId}/checklist-items", new { text = "   " });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Posting_an_item_to_a_missing_card_is_404()
    {
        var response = await _client.PostAsJsonAsync("/api/cards/9999/checklist-items", new { text = "X" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Card_detail_nests_items_in_order_and_rename_shows_there()
    {
        var cardId = await NewCardAsync();
        var first = await AddItemAsync(cardId, "First");
        await AddItemAsync(cardId, "Second");

        var rename = await _client.PutAsJsonAsync($"/api/checklist-items/{first.Id}", new { text = "Renamed" });
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{cardId}"))!;
        Assert.That(detail.Items.Select(i => i.Text), Is.EqualTo(new[] { "Renamed", "Second" }));
        Assert.That(detail.Items.Select(i => i.Position), Is.EqualTo(new[] { 0, 1 }));
    }
}
