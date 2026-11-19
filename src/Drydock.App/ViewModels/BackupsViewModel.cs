using System.Globalization;
using Drydock.Core;

namespace Drydock.App.ViewModels;

public sealed record BackupItem(Backup Backup, string TakenText, string AgeText, string FilesText, IReadOnlyList<string> Warnings)
{
    public string WarningText => string.Join("\n", Warnings);
}

/// <summary>The backups of one save, newest first, with what restoring each would cost.</summary>
public sealed class BackupsViewModel(BackupStore store, string saveDir)
{
    public const string SavedSinceWarning = "The game has saved since this backup. Restoring loses that progress.";
    public const string SaveFileOnlyWarning = "This backup only holds savegame.vrb, the rest of the save may not match.";

    public BackupStore Store { get; } = store;
    public string SaveDir { get; } = saveDir;

    public IReadOnlyList<BackupItem> Items { get; } = store.For(saveDir).Select(b =>
    {
        var warnings = new List<string>();
        if (store.SavedSince(b, saveDir))
            warnings.Add(SavedSinceWarning);
        if (b.SaveFileOnly)
            warnings.Add(SaveFileOnlyWarning);
        return new BackupItem(b, b.Taken.ToString("g", CultureInfo.CurrentCulture), Age(b.Taken, DateTime.Now),
            b.Files.Count == 1 ? "1 file" : $"{b.Files.Count} files", warnings);
    }).ToList();

    public static string Age(DateTime taken, DateTime now)
    {
        var age = now - taken;
        static string Unit(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? Unit((int)age.TotalMinutes, "minute")
            : age.TotalDays < 1 ? Unit((int)age.TotalHours, "hour")
            : Unit((int)age.TotalDays, "day");
    }
}
