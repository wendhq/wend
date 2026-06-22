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
        
        app.MapGet("/api/cards/{id:int}", async (int id, ICardRepository cards, IListRepository lists) =>
        {
            if (await cards.GetCardAsync(id) is not { } c) return Results.NotFound();
            var list = await lists.GetListAsync(c.ListId);
            return Results.Ok(new CardDetail(c.Id, c.ListId, list?.Title ?? "", c.Title, c.Description, c.DueDate, c.Position));
        });

        // GET / PUT / DELETE arrive in Tasks 6-7.
        return app;
    }
}

public record CreateCardRequest(string Title);
public record CardDetail(int Id, int ListId, string ListTitle, string Title, string? Description, DateOnly? DueDate, int Position);