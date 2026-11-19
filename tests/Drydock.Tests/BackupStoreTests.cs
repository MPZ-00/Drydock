using Drydock.Core;

namespace Drydock.Tests;

public sealed class BackupStoreTests : IDisposable
{
    readonly string _temp = Directory.CreateTempSubdirectory("drydock-backups-").FullName;
    string Root => Path.Combine(_temp, "backups");

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    string MakeSave(string name = "My World")
    {
        var dir = Directory.CreateDirectory(Path.Combine(_temp, "saves", name)).FullName;
        File.WriteAllText(Path.Combine(dir, "savegame.vrb"), "save v1");
        File.WriteAllText(Path.Combine(dir, "snapshots.vrb"), "snap v1");
        File.WriteAllText(Path.Combine(dir, "thumb.jpg"), "thumb");
        Directory.CreateDirectory(Path.Combine(dir, "Blobs"));
        File.WriteAllText(Path.Combine(dir, "Blobs", "big.bin"), "blob");
        return dir;
    }

    BackupStore Store(bool gameRunning = false) => new(Root, () => gameRunning);

    void HandMade(string saveName, DateTime taken, params string[] files)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Root, $"{saveName}-{taken:yyyyMMdd-HHmmss}")).FullName;
        foreach (var f in files)
            File.WriteAllText(Path.Combine(dir, f), "old");
    }

    [Fact]
    public void Backups_hold_top_level_files_only()
    {
        var backup = Store().Create(MakeSave());
        Assert.Equal(["savegame.vrb", "snapshots.vrb", "thumb.jpg"], backup.Files.Order());
        Assert.False(Directory.Exists(Path.Combine(backup.Folder, "Blobs")));
    }

    [Fact]
    public void Backups_in_one_second_do_not_clash()
    {
        var save = MakeSave();
        var a = Store().Create(save);
        var b = Store().Create(save);
        Assert.NotEqual(a.Folder, b.Folder);
        Assert.Equal(2, Store().For(save).Count);
    }

    [Fact]
    public void Backups_of_other_saves_are_not_listed()
    {
        var foo = MakeSave("Foo");
        Store().Create(foo);
        Store().Create(MakeSave("Foo-Bar"));
        Assert.Single(Store().For(foo));
    }

    [Fact]
    public void Old_single_file_backups_are_listed()
    {
        var save = MakeSave();
        HandMade("My World", new DateTime(2026, 1, 1, 12, 0, 0), "savegame.vrb");
        var backup = Assert.Single(Store().For(save));
        Assert.True(backup.SaveFileOnly);
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0), backup.Taken);
    }

    [Fact]
    public void Restore_backs_up_first_and_copies_back()
    {
        var save = MakeSave();
        var backup = Store().Create(save);
        File.WriteAllText(Path.Combine(save, "savegame.vrb"), "save v2");

        var safety = Store().Restore(backup, save);

        Assert.Equal("save v1", File.ReadAllText(Path.Combine(save, "savegame.vrb")));
        Assert.Equal("save v2", File.ReadAllText(Path.Combine(safety.Folder, "savegame.vrb")));
    }

    [Fact]
    public void Restore_leaves_files_the_backup_lacks()
    {
        var save = MakeSave();
        var backup = Store().Create(save);
        File.WriteAllText(Path.Combine(save, "new.vrb"), "new");
        Store().Restore(backup, save);
        Assert.True(File.Exists(Path.Combine(save, "new.vrb")));
    }

    [Fact]
    public void Restore_is_refused_while_the_game_runs()
    {
        var save = MakeSave();
        var backup = Store().Create(save);
        Assert.Throws<InvalidOperationException>(() => Store(gameRunning: true).Restore(backup, save));
    }

    [Fact]
    public void Prune_keeps_the_newest()
    {
        var save = MakeSave();
        for (int i = 0; i < 23; i++)
            HandMade("My World", new DateTime(2026, 1, 1).AddHours(i), "savegame.vrb");
        Store().Prune(save, keep: 20);
        var left = Store().For(save);
        Assert.Equal(20, left.Count);
        Assert.Equal(new DateTime(2026, 1, 1).AddHours(3), left[^1].Taken);
    }

    [Fact]
    public void A_locked_old_backup_does_not_fail_pruning()
    {
        var save = MakeSave();
        for (int i = 0; i < 22; i++)
            HandMade("My World", new DateTime(2026, 1, 1).AddHours(i), "savegame.vrb");
        var oldest = Store().For(save)[^1];
        using (File.Open(Path.Combine(oldest.Folder, "savegame.vrb"), FileMode.Open, FileAccess.Read, FileShare.None))
            Store().Prune(save, keep: 20);
        Assert.Equal(21, Store().For(save).Count);
    }

    [Fact]
    public void A_drydock_write_is_not_game_progress()
    {
        var save = MakeSave();
        var backup = Store().Create(save);
        File.WriteAllText(Path.Combine(save, "savegame.vrb"), "edited by drydock");
        Store().MarkWritten(backup, save);
        Assert.False(Store().SavedSince(backup, save));

        File.SetLastWriteTime(Path.Combine(save, "savegame.vrb"), DateTime.Now.AddMinutes(5));
        Assert.True(Store().SavedSince(backup, save));
    }

    [SaveFact]
    public void A_save_written_after_loading_is_detected()
    {
        var copy = Path.Combine(Directory.CreateDirectory(Path.Combine(_temp, "copy")).FullName, "savegame.vrb");
        File.Copy(TestGame.SavePath!, copy);
        var world = World.Load(copy, GameContent.Load(GameInstall.RootDir));
        Assert.False(world.ChangedOnDisk());

        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(1));
        Assert.True(world.ChangedOnDisk());
    }
}
