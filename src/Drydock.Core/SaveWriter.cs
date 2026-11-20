using System.Diagnostics;

namespace Drydock.Core;

/// <summary>Writes an edited archive back over a save file, with a backup and without ever leaving a half-written file.</summary>
public static class SaveWriter
{
    // the running game overwrites the save on its next save and may hold its own copy in memory
    public const string GameRunningReason =
        "Space Engineers 2 is running. While it has this world loaded, its next autosave or exit writes its own copy over the file and your changes are gone. " +
        "Writing is only safe when the game sits at the main menu or has a different world loaded, and Drydock can't tell which.";

    public static bool GameRunning() => Process.GetProcessesByName("SpaceEngineers2").Length > 0;

    /// <returns>The backup folder.</returns>
    /// <param name="allowRunningGame">The user confirmed the game is at the main menu or has another world loaded.</param>
    public static string Write(SaveArchive edited, string targetPath, BackupStore? store = null, bool allowRunningGame = false)
    {
        if (edited.IsPreview)
            throw new InvalidOperationException("A preview can't be written.");
        if (!allowRunningGame && GameRunning())
            throw new InvalidOperationException(GameRunningReason);
        if (!edited.File.Verify(out var problem))
            throw new InvalidOperationException($"Refusing to write an archive that fails verification: {problem}");

        store ??= BackupStore.Default;
        var saveDir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        var backup = store.Create(saveDir);

        var temp = targetPath + ".drydock-tmp";
        File.WriteAllBytes(temp, edited.File.Data);
        File.Move(temp, targetPath, overwrite: true);
        try
        {
            store.MarkWritten(backup, saveDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the save is written; without the marker this backup only reads as older than it is
        }
        store.Prune(saveDir);
        return backup.Folder;
    }
}
