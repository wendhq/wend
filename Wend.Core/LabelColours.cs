namespace Wend.Core;

/// <summary>The curated label palette. Colours live in CSS; the database stores only these keys,
/// validated here so an unknown colour can never be persisted.</summary>
public static class LabelColours
{
    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { "mint", "cyan", "amber", "rose", "lilac", "slate" };

    public static bool IsValid(string? colour) => colour is not null && All.Contains(colour);
}
