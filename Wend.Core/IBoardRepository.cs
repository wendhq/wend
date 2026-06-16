namespace Wend.Core;

/// <summary>
/// Persistence seam for the board domain. Slice 1 implements this with EF Core → SQLite;
/// the API and services depend only on this interface, so storage can be swapped without
/// touching board logic.
/// <para>
/// Members are added per feature, test-first, as Slice 1 is built (boards, lists, cards,
/// move, checklist, labels, soft-delete/restore).
/// </para>
/// </summary>
public interface IBoardRepository
{
}
