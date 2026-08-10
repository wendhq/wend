namespace Wend.Core;

/// <summary>
/// Display names are user-controlled content that Slice 2b will render on *other users'* boards,
/// so a bad value is a stored-XSS vector across a trust boundary. Cleaning happens once, at write
/// time; escaping still happens at every interpolation. Both, not either.
/// </summary>
public static class DisplayNames
{
    /// <summary>Matches the column cap configured in WendDbContext.</summary>
    public const int MaxLength = 100;

    /// <summary>Strips control characters (including newlines, which break log lines) and trims.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var kept = value.Where(c => !char.IsControl(c)).ToArray();
        return new string(kept).Trim();
    }
}
