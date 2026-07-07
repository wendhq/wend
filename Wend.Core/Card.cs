namespace Wend.Core;

/// <summary>A card within a list — the unit of work. Carries its ordering position and the
/// lifecycle timestamps later plans use (Done / Archive / soft-delete); only the core fields
/// are written in Plan 3.</summary>
public class Card
{
    public int Id { get; set; }
    public int ListId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }   // Plan 6 (Done)
    public DateTime? ArchivedAt { get; set; }    // later slice (Archive)
    public DateTime? DeletedAt { get; set; }     // Plan 7 (undo-delete)

    // A card's checklist items. Required FK on ChecklistItem.CardId → deleting a card cascades to them.
    public ICollection<ChecklistItem> ChecklistItems { get; set; } = [];
}
