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

        app.MapPut("/api/checklist-items/{id:int}/check",
            async (int id, CheckChecklistItemRequest req, IChecklistItemRepository items) =>
                await items.SetCheckedAsync(id, req.Checked) ? Results.NoContent() : Results.NotFound());

        app.MapPut("/api/checklist-items/{id:int}/move",
            async (int id, MoveChecklistItemRequest req, IChecklistItemRepository items) =>
                await items.MoveItemAsync(id, req.Position) ? Results.NoContent() : Results.NotFound());

        app.MapDelete("/api/checklist-items/{id:int}", async (int id, IChecklistItemRepository items) =>
            await items.DeleteItemAsync(id) ? Results.NoContent() : Results.NotFound());

        app.MapPost("/api/checklist-items/{id:int}/restore", async (int id, IChecklistItemRepository items) =>
            await items.RestoreItemAsync(id) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}

public record CreateChecklistItemRequest(string Text);
public record RenameChecklistItemRequest(string Text);
public record CheckChecklistItemRequest(bool Checked);
public record MoveChecklistItemRequest(int Position);
public record ChecklistItemDto(int Id, string Text, DateTime? CheckedAt, int Position);
