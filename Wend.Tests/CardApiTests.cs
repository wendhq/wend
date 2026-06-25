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
    private record CardDetailDto(int Id, int ListId, string ListTitle, string Title, string? Description, string? DueDate, int Position, DateTime? CompletedAt);

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
    
    [Test]
    public async Task Get_card_returns_its_detail_with_the_list_name()
    {
        var board = await CreateBoardAsync("Sprint");
        var list = await CreateListAsync(board.Id, "To do");
        var card = await CreateCardAsync(list.Id, "Email Rebecka");

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");

        Assert.That(detail!.Title, Is.EqualTo("Email Rebecka"));
        Assert.That(detail.ListId, Is.EqualTo(list.Id));
        Assert.That(detail.ListTitle, Is.EqualTo("To do"));
        Assert.That(detail.Description, Is.Null);
        Assert.That(detail.DueDate, Is.Null);
    }

    [Test]
    public async Task Get_a_missing_card_is_404()
    {
        var res = await _client.GetAsync("/api/cards/9999");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

        [Test]
    public async Task Put_edits_a_cards_title_notes_and_due_date()
    {
        var listId = await NewListAsync();
        var card = await CreateCardAsync(listId, "Old");

        var put = await _client.PutAsJsonAsync($"/api/cards/{card.Id}",
            new { title = "New", description = "Some notes", dueDate = "2026-06-25" });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");
        Assert.That(detail!.Title, Is.EqualTo("New"));
        Assert.That(detail.Description, Is.EqualTo("Some notes"));
        Assert.That(detail.DueDate, Is.EqualTo("2026-06-25"));
    }

    [Test]
    public async Task Put_rejects_a_blank_card_title()
    {
        var listId = await NewListAsync();
        var card = await CreateCardAsync(listId, "Old");

        var put = await _client.PutAsJsonAsync($"/api/cards/{card.Id}", new { title = "  ", description = (string?)null, dueDate = (string?)null });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Put_to_a_missing_card_is_404()
    {
        var put = await _client.PutAsJsonAsync("/api/cards/9999", new { title = "X", description = (string?)null, dueDate = (string?)null });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_removes_a_card()
    {
        var board = await CreateBoardAsync("B");
        var list = await CreateListAsync(board.Id, "L");
        var card = await CreateCardAsync(list.Id, "Temp");

        var del = await _client.DeleteAsync($"/api/cards/{card.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Single().Cards, Is.Empty);
    }

    [Test]
    public async Task Delete_a_missing_card_is_404()
    {
        var del = await _client.DeleteAsync("/api/cards/9999");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task Moving_a_card_within_its_list_reorders_it()
    {
        var board = await CreateBoardAsync("Sprint");
        var list = await CreateListAsync(board.Id, "To do");
        var a = await CreateCardAsync(list.Id, "A");  // 0
        await CreateCardAsync(list.Id, "B");          // 1
        await CreateCardAsync(list.Id, "C");          // 2

        var move = await _client.PutAsJsonAsync($"/api/cards/{a.Id}/move", new { listId = list.Id, position = 2 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Single().Cards.Select(c => c.Title), Is.EqualTo(new[] { "B", "C", "A" }));
    }

    [Test]
    public async Task Moving_a_card_to_another_list_appends_it_there()
    {
        var board = await CreateBoardAsync("Sprint");
        var todo = await CreateListAsync(board.Id, "To do");
        var doing = await CreateListAsync(board.Id, "Doing");
        var a = await CreateCardAsync(todo.Id, "A");
        await CreateCardAsync(doing.Id, "X");

        var move = await _client.PutAsJsonAsync($"/api/cards/{a.Id}/move", new { listId = doing.Id, position = 99 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<BoardWithCardsDto>($"/api/boards/{board.Id}");
        Assert.That(detail!.Lists.Single(l => l.Title == "To do").Cards, Is.Empty);
        Assert.That(detail.Lists.Single(l => l.Title == "Doing").Cards.Select(c => c.Title),
            Is.EqualTo(new[] { "X", "A" }));
    }

    [Test]
    public async Task Moving_a_missing_card_is_404()
    {
        var board = await CreateBoardAsync("Sprint");
        var list = await CreateListAsync(board.Id, "To do");

        var move = await _client.PutAsJsonAsync("/api/cards/9999/move", new { listId = list.Id, position = 0 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Moving_a_card_to_another_board_is_400()
    {
        var boardA = await CreateBoardAsync("A");
        var listA = await CreateListAsync(boardA.Id, "A-list");
        var card = await CreateCardAsync(listA.Id, "Card");
        var boardB = await CreateBoardAsync("B");
        var listB = await CreateListAsync(boardB.Id, "B-list");

        var move = await _client.PutAsJsonAsync($"/api/cards/{card.Id}/move", new { listId = listB.Id, position = 0 });
        Assert.That(move.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task A_new_cards_completedAt_is_null()
    {
        var listId = await NewListAsync();
        var card = await CreateCardAsync(listId, "Fresh");

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");
        Assert.That(detail!.CompletedAt, Is.Null);
    }

    [Test]
    public async Task Completing_a_card_sets_its_completedAt()
    {
        var listId = await NewListAsync();
        var card = await CreateCardAsync(listId, "Ship it");

        var done = await _client.PutAsJsonAsync($"/api/cards/{card.Id}/complete", new { completed = true });
        Assert.That(done.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");
        Assert.That(detail!.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task Un_completing_a_card_clears_its_completedAt()
    {
        var listId = await NewListAsync();
        var card = await CreateCardAsync(listId, "Ship it");
        await _client.PutAsJsonAsync($"/api/cards/{card.Id}/complete", new { completed = true });

        var undone = await _client.PutAsJsonAsync($"/api/cards/{card.Id}/complete", new { completed = false });
        Assert.That(undone.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var detail = await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}");
        Assert.That(detail!.CompletedAt, Is.Null);
    }

    [Test]
    public async Task Completing_a_missing_card_is_404()
    {
        var res = await _client.PutAsJsonAsync("/api/cards/9999/complete", new { completed = true });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
