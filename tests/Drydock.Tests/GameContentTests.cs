using Drydock.Core;

namespace Drydock.Tests;

public class GameContentTests
{
    [GameFact]
    public void Cached_content_matches_a_fresh_scan()
    {
        var fresh = GameContent.Load(GameInstall.RootDir, useCache: false);
        GameContent.Load(GameInstall.RootDir); // writes the cache if it is missing or stale
        var cached = GameContent.Load(GameInstall.RootDir);

        Assert.Equal(fresh.Items.Items.OrderBy(i => i.Guid), cached.Items.Items.OrderBy(i => i.Guid));
        var consumables = Guid.Parse("935c5b3c-e735-49c4-9b61-62c16df1764a");
        var character = Guid.Parse("4cc16aab-0c8b-4196-9a2d-0d8bc33fe89e");
        Assert.Equal(fresh.SlotName(consumables), cached.SlotName(consumables));
        Assert.Equal(25, cached.Rules(character, consumables)?.MaxMass);
        Assert.Equal(5, cached.Rules(character, consumables)?.MaxStacks);
    }
}
