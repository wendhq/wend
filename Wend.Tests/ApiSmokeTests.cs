using System.Net;


namespace Wend.Tests;

/// <summary>
/// Smoke tests: the API host boots and serves both the API and the frontend shell.
/// Proves the scaffold is wired end to end before Slice 1 features land.
/// </summary>
public class ApiSmokeTests
{
    [Test]
    public async Task Health_endpoint_returns_ok()
    {
        await using var factory = new WendApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Root_serves_the_frontend_shell()
    {
        await using var factory = new WendApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
    }

    [Test]
    public async Task An_unmatched_api_route_is_404_not_the_shell()
    {
        await using var factory = new WendApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/definitely-not-a-route");

        // Without the API catch-all this falls through to MapFallbackToFile and returns the SPA
        // shell at 200, so a typo'd route reads as success and api() chokes parsing HTML as JSON.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.Not.EqualTo("text/html"));
    }

    [Test]
    public async Task Static_files_are_no_cache_in_development()
    {
        await using var factory = new WendApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/css/app.css");

        // Without the header the browser applies its own heuristic and a normal reload keeps
        // serving an earlier session's JS/CSS. Two "bugs" in the 2026-07-08 accessibility sweep
        // were that, so the dev header is worth a guard.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
    }

    [Test]
    public async Task The_shell_is_no_cache_in_development()
    {
        await using var factory = new WendApiFactory();
        using var client = factory.CreateClient();

        // MapFallbackToFile serves index.html through its own static-file pipeline, so it takes the
        // options separately — the middleware's copy never reaches it.
        var response = await client.GetAsync("/boards/42");

        Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
    }

    [Test]
    public async Task An_unmatched_non_api_route_still_serves_the_shell()
    {
        await using var factory = new WendApiFactory();
        using var client = factory.CreateClient();

        // Client-side routing depends on deep links falling through to the shell.
        var response = await client.GetAsync("/boards/42");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
    }
}
