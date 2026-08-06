using System.Text.Json.Serialization;

namespace Wend.Core;

/// <summary>A list (column) within a board — holds its ordering position and its cards.</summary>
public class List
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Title { get; set; } = "";
    public int Position { get; set; }

    // Upward navigation — exists so ownership (Board.OwnerId) is expressible in a query.
    // [JsonIgnore] because EF's fixup populates it whenever the board is in the same context,
    // which would make Board.Lists → List.Board → Board.Lists a serialisation cycle on the wire.
    [JsonIgnore]
    public Board Board { get; set; } = null!;

    // A list's cards. Required FK on Card.ListId → deleting a list cascades to them.
    public ICollection<Card> Cards { get; set; } = [];
}
