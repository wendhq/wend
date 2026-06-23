using System.Net;
using System.Net.Http.Json;

namespace Wend.Tests;

public class LabelApiTests
{
    private WendApiFactory _factory = null!;
    private HttpClient _client = null!;
    private record CardSummaryDto(int Id, string Title, string? DueDate, int Position, List<int> LabelIds);
    private record ListWithCardsDto(int Id, string Title, int Position, List<CardSummaryDto> Cards);
    private record BoardDetailDto(int Id, string Title, List<LabelDto> Labels, List<ListWithCardsDto> Lists);

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
    private record CardDetailDto(int Id, int ListId, string ListTitle, int BoardId, string Title, string? Description, string? DueDate, int Position, List<LabelDto> Labels);

    private async Task<BoardDto> CreateBoardAsync(string title) =>
        (await (await _client.PostAsJsonAsync("/api/boards", new { title })).Content.ReadFromJsonAsync<BoardDto>())!;

    private async Task<ListDto> CreateListAsync(int boardId, string title) =>
        (await (await _client.PostAsJsonAsync($"/api/boards/{boardId}/lists", new { title })).Content.ReadFromJsonAsync<ListDto>())!;

    private async Task<CardDto> CreateCardAsync(int listId, string title) =>
        (await (await _client.PostAsJsonAsync($"/api/lists/{listId}/cards", new { title })).Content.ReadFromJsonAsync<CardDto>())!;

    private async Task<LabelDto> CreateLabelAsync(int boardId, string name, string colour) =>
        (await (await _client.PostAsJsonAsync($"/api/boards/{boardId}/labels", new { name, colour })).Content.ReadFromJsonAsync<LabelDto>())!;

    [Test]
    public async Task Card_detail_includes_board_id_and_attached_labels()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");
        await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });

        var detail = (await _client.GetFromJsonAsync<CardDetailDto>($"/api/cards/{card.Id}"))!;

        Assert.That(detail.BoardId, Is.EqualTo(board.Id));
        Assert.That(detail.Labels.Select(l => l.Name), Is.EqualTo(new[] { "Urgent" }));
    } 
    
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
        var saved = palette!.Single();
        Assert.That(saved.Name, Is.EqualTo("New"));
        Assert.That(saved.Colour, Is.EqualTo("lilac"));
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
    
    [Test]
    public async Task Attaching_a_label_to_a_card_succeeds_and_is_idempotent()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");

        var first = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var again = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); // idempotent
    }

    [Test]
    public async Task Attaching_a_label_from_another_board_is_rejected()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var other = await CreateBoardAsync("Other");
        var foreign = await CreateLabelAsync(other.Id, "Foreign", "mint");

        var res = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = foreign.Id });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Attaching_to_a_missing_card_or_label_is_404()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");

        var missingCard = await _client.PostAsJsonAsync("/api/cards/9999/labels", new { labelId = label.Id });
        Assert.That(missingCard.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var missingLabel = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = 9999 });
        Assert.That(missingLabel.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Detaching_is_always_204_including_when_not_attached()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");

        var notAttached = await _client.DeleteAsync($"/api/cards/{card.Id}/labels/{label.Id}");
        Assert.That(notAttached.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });
        var attachedThenRemoved = await _client.DeleteAsync($"/api/cards/{card.Id}/labels/{label.Id}");
        Assert.That(attachedThenRemoved.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
    [Test]
    public async Task Board_detail_includes_the_palette_and_each_cards_label_ids()
    {
        var board = await CreateBoardAsync("Board");
        var list = await CreateListAsync(board.Id, "List");
        var card = await CreateCardAsync(list.Id, "Card");
        var label = await CreateLabelAsync(board.Id, "Urgent", "rose");
        await _client.PostAsJsonAsync($"/api/cards/{card.Id}/labels", new { labelId = label.Id });

        var detail = (await _client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}"))!;

        Assert.That(detail.Labels.Select(l => l.Name), Is.EqualTo(new[] { "Urgent" }));
        Assert.That(detail.Lists.Single().Cards.Single().LabelIds, Is.EqualTo(new[] { label.Id }));
    }
}
