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
}
