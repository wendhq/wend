namespace Wend.Core;

/// <summary>One entry in a card's checklist — a miniature Card: the same 0-based gapless
/// Position algebra and lifecycle idioms (CheckedAt mirrors Card.CompletedAt, DeletedAt
/// mirrors the soft delete). Checked and unchecked items share ONE position sequence, so
/// un-checking an item drops it back exactly where it lived.</summary>
public class ChecklistItem
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public string Text { get; set; } = "";
    public int Position { get; set; }
    public DateTime? CheckedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
