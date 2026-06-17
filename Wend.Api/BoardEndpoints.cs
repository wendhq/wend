using Wend.Core;

namespace Wend.Api;

public static class BoardEndpoints
{
    private const int MaxTitleLength = 200;

    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IBoardRepository repo) =>
            Results.Ok(await repo.GetBoardsAsync()));

        return group;
    }
}
