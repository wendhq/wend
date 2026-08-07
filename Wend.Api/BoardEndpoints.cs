using Wend.Core;

namespace Wend.Api;

public static class BoardEndpoints
{
    private const int MaxTitleLength = 200;

    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        // Each handler opens with the same guard — 28 of them across the five endpoint files.
        // Reviewed at that count and kept per-handler rather than lifted into a
        // route-group filter: it is compile-enforced (ownerId only exists via this pattern match),
        // and Plan 3 puts real authentication in front of these routes, where a group filter would
        // have to be unpicked to let /api/auth/* through. Decided at plan time — please don't reopen.
        group.MapGet("/", async (IBoardRepository repo, ICurrentUser currentUser) =>
        {
            if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
            return Results.Ok(await repo.GetBoardsAsync(ownerId));
        });

        group.MapPost("/", async (CreateBoardRequest req, IBoardRepository repo, ICurrentUser currentUser) =>
        {
            if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            var board = await repo.CreateBoardAsync(title, ownerId);
            return Results.Created($"/api/boards/{board.Id}", board);
        });

        group.MapGet("/{id:int}", async (int id, IBoardRepository boards, IListRepository lists,
            ICardRepository cards, ILabelRepository labels, IChecklistItemRepository checklist,
            ICurrentUser currentUser) =>
        {
            if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
            // The board is ownership-checked here, so the nested reads below are already scoped.
            if (await boards.GetBoardAsync(id, ownerId) is not { } board) return Results.NotFound();

            var palette = (await labels.GetBoardLabelsAsync(id, ownerId))
                .Select(l => new LabelDto(l.Id, l.Name, l.Colour)).ToList();
            var labelIdsByCard = await labels.GetLabelIdsByCardAsync(id, ownerId);
            var counts = await checklist.GetCountsByCardAsync(id, ownerId);

            var summaries = new List<ListSummary>();
            foreach (var l in await lists.GetListsForBoardAsync(id, ownerId))
            {
                var cardSummaries = (await cards.GetCardsForListAsync(l.Id, ownerId))
                    .Select(c => new CardSummary(c.Id, c.Title, c.DueDate, c.Position, c.CompletedAt,
                        labelIdsByCard.TryGetValue(c.Id, out var ids) ? ids : new List<int>(),
                        counts.TryGetValue(c.Id, out var k) ? k.Done : 0,
                        counts.TryGetValue(c.Id, out var t) ? t.Total : 0))
                    .ToList();
                summaries.Add(new ListSummary(l.Id, l.Title, l.Position, cardSummaries));
            }
            return Results.Ok(new BoardDetail(board.Id, board.Title, palette, summaries));
        });

        group.MapPut("/{id:int}", async (int id, RenameBoardRequest req, IBoardRepository repo,
            ICurrentUser currentUser) =>
        {
            if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            return await repo.RenameBoardAsync(id, title, ownerId)
                ? Results.NoContent() : Results.NotFound();
        });

        group.MapDelete("/{id:int}", async (int id, IBoardRepository repo, ICurrentUser currentUser) =>
        {
            if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
            return await repo.DeleteBoardAsync(id, ownerId) ? Results.NoContent() : Results.NotFound();
        });

        return group;
    }
}

public record CreateBoardRequest(string Title);
public record RenameBoardRequest(string Title);
public record BoardDetail(int Id, string Title, IReadOnlyList<LabelDto> Labels, IReadOnlyList<ListSummary> Lists);
public record ListSummary(int Id, string Title, int Position, IReadOnlyList<CardSummary> Cards);
public record CardSummary(int Id, string Title, DateOnly? DueDate, int Position, DateTime? CompletedAt, IReadOnlyList<int> LabelIds, int ChecklistDone, int ChecklistTotal);
