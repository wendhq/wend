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
        
        return group;
    }
}

public record CreateBoardRequest(string Title);