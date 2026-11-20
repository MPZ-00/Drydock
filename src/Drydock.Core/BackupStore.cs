using System.Globalization;
using System.Text.RegularExpressions;

namespace Drydock.Core;

/// <param name="Files">The save files the backup holds, without Drydock's own marker.</param>
public sealed record Backup(string Folder, string SaveName, DateTime Taken, IReadOnlyList<string> Files)
{
    /// <summary>Made before backups held the whole save, restoring it mixes this savegame.vrb with newer session files.</summary>
    public bool SaveFileOnly => Files.Count == 1;
}

/// <summary>Copies of a save's top-level files, one folder per backup, named after the save and the time.</summary>
public sealed partial class BackupStore(string root, Func<bool>? gameRunning = null)
{
    // the save file's write time right after Drydock wrote it, so its own write doesn't count as the game saving
    const string WrittenMarker = "drydock-written.txt";
    const string StampFormat = "yyyyMMdd-HHmmss";

    readonly Func<bool> _gameRunning = gameRunning ?? SaveWriter.GameRunning;

    public static BackupStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drydock", "backups"));

    public string Root { get; } = root;

    static string SaveName(string saveDir) => Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveDir)));

    /// <summary>Copies the save folder's top-level files. Subfolders (Blobs, the game's own autosaves) are left out.</summary>
    public Backup Create(string saveDir)
    {
        var name = SaveName(saveDir);
        var stamp = DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture);
        var folder = Path.Combine(Root, $"{name}-{stamp}");
        for (int n = 2; Directory.Exists(folder); n++)
            folder = Path.Combine(Root, $"{name}-{stamp}-{n}");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.EnumerateFiles(saveDir))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        return Read(folder, name)!;
    }

    public void MarkWritten(Backup backup, string saveDir) =>
        File.WriteAllText(Path.Combine(backup.Folder, WrittenMarker),
            File.GetLastWriteTimeUtc(Path.Combine(saveDir, "savegame.vrb")).ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Backups of this save, newest first.</summary>
    public List<Backup> For(string saveDir)
    {
        if (!Directory.Exists(Root))
            return [];
        var name = SaveName(saveDir);
        return Directory.EnumerateDirectories(Root)
            .Select(d => Read(d, name))
            .OfType<Backup>()
            .OrderByDescending(b => b.Taken)
            .ThenByDescending(b => b.Folder, StringComparer.Ordinal)
            .ToList();
    }

    static Backup? Read(string folder, string saveName)
    {
        var dirName = Path.GetFileName(folder);
        if (!dirName.StartsWith(saveName + "-", StringComparison.OrdinalIgnoreCase))
            return null;
        var match = Stamp().Match(dirName[(saveName.Length + 1)..]);
        if (!match.Success || !DateTime.TryParseExact(match.Groups[1].Value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken))
            return null;
        var files = Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()
            .Where(f => f != WrittenMarker).ToList();
        return new Backup(folder, saveName, taken, files);
    }

    [GeneratedRegex(@"^(\d{8}-\d{6})(-\d+)?$")]
    private static partial Regex Stamp();

    /// <summary>Whether the game wrote the save after this backup (and after Drydock's own write that followed it).</summary>
    public bool SavedSince(Backup backup, string saveDir)
    {
        var current = File.GetLastWriteTimeUtc(Path.Combine(saveDir, "savegame.vrb"));
        var marker = Path.Combine(backup.Folder, WrittenMarker);
        if (File.Exists(marker) && DateTime.TryParse(File.ReadAllText(marker), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written))
            return current > written;
        return current > backup.Taken.ToUniversalTime().AddSeconds(1);
    }

    /// <summary>Puts a backup's files back. The current state is backed up first and returned.</summary>
    public Backup Restore(Backup backup, string saveDir, bool allowRunningGame = false)
    {
        if (!allowRunningGame && _gameRunning())
            throw new InvalidOperationException(SaveWriter.GameRunningReason);
        var safety = Create(saveDir);
        foreach (var file in backup.Files)
        {
            var target = Path.Combine(saveDir, file);
            var temp = target + ".drydock-tmp";
            File.Copy(Path.Combine(backup.Folder, file), temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        MarkWritten(safety, saveDir);
        return safety;
    }

    public void Prune(string saveDir, int keep = 20)
    {
        foreach (var old in For(saveDir).Skip(keep))
        {
            try
            {
                Directory.Delete(old.Folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a locked old backup goes on the next prune, it must not fail the write that came before
            }
        }
    }
}
