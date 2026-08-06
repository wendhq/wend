using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Wend.Api;
using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// The per-user boundary: user A can never see or touch user B's data, and an anonymous request
/// gets 401. This is the highest-value test set in Slice 2a — everything else is plumbing.
/// </summary>
public class OwnershipTests
{
    [Test]
    public void No_current_user_means_no_user_id()
    {
        Assert.That(new NullCurrentUser().UserId, Is.Null);
    }

    [Test]
    public void The_api_factory_seeds_its_default_user()
    {
        using var factory = new WendApiFactory();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WendDbContext>();
        var user = db.Users.SingleOrDefault(u => u.Id == factory.DefaultUserId);

        Assert.That(user, Is.Not.Null);
        Assert.That(factory.CurrentUser.UserId, Is.EqualTo(factory.DefaultUserId));
    }

    [Test]
    public async Task A_board_is_invisible_to_another_user()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" });
        var board = await created.Content.ReadFromJsonAsync<Board>();

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);

        Assert.That((await client.GetAsync($"/api/boards/{board!.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.DeleteAsync($"/api/boards/{board.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/boards/{board.Id}", new { Title = "Yours" })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await (await client.GetAsync("/api/boards")).Content
            .ReadFromJsonAsync<List<Board>>(), Is.Empty);
    }

    [Test]
    public async Task Anonymous_requests_are_unauthorized()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();
        factory.CurrentUser.UserId = null;   // after CreateClient, which sets the default user

        Assert.That((await client.GetAsync("/api/boards")).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_list_is_invisible_to_another_user()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();
        var list = await (await client.PostAsJsonAsync($"/api/boards/{board!.Id}/lists", new { Title = "To do" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);
        Assert.That(factory.CurrentUser.UserId, Is.Not.EqualTo(factory.DefaultUserId));

        Assert.That((await client.PutAsJsonAsync($"/api/lists/{list!.Id}", new { Title = "Theirs" })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/lists/{list.Id}/move", new { Position = 0 })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.DeleteAsync($"/api/lists/{list.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Posting_a_list_into_another_users_board_is_404()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);

        var posted = await client.PostAsJsonAsync($"/api/boards/{board!.Id}/lists", new { Title = "Intruder" });
        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_card_is_invisible_to_another_user()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();
        var list = await (await client.PostAsJsonAsync($"/api/boards/{board!.Id}/lists", new { Title = "To do" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();
        var card = await (await client.PostAsJsonAsync($"/api/lists/{list!.Id}/cards", new { Title = "A card" }))
            .Content.ReadFromJsonAsync<Card>();
        await client.DeleteAsync($"/api/cards/{card!.Id}");   // soft-deleted, restorable by its owner

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);
        Assert.That(factory.CurrentUser.UserId, Is.Not.EqualTo(factory.DefaultUserId));

        // Restore is the important one: it reaches soft-deleted rows via IgnoreQueryFilters(),
        // which must NOT widen the boundary. Ownership is a Where clause, so it survives.
        Assert.That((await client.PostAsync($"/api/cards/{card.Id}/restore", null)).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync($"/api/cards/{card.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/cards/{card.Id}",
            new { Title = "Theirs", Description = (string?)null, DueDate = (DateOnly?)null })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/cards/{card.Id}/complete", new { Completed = true })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.DeleteAsync($"/api/cards/{card.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));

        // ...and its owner can still restore it, so the boundary blocked the intruder, not the feature.
        factory.CurrentUser.UserId = factory.DefaultUserId;
        Assert.That((await client.PostAsync($"/api/cards/{card.Id}/restore", null)).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Posting_a_card_into_another_users_list_is_404()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();
        var list = await (await client.PostAsJsonAsync($"/api/boards/{board!.Id}/lists", new { Title = "To do" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);

        var posted = await client.PostAsJsonAsync($"/api/lists/{list!.Id}/cards", new { Title = "Intruder" });
        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Moving_a_card_into_another_users_list_is_404_not_400()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        // User A's board, list and card.
        var boardA = await (await client.PostAsJsonAsync("/api/boards", new { Title = "A" }))
            .Content.ReadFromJsonAsync<Board>();
        var listA = await (await client.PostAsJsonAsync($"/api/boards/{boardA!.Id}/lists", new { Title = "A list" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();
        var cardA = await (await client.PostAsJsonAsync($"/api/lists/{listA!.Id}/cards", new { Title = "A card" }))
            .Content.ReadFromJsonAsync<Card>();

        // User B's own board and list.
        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);
        Assert.That(factory.CurrentUser.UserId, Is.Not.EqualTo(factory.DefaultUserId));
        var boardB = await (await client.PostAsJsonAsync("/api/boards", new { Title = "B" }))
            .Content.ReadFromJsonAsync<Board>();
        var listB = await (await client.PostAsJsonAsync($"/api/boards/{boardB!.Id}/lists", new { Title = "B list" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();

        // B moving A's card anywhere: the card is not B's, so it is simply missing.
        var moveAsB = await client.PutAsJsonAsync($"/api/cards/{cardA!.Id}/move",
            new { ListId = listB!.Id, Position = 0 });
        Assert.That(moveAsB.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // A moving their own card into B's list: the target is not A's, so it is missing too —
        // 404, never 400, which would confirm B's list exists on another board.
        factory.CurrentUser.UserId = factory.DefaultUserId;
        var moveAsA = await client.PutAsJsonAsync($"/api/cards/{cardA.Id}/move",
            new { ListId = listB.Id, Position = 0 });
        Assert.That(moveAsA.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_label_is_invisible_to_another_user()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();
        var label = await (await client.PostAsJsonAsync($"/api/boards/{board!.Id}/labels",
            new { Name = "Urgent", Colour = "rose" })).Content.ReadFromJsonAsync<LabelDto>();

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);
        Assert.That(factory.CurrentUser.UserId, Is.Not.EqualTo(factory.DefaultUserId));

        Assert.That((await client.GetAsync($"/api/boards/{board.Id}/labels")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/labels/{label!.Id}",
            new { Name = "Theirs", Colour = "mint" })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.DeleteAsync($"/api/labels/{label.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_label_cannot_be_attached_to_another_users_card()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        // User A's card.
        var boardA = await (await client.PostAsJsonAsync("/api/boards", new { Title = "A" }))
            .Content.ReadFromJsonAsync<Board>();
        var listA = await (await client.PostAsJsonAsync($"/api/boards/{boardA!.Id}/lists", new { Title = "A list" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();
        var cardA = await (await client.PostAsJsonAsync($"/api/lists/{listA!.Id}/cards", new { Title = "A card" }))
            .Content.ReadFromJsonAsync<Card>();

        // User B's own label.
        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);
        var boardB = await (await client.PostAsJsonAsync("/api/boards", new { Title = "B" }))
            .Content.ReadFromJsonAsync<Board>();
        var labelB = await (await client.PostAsJsonAsync($"/api/boards/{boardB!.Id}/labels",
            new { Name = "Theirs", Colour = "cyan" })).Content.ReadFromJsonAsync<LabelDto>();

        // B attaching their label to A's card: the card is missing to them.
        var attached = await client.PostAsJsonAsync($"/api/cards/{cardA!.Id}/labels", new { LabelId = labelB!.Id });
        Assert.That(attached.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // And A cannot reach B's label either.
        factory.CurrentUser.UserId = factory.DefaultUserId;
        var reverse = await client.PostAsJsonAsync($"/api/cards/{cardA.Id}/labels", new { LabelId = labelB.Id });
        Assert.That(reverse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_checklist_item_is_invisible_to_another_user()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();
        var list = await (await client.PostAsJsonAsync($"/api/boards/{board!.Id}/lists", new { Title = "To do" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();
        var card = await (await client.PostAsJsonAsync($"/api/lists/{list!.Id}/cards", new { Title = "A card" }))
            .Content.ReadFromJsonAsync<Card>();
        var item = await (await client.PostAsJsonAsync($"/api/cards/{card!.Id}/checklist-items",
            new { Text = "Step one" })).Content.ReadFromJsonAsync<ChecklistItem>();
        await client.DeleteAsync($"/api/checklist-items/{item!.Id}");  // soft-deleted, restorable by its owner

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);
        Assert.That(factory.CurrentUser.UserId, Is.Not.EqualTo(factory.DefaultUserId));

        // Restore is the important one: it reaches soft-deleted rows through the ignore-filtered
        // ownership helper, which must not widen the boundary while it widens the delete state.
        Assert.That((await client.PostAsync($"/api/checklist-items/{item.Id}/restore", null)).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/checklist-items/{item.Id}",
            new { Text = "Theirs" })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/checklist-items/{item.Id}/check",
            new { Checked = true })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.PutAsJsonAsync($"/api/checklist-items/{item.Id}/move",
            new { Position = 0 })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.DeleteAsync($"/api/checklist-items/{item.Id}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));

        // ...and its owner can still restore it, so the boundary blocked the intruder, not the feature.
        factory.CurrentUser.UserId = factory.DefaultUserId;
        Assert.That((await client.PostAsync($"/api/checklist-items/{item.Id}/restore", null)).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Posting_a_checklist_item_into_another_users_card_is_404()
    {
        using var factory = new WendApiFactory();
        var client = factory.CreateClient();

        var board = await (await client.PostAsJsonAsync("/api/boards", new { Title = "Mine" }))
            .Content.ReadFromJsonAsync<Board>();
        var list = await (await client.PostAsJsonAsync($"/api/boards/{board!.Id}/lists", new { Title = "To do" }))
            .Content.ReadFromJsonAsync<Wend.Core.List>();
        var card = await (await client.PostAsJsonAsync($"/api/lists/{list!.Id}/cards", new { Title = "A card" }))
            .Content.ReadFromJsonAsync<Card>();

        factory.CurrentUser.UserId = await SeedOtherUserAsync(factory);

        var posted = await client.PostAsJsonAsync($"/api/cards/{card!.Id}/checklist-items",
            new { Text = "Intruder" });
        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Seeds a second user. Callers assign the result to factory.CurrentUser.UserId — never call
    /// CreateClient() again afterwards, or ConfigureClient silently reverts to the default user.
    /// </summary>
    private static async Task<string> SeedOtherUserAsync(WendApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await TestUsers.SeedAsync(scope.ServiceProvider.GetRequiredService<WendDbContext>());
    }
}
