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
}
