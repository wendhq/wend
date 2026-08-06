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
