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

        app.MapPut("/api/labels/{id:int}", async (int id, EditLabelRequest req, ILabelRepository labels) =>
        {
            var name = req.Name?.Trim() ?? "";
            if (name.Length is 0 or > MaxNameLength) return Results.BadRequest();
            if (!LabelColours.IsValid(req.Colour)) return Results.BadRequest();
            return await labels.EditLabelAsync(id, name, req.Colour)
                ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/api/labels/{id:int}", async (int id, ILabelRepository labels) =>
            await labels.DeleteLabelAsync(id) ? Results.NoContent() : Results.NotFound());
        
        app.MapPost("/api/cards/{cardId:int}/labels",
            async (int cardId, AttachLabelRequest req, ICardRepository cards, IListRepository lists, ILabelRepository labels) =>
            {
                if (await cards.GetCardAsync(cardId) is not { } card) return Results.NotFound();
                if (await labels.GetLabelAsync(req.LabelId) is not { } label) return Results.NotFound();
                var list = await lists.GetListAsync(card.ListId);
                if (list is null || list.BoardId != label.BoardId) return Results.BadRequest(); // cross-board
                await labels.AttachAsync(cardId, req.LabelId); // idempotent
                return Results.NoContent();
            });

        app.MapDelete("/api/cards/{cardId:int}/labels/{labelId:int}",
            async (int cardId, int labelId, ILabelRepository labels) =>
            {
                await labels.DetachAsync(cardId, labelId); // idempotent — always 204
                return Results.NoContent();
            });
        
        return app;
    }
}

public record LabelDto(int Id, string Name, string Colour);
public record CreateLabelRequest(string Name, string Colour);
public record EditLabelRequest(string Name, string Colour);
public record AttachLabelRequest(int LabelId);