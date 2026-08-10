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

    /// <summary>
    /// Where the dev email sender writes confirmation links: <c>%LOCALAPPDATA%\Wend\auth-emails.log</c>.
    /// Not a mailbox — a developer's click-through log. It contains live tokens, so it stays out of
    /// the repo (AppData, like the database) and is never shipped to a real environment.
    /// </summary>
    public static string AuthEmailLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wend");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "auth-emails.log");
    }
}
