namespace Wend.Core;

/// <summary>Join row: a card carries a label. Composite key (CardId, LabelId); deleting either
/// the card or the label cascades this row away.</summary>
public class CardLabel
{
    public int CardId { get; set; }
    public int LabelId { get; set; }
}
