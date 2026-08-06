using Wend.Core;

namespace Wend.Api;

public static class ChecklistItemEndpoints
{
    private const int MaxTextLength = 200;

    public static IEndpointRouteBuilder MapChecklistItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/cards/{cardId:int}/checklist-items",
            async (int cardId, CreateChecklistItemRequest req, ICardRepository cards,
                IChecklistItemRepository items, ICurrentUser currentUser) =>
            {
                if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
                var text = req.Text?.Trim() ?? "";
                if (text.Length is 0 or > MaxTextLength) return Results.BadRequest();
                // Another user's card resolves as null here, so posting into it is 404 — not 403.
                if (await cards.GetCardAsync(cardId, ownerId) is null) return Results.NotFound();
                var item = await items.AddItemAsync(cardId, text, ownerId);
                return Results.Created($"/api/checklist-items/{item.Id}", item);
            });

        app.MapPut("/api/checklist-items/{id:int}",
            async (int id, RenameChecklistItemRequest req, IChecklistItemRepository items,
                ICurrentUser currentUser) =>
            {
                if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
                var text = req.Text?.Trim() ?? "";
                if (text.Length is 0 or > MaxTextLength) return Results.BadRequest();
                return await items.RenameItemAsync(id, text, ownerId) ? Results.NoContent() : Results.NotFound();
            });

        app.MapPut("/api/checklist-items/{id:int}/check",
            async (int id, CheckChecklistItemRequest req, IChecklistItemRepository items,
                ICurrentUser currentUser) =>
            {
                if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
                return await items.SetCheckedAsync(id, req.Checked, ownerId)
                    ? Results.NoContent() : Results.NotFound();
            });

        app.MapPut("/api/checklist-items/{id:int}/move",
            async (int id, MoveChecklistItemRequest req, IChecklistItemRepository items,
                ICurrentUser currentUser) =>
            {
                if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
                return await items.MoveItemAsync(id, req.Position, ownerId)
                    ? Results.NoContent() : Results.NotFound();
            });

        app.MapDelete("/api/checklist-items/{id:int}",
            async (int id, IChecklistItemRepository items, ICurrentUser currentUser) =>
            {
                if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
                return await items.DeleteItemAsync(id, ownerId) ? Results.NoContent() : Results.NotFound();
            });

        app.MapPost("/api/checklist-items/{id:int}/restore",
            async (int id, IChecklistItemRepository items, ICurrentUser currentUser) =>
            {
                if (currentUser.UserId is not { } ownerId) return Results.Unauthorized();
                return await items.RestoreItemAsync(id, ownerId) ? Results.NoContent() : Results.NotFound();
            });

        return app;
    }
}

public record CreateChecklistItemRequest(string Text);
public record RenameChecklistItemRequest(string Text);
public record CheckChecklistItemRequest(bool Checked);
public record MoveChecklistItemRequest(int Position);
public record ChecklistItemDto(int Id, string Text, DateTime? CheckedAt, int Position);
