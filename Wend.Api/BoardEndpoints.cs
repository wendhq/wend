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
        group.MapGet("/{id:int}", async (int id, IBoardRepository repo) =>
            await repo.GetBoardAsync(id) is { } board ? Results.Ok(board) : Results.NotFound());

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
