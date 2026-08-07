using System.Text.Json.Serialization;

namespace Wend.Core;

/// <summary>A board-scoped, reusable label — a {name, colour} tag any card on the board can
/// carry (many-to-many via CardLabel). Colour is a palette key, not a hex value.</summary>
public class Label
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "";

    // Upward navigation — ownership is reached via Board → OwnerId.
    // [JsonIgnore]: EF fixup would otherwise make Board.Labels → Label.Board a serialisation cycle.
    [JsonIgnore]
    public Board Board { get; set; } = null!;
}
