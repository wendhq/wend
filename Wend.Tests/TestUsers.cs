using Wend.Core;

namespace Wend.Tests;

/// <summary>
/// Seeds WendUser rows directly through the context. Identity's services are not wired in this
/// plan (that is Plan 3), so there is no UserManager — and none is needed to own a board.
/// </summary>
public static class TestUsers
{
    public static async Task<string> SeedAsync(WendDbContext db, string? email = null)
    {
        var id = Guid.NewGuid().ToString();
        email ??= $"{id}@example.test";
        db.Users.Add(new WendUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Test User",
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await db.SaveChangesAsync();
        return id;
    }
}
