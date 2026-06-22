using Wend.Core;

namespace Wend.Api;

public static class ListEndpoints
{
    private const int MaxTitleLength = 200;

    public static IEndpointRouteBuilder MapListEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/boards/{boardId:int}/lists",
            async (int boardId, CreateListRequest req, IBoardRepository boards, IListRepository lists) =>
            {
                var title = req.Title?.Trim() ?? "";
                if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
                if (await boards.GetBoardAsync(boardId) is null) return Results.NotFound();
                var list = await lists.CreateListAsync(boardId, title);
                return Results.Created($"/api/lists/{list.Id}", list);
            });

        app.MapPut("/api/lists/{id:int}", async (int id, RenameListRequest req, IListRepository lists) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            return await lists.RenameListAsync(id, title) ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/api/lists/{id:int}", async (int id, IListRepository lists) =>
            await lists.DeleteListAsync(id) ? Results.NoContent() : Results.NotFound());
        
        app.MapPut("/api/lists/{id:int}/move", async (int id, MoveListRequest req, IListRepository lists) =>
            await lists.MoveListAsync(id, req.Position) ? Results.NoContent() : Results.NotFound());
        
        return app;
    }
}

public record CreateListRequest(string Title);
public record RenameListRequest(string Title);
public record MoveListRequest(int Position);