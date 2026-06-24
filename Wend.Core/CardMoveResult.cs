namespace Wend.Core;

/// <summary>Outcome of a card move, mapped to an HTTP status by the endpoint.</summary>
public enum CardMoveResult
{
    Moved,
    NotFound,    // the card or the target list doesn't exist
    CrossBoard,  // the target list belongs to a different board
}
