using Wend.Core;

namespace Wend.Api;

public static class ChecklistItemEndpoints
{
    private const int MaxTextLength = 200;

    public static IEndpointRouteBuilder MapChecklistItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/cards/{cardId:int}/checklist-items",
            async (int cardId, CreateChecklistItemRequest req, ICardRepository cards, IChecklistItemRepository items) =>
            {
                var text = req.Text?.Trim() ?? "";
                if (text.Length is 0 or > MaxTextLength) return Results.BadRequest();
                if (await cards.GetCardAsync(cardId) is null) return Results.NotFound();
                var item = await items.AddItemAsync(cardId, text);
                return Results.Created($"/api/checklist-items/{item.Id}", item);
            });

        app.MapPut("/api/checklist-items/{id:int}",
            async (int id, RenameChecklistItemRequest req, IChecklistItemRepository items) =>
            {
                var text = req.Text?.Trim() ?? "";
                if (text.Length is 0 or > MaxTextLength) return Results.BadRequest();
                return await items.RenameItemAsync(id, text) ? Results.NoContent() : Results.NotFound();
            });

        return app;
    }
}

public record CreateChecklistItemRequest(string Text);
public record RenameChecklistItemRequest(string Text);
public record ChecklistItemDto(int Id, string Text, DateTime? CheckedAt, int Position);
