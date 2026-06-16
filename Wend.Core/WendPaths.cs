namespace Wend.Core;

/// <summary>Well-known on-disk locations for Wend, under the user's local AppData.</summary>
public static class WendPaths
{
    /// <summary>
    /// The SQLite database file: <c>%LOCALAPPDATA%\Wend\data.db</c>. Creates the folder if needed.
    /// Living in AppData (not the app folder) keeps the database out of source control and lets it
    /// survive rebuilds.
    /// </summary>
    public static string DefaultDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wend");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "data.db");
    }
}
