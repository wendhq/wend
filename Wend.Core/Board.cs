namespace Wend.Core;

/// <summary>A board — the top-level container for its lists and cards.</summary>
public class Board
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    // A board's lists. Required FK on List.BoardId → deleting a board cascades to them.
    public ICollection<List> Lists { get; set; } = [];
}
