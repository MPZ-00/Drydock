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

public sealed class ItemIconTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("drydock-icons-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Def(string folder)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_dir, folder)).FullName;
        var def = Path.Combine(dir, "Thing.def");
        File.WriteAllText(def, "{}");
        return def;
    }

    [Fact]
    public void The_icon_folder_next_to_the_definition_wins()
    {
        var def = Def("Medikit");
        Directory.CreateDirectory(Path.Combine(_dir, "Medikit", "Icon"));
        File.WriteAllText(Path.Combine(_dir, "Medikit", "Icon", "Medikit_Icon.png"), "");
        Assert.Equal(Path.Combine(_dir, "Medikit", "Icon", "Medikit_Icon.png"), ItemCatalog.FindIcon(def));
    }

    [Fact]
    public void Several_icons_and_none_named_for_the_item_is_no_icon()
    {
        var def = Def("Datapads");
        var icons = Directory.CreateDirectory(Path.Combine(_dir, "Datapads", "Icons")).FullName;
        File.WriteAllText(Path.Combine(icons, "Datapad_Encrypted_Icon_200.png"), "");
        File.WriteAllText(Path.Combine(icons, "Datapad_Icon_200.png"), "");
        Assert.Null(ItemCatalog.FindIcon(def));
    }

    [Fact]
    public void A_picture_without_icon_in_its_name_is_not_an_icon()
    {
        var def = Def("Datapads");
        File.WriteAllText(Path.Combine(_dir, "Datapads", "Apology.png"), "");
        Assert.Null(ItemCatalog.FindIcon(def));
    }

    [Fact]
    public void The_terminal_icons_cover_items_without_their_own()
    {
        var def = Def(Path.Combine("Components", "Simple", "ElectronicParts"));
        var terminal = Directory.CreateDirectory(Path.Combine(_dir, "MainMenuData", "UI", "Terminal", "Components", "Simple")).FullName;
        File.WriteAllText(Path.Combine(terminal, "Electronic_Parts_Icon.png"), "");
        File.WriteAllText(Path.Combine(terminal, "ChromeCoating_Icon.png"), "");
        Assert.Equal(Path.Combine(terminal, "Electronic_Parts_Icon.png"), ItemCatalog.FindIcon(def, _dir));
    }

    [GameFact]
    public void Medikit_has_an_icon_from_the_install_and_some_items_have_none()
    {
        var items = GameContent.Load(GameInstall.RootDir).Items.Items;
        var medikit = items.First(i => i.Key.Contains("Medikit", StringComparison.OrdinalIgnoreCase) || i.Name.Contains("Medikit", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith(".png", medikit.Icon);
        Assert.True(File.Exists(medikit.Icon));
        Assert.Contains(items, i => i.Icon == null);
        var steel = items.First(i => i.Key.Contains("SteelPlate", StringComparison.OrdinalIgnoreCase) || i.Name == "Steel Plate");
        Assert.NotNull(steel.Icon);
    }
}
