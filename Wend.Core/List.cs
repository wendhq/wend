namespace Wend.Core;

/// <summary>A list (column) within a board — holds its ordering position and its cards.</summary>
public class List
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Title { get; set; } = "";
    public int Position { get; set; }

    // A list's cards. Required FK on Card.ListId → deleting a list cascades to them.
    public ICollection<Card> Cards { get; set; } = [];
}
