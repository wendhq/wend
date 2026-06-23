using Wend.Core;

namespace Wend.Api;

public static class CardEndpoints
{
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 5000;

    public static IEndpointRouteBuilder MapCardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lists/{listId:int}/cards",
            async (int listId, CreateCardRequest req, IListRepository lists, ICardRepository cards) =>
            {
                var title = req.Title?.Trim() ?? "";
                if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
                if (await lists.GetListAsync(listId) is null) return Results.NotFound();
                var card = await cards.CreateCardAsync(listId, title);
                return Results.Created($"/api/cards/{card.Id}", card);
            });
        
        app.MapGet("/api/cards/{id:int}", async (int id, ICardRepository cards, IListRepository lists, ILabelRepository labels) =>
        {
            if (await cards.GetCardAsync(id) is not { } c) return Results.NotFound();
            var list = await lists.GetListAsync(c.ListId);
            var attached = (await labels.GetCardLabelsAsync(c.Id))
                .Select(l => new LabelDto(l.Id, l.Name, l.Colour)).ToList();
            return Results.Ok(new CardDetail(c.Id, c.ListId, list?.Title ?? "", list?.BoardId ?? 0,
                c.Title, c.Description, c.DueDate, c.Position, attached));
        });

        app.MapPut("/api/cards/{id:int}", async (int id, EditCardRequest req, ICardRepository cards) =>
        {
            var title = req.Title?.Trim() ?? "";
            if (title.Length is 0 or > MaxTitleLength) return Results.BadRequest();
            var description = req.Description?.Trim();
            if (description is { Length: > MaxDescriptionLength }) return Results.BadRequest();
            return await cards.EditCardAsync(id, title, description, req.DueDate)
                ? Results.NoContent() : Results.NotFound();
        });

        app.MapDelete("/api/cards/{id:int}", async (int id, ICardRepository cards) =>
            await cards.DeleteCardAsync(id) ? Results.NoContent() : Results.NotFound());
        return app;
    }
}

public record CreateCardRequest(string Title);
public record CardDetail(int Id, int ListId, string ListTitle, int BoardId, string Title, string? Description, DateOnly? DueDate, int Position, IReadOnlyList<LabelDto> Labels);
public record EditCardRequest(string Title, string? Description, DateOnly? DueDate);
