using Wend.Core;

namespace Wend.Api;

public static class BoardEndpoints
{
    private const int MaxTitleLength = 200;

    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IBoardRepository repo) =>
            Results.Ok(await repo.GetBoardsAsync()));
        
        group.MapPost("/", async (CreateBoardRequest req, IBoardRepository repo) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            var board = await repo.CreateBoardAsync(title);
            return Results.Created($"/api/boards/{board.Id}", board);
        });
        group.MapGet("/{id:int}", async (int id, IBoardRepository boards, IListRepository lists, ICardRepository cards, ILabelRepository labels) =>
        {
            if (await boards.GetBoardAsync(id) is not { } board) return Results.NotFound();

            var palette = (await labels.GetBoardLabelsAsync(id))
                .Select(l => new LabelDto(l.Id, l.Name, l.Colour)).ToList();
            var labelIdsByCard = await labels.GetLabelIdsByCardAsync(id);

            var summaries = new List<ListSummary>();
            foreach (var l in await lists.GetListsForBoardAsync(id))
            {
                var cardSummaries = (await cards.GetCardsForListAsync(l.Id))
                    .Select(c => new CardSummary(c.Id, c.Title, c.DueDate, c.Position,
                        labelIdsByCard.TryGetValue(c.Id, out var ids) ? ids : new List<int>()))
                    .ToList();
                summaries.Add(new ListSummary(l.Id, l.Title, l.Position, cardSummaries));
            }
            return Results.Ok(new BoardDetail(board.Id, board.Title, palette, summaries));
        });

        group.MapPut("/{id:int}", async (int id, RenameBoardRequest req, IBoardRepository repo) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            return await repo.RenameBoardAsync(id, title)
                ? Results.NoContent() : Results.NotFound();
        });

        group.MapDelete("/{id:int}", async (int id, IBoardRepository repo) =>
            await repo.DeleteBoardAsync(id) ? Results.NoContent() : Results.NotFound());
        
        return group;
    }
}

public record CreateBoardRequest(string Title);
public record RenameBoardRequest(string Title);
public record BoardDetail(int Id, string Title, IReadOnlyList<LabelDto> Labels, IReadOnlyList<ListSummary> Lists);
public record ListSummary(int Id, string Title, int Position, IReadOnlyList<CardSummary> Cards);
public record CardSummary(int Id, string Title, DateOnly? DueDate, int Position, IReadOnlyList<int> LabelIds);
