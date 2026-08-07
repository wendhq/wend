namespace Wend.Core;

/// <summary>A board — the top-level container for its lists and cards.</summary>
public class Board
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    // The owning account. Required: every board belongs to exactly one user, and deleting that
    // user cascades through boards → lists → cards (the GDPR erasure path). Required rather than
    // nullable because an ownerless board should not be a state the system can represent.
    public string OwnerId { get; set; } = "";

    // A board's lists. Required FK on List.BoardId → deleting a board cascades to them.
    public ICollection<List> Lists { get; set; } = [];

    // A board's labels. Required FK on Label.BoardId → deleting a board cascades to them.
    public ICollection<Label> Labels { get; set; } = [];
}
