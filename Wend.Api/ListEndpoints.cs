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

        // PUT (rename), DELETE and move arrive in Tasks 7-8.
        return app;
    }
}

public record CreateListRequest(string Title);
