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

    [GameFact]
    public async Task An_edit_shows_in_the_pane_after_the_preview()
    {
        using var vm = new MainViewModel(new Settings(), Path.Combine(_dir, "user.json"), Path.Combine(_dir, "portable.json"));
        vm.RefreshSaves();
        if (vm.Saves.Count == 0)
            return; // no save on this machine
        await vm.LoadAsync(vm.Saves[0]);
        var main = vm.Objects[0].Inventories.First(i => i.Slot == "main");
        vm.SelectedInventory = main;
        var iron = vm.Catalog.First(i => i.Name == "Iron" && i.Type == "Ore");

        await vm.ApplyAsync(new SetTotal(main.Index, iron.Guid, vm.CurrentTotal(main.Index, iron.Guid) + 7));

        Assert.Equal(1, vm.PendingCount);
        var row = vm.Pane!.Rows.Single(r => r.Item == iron.Guid);
        Assert.True(row.IsChanged);
        Assert.True(vm.Tree[0].HasPending);
        Assert.Null(vm.EditError);
    }

    /// <summary>A copy of the newest real save under the temp saves root, or null without one.</summary>
    string? CopySave()
    {
        var source = Saves.List().FirstOrDefault();
        if (source == null)
            return null;
        var copy = Directory.CreateDirectory(Path.Combine(_dir, "saves", "Copy")).FullName;
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(source.File)!))
            File.Copy(f, Path.Combine(copy, Path.GetFileName(f)));
        return copy;
    }

    MainViewModel WithStore(bool gameRunning = false) => new(new Settings(), Path.Combine(_dir, "user.json"), Path.Combine(_dir, "portable.json"), Path.Combine(_dir, "saves"))
    {
        Backups = new BackupStore(Path.Combine(_dir, "backups"), () => gameRunning),
        GameRunningCheck = () => gameRunning,
    };

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Start_game_is_off_while_it_runs(bool running, bool canStart)
    {
        using var vm = WithStore(gameRunning: running);
        vm.RefreshGameRunning();
        Assert.Equal(canStart, vm.CanStartGame);
    }

    [GameFact]
    public async Task Save_stays_possible_while_the_game_runs_and_needs_the_one_time_ok()
    {
        if (CopySave() is not { } copy)
            return;
        using var vm = WithStore(gameRunning: true);
        vm.RefreshSaves();
        await vm.LoadAsync(vm.Saves.Single());
        var main = vm.Objects[0].Inventories.First(i => i.Slot == "main");
        var iron = vm.Catalog.First(i => i.Name == "Iron" && i.Type == "Ore");
        await vm.ApplyAsync(new SetTotal(main.Index, iron.Guid, vm.CurrentTotal(main.Index, iron.Guid) + 7));
        vm.RefreshGameRunning();

        Assert.True(vm.GameRunning);
        Assert.True(vm.CanSave);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.WriteAsync(writeAnyway: true));
        var backup = await vm.WriteAsync(writeAnyway: true, allowRunningGame: true);

        Assert.True(Directory.Exists(backup));
        Assert.Equal(0, vm.PendingCount);
    }

    [GameFact]
    public async Task A_save_the_game_wrote_meanwhile_is_never_overwritten()
    {
        if (CopySave() is not { } copy)
            return;
        using var vm = WithStore();
        vm.RefreshSaves();
        await vm.LoadAsync(vm.Saves.Single());
        var main = vm.Objects[0].Inventories.First(i => i.Slot == "main");
        var iron = vm.Catalog.First(i => i.Name == "Iron" && i.Type == "Ore");
        await vm.ApplyAsync(new SetTotal(main.Index, iron.Guid, vm.CurrentTotal(main.Index, iron.Guid) + 7));
        var file = Path.Combine(copy, "savegame.vrb");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        var bytes = File.ReadAllBytes(file);

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.WriteAsync(writeAnyway: true));

        Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [GameFact]
    public async Task A_second_save_while_saving_is_refused()
    {
        if (CopySave() is null)
            return;
        using var vm = WithStore();
        vm.RefreshSaves();
        await vm.LoadAsync(vm.Saves.Single());
        var main = vm.Objects[0].Inventories.First(i => i.Slot == "main");
        var iron = vm.Catalog.First(i => i.Name == "Iron" && i.Type == "Ore");
        await vm.ApplyAsync(new SetTotal(main.Index, iron.Guid, vm.CurrentTotal(main.Index, iron.Guid) + 7));

        var first = vm.WriteAsync(writeAnyway: true);
        var second = await Record.ExceptionAsync(() => vm.WriteAsync(writeAnyway: true));
        try { await first; } catch (InvalidOperationException) { } // the real writer refuses while SE2 runs

        Assert.IsType<InvalidOperationException>(second);
        Assert.Contains("already", second.Message);
    }

    [GameFact]
    public async Task A_save_that_fails_to_load_can_still_be_restored()
    {
        if (CopySave() is not { } copy)
            return;
        using var vm = WithStore();
        var backup = vm.Backups.Create(copy);
        File.WriteAllText(Path.Combine(copy, "savegame.vrb"), "broken");
        vm.RefreshSaves();
        var save = vm.Saves.Single();
        vm.SelectedSave = save;
        await vm.LoadAsync(save);
        Assert.False(vm.HasWorld);
        Assert.True(vm.CanOpenBackups);

        await vm.RestoreAsync(backup, save);

        Assert.True(vm.HasWorld);
        Assert.Null(vm.Error);
    }

    [GameFact]
    public async Task Saving_writes_a_backup_and_reloads_without_pending_edits()
    {
        var source = Saves.List().FirstOrDefault();
        if (source == null || SaveWriter.GameRunning())
            return; // needs a save, and the writer refuses while the game runs
        var copy = Directory.CreateDirectory(Path.Combine(_dir, "saves", "Copy")).FullName;
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(source.File)!))
            File.Copy(f, Path.Combine(copy, Path.GetFileName(f)));
        var vmWithStore = new MainViewModel(new Settings(), Path.Combine(_dir, "user.json"), Path.Combine(_dir, "portable.json"), Path.Combine(_dir, "saves"))
        {
            Backups = new BackupStore(Path.Combine(_dir, "backups"), () => false),
        };
        using var _ = vmWithStore;
        vmWithStore.RefreshSaves();
        await vmWithStore.LoadAsync(vmWithStore.Saves.Single());
        var main = vmWithStore.Objects[0].Inventories.First(i => i.Slot == "main");
        var iron = vmWithStore.Catalog.First(i => i.Name == "Iron" && i.Type == "Ore");
        decimal target = vmWithStore.CurrentTotal(main.Index, iron.Guid) + 7;
        await vmWithStore.ApplyAsync(new SetTotal(main.Index, iron.Guid, target));

        var backup = await vmWithStore.WriteAsync(writeAnyway: true);

        Assert.True(Directory.Exists(backup));
        Assert.Equal(0, vmWithStore.PendingCount);
        Assert.Equal(target, vmWithStore.CurrentTotal(main.Index, iron.Guid));
    }
}
