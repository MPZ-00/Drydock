using System.Diagnostics;

namespace Drydock.Core;

/// <summary>Writes an edited archive back over a save file, with a backup and without ever leaving a half-written file.</summary>
public static class SaveWriter
{
    public static readonly string BackupRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drydock", "backups");

    // the running game overwrites the save on its next save and may hold its own copy in memory
    public static bool GameRunning() => Process.GetProcessesByName("SpaceEngineers2").Length > 0;

    /// <returns>The backup folder.</returns>
    public static string Write(SaveArchive edited, string targetPath)
    {
        if (GameRunning())
            throw new InvalidOperationException("Space Engineers 2 is running. Quit it completely, then run this again.");
        if (!edited.File.Verify(out var problem))
            throw new InvalidOperationException($"Refusing to write an archive that fails verification: {problem}");

        var saveDir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        var backup = Path.Combine(BackupRoot, $"{Path.GetFileName(saveDir)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backup);
        File.Copy(targetPath, Path.Combine(backup, Path.GetFileName(targetPath)));

        var temp = targetPath + ".drydock-tmp";
        File.WriteAllBytes(temp, edited.File.Data);
        File.Move(temp, targetPath, overwrite: true);
        return backup;
    }
}
