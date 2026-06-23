namespace Wend.Core;

/// <summary>A board-scoped, reusable label — a {name, colour} tag any card on the board can
/// carry (many-to-many via CardLabel). Colour is a palette key, not a hex value.</summary>
public class Label
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "";
}
