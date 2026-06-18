using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Wend.Tests;

/// <summary>
/// Boots the real app against a throwaway SQLite file via the Wend:DbPath seam, so tests
/// never touch the real %LOCALAPPDATA%\Wend\data.db. Each instance gets its own database.
/// </summary>
public sealed class WendApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"wend-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Wend:DbPath", _dbPath);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Best-effort: the SQLite connection pool may still hold the file briefly on
        // Windows. If the delete fails, leave it — it's in the OS temp folder.
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }
}
