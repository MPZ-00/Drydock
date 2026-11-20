using System.Diagnostics;
using System.Reflection;
using Drydock.Core;

namespace Drydock.App.ViewModels;

/// <summary>What the About window tells: who made it, where it lives, and which game version it was tested with.</summary>
public static class AboutInfo
{
    /// <summary>The game exe's file version this release was tested with, the same number the game shows.</summary>
    public const string TestedGameVersion = "4.0.0.3169";

    public const string Author = "MPZ-00";
    public const string RepoUrl = "https://github.com/MPZ-00/Drydock";
    public const string IssuesUrl = RepoUrl + "/issues";
    public const string LicenseUrl = RepoUrl + "/blob/master/LICENSE";

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "unknown";

    /// <summary>The game's version from SpaceEngineers2.exe, or null when it can't be read.</summary>
    public static string? FoundGameVersion()
    {
        try
        {
            var exe = Path.Combine(GameInstall.BinDir, "SpaceEngineers2.exe");
            return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).FileVersion : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <returns>Null when the found version is the tested one.</returns>
    public static string? GameVersionNote(string? found) =>
        found == null ? "Drydock couldn't read the game's version."
        : found == TestedGameVersion ? null
        : $"This Drydock was tested with game version {TestedGameVersion}, yours is {found}. Updates can change the save format, keep backups.";
}
