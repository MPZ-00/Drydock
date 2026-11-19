using System.Runtime.CompilerServices;
using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App.Tests;

static class TestGame
{
    public static bool Installed { get; private set; }

    [ModuleInitializer]
    internal static void Init()
    {
        if (GameInstall.Find() is { } dir)
        {
            GameInstall.Register(dir);
            Installed = true;
        }
    }
}

public sealed class GameFactAttribute : FactAttribute
{
    public GameFactAttribute()
    {
        if (!TestGame.Installed)
            Skip = "Space Engineers 2 is not installed.";
    }
}

public sealed class MainViewModelTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("drydock-app-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    MainViewModel Create() => new(
        new Settings(),
        Path.Combine(_dir, "user.json"),
        Path.Combine(_dir, "portable.json"),
        savesRoot: Path.Combine(_dir, "saves"));

    [Fact]
    public void Theme_choice_is_saved()
    {
        using var vm = Create();
        vm.Theme = "Dark";
        Assert.Equal("Dark", Settings.Load(Path.Combine(_dir, "user.json"), Path.Combine(_dir, "portable.json")).Theme);
    }

    [GameFact]
    public async Task A_failed_load_keeps_the_app_usable()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "saves", "Broken")).FullName;
        File.WriteAllText(Path.Combine(folder, "savegame.vrb"), "not a save");
        using var vm = Create();
        vm.RefreshSaves();

        await vm.LoadAsync(Assert.Single(vm.Saves));

        Assert.NotNull(vm.Error);
        Assert.Empty(vm.Objects);
        Assert.False(vm.HasWorld);
    }
}
