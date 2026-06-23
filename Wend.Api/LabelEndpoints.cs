using Wend.Core;

namespace Wend.Api;

public static class LabelEndpoints
{
    private const int MaxNameLength = 50;

    public static IEndpointRouteBuilder MapLabelEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/boards/{boardId:int}/labels",
            async (int boardId, IBoardRepository boards, ILabelRepository labels) =>
            {
                if (await boards.GetBoardAsync(boardId) is null) return Results.NotFound();
                var palette = (await labels.GetBoardLabelsAsync(boardId))
                    .Select(l => new LabelDto(l.Id, l.Name, l.Colour));
                return Results.Ok(palette);
            });

        app.MapPost("/api/boards/{boardId:int}/labels",
            async (int boardId, CreateLabelRequest req, IBoardRepository boards, ILabelRepository labels) =>
            {
                var name = req.Name?.Trim() ?? "";
                if (name.Length is 0 or > MaxNameLength) return Results.BadRequest();
                if (!LabelColours.IsValid(req.Colour)) return Results.BadRequest();
                if (await boards.GetBoardAsync(boardId) is null) return Results.NotFound();
                var label = await labels.CreateLabelAsync(boardId, name, req.Colour);
                return Results.Created($"/api/labels/{label.Id}", new LabelDto(label.Id, label.Name, label.Colour));
            });

        // PUT / DELETE / attach / detach arrive in Tasks 5-6.
        return app;
    }
}

public record LabelDto(int Id, string Name, string Colour);
public record CreateLabelRequest(string Name, string Colour);
