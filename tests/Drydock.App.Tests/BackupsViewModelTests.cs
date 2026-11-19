using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App.Tests;

public sealed class BackupsViewModelTests : IDisposable
{
    readonly string _temp = Directory.CreateTempSubdirectory("drydock-app-backups-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    string MakeSave()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_temp, "saves", "My World")).FullName;
        File.WriteAllText(Path.Combine(dir, "savegame.vrb"), "save v1");
        File.WriteAllText(Path.Combine(dir, "snapshots.vrb"), "snap v1");
        return dir;
    }

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(60 * 5, "5 minutes ago")]
    [InlineData(60, "1 minute ago")]
    [InlineData(3600 * 3, "3 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(86400 * 12, "12 days ago")]
    public void Age_text_is_human(int secondsAgo, string expected)
    {
        var now = new DateTime(2026, 11, 19, 20, 0, 0);
        Assert.Equal(expected, BackupsViewModel.Age(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void Warnings_match_backup_kind()
    {
        var save = MakeSave();
        var store = new BackupStore(Path.Combine(_temp, "backups"), () => false);
        var full = store.Create(save);
        File.SetLastWriteTimeUtc(Path.Combine(save, "savegame.vrb"), DateTime.UtcNow.AddMinutes(5));
        var only = Directory.CreateDirectory(Path.Combine(store.Root, "My World-20250101-120000")).FullName;
        File.WriteAllText(Path.Combine(only, "savegame.vrb"), "old");

        var vm = new BackupsViewModel(store, save);

        var fullItem = vm.Items.Single(i => i.Backup.Folder == full.Folder);
        Assert.Contains("The game has saved since this backup. Restoring loses that progress.", fullItem.Warnings);
        Assert.DoesNotContain("This backup only holds savegame.vrb, the rest of the save may not match.", fullItem.Warnings);
        var onlyItem = vm.Items.Single(i => i.Backup.Folder == only);
        Assert.Contains("This backup only holds savegame.vrb, the rest of the save may not match.", onlyItem.Warnings);
        Assert.Equal("2 files", fullItem.FilesText);
    }
}
