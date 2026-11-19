using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Drydock.App;

/// <summary>First run without a detected install: lets the player point at the game's Game2 folder.</summary>
sealed class GameDirWindow : Window
{
    public string? GameDir { get; private set; }

    public GameDirWindow()
    {
        Title = "Drydock";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var message = new TextBlock
        {
            Text = "Space Engineers 2 wasn't found. Pick its Game2 folder, usually under steamapps\\common\\SpaceEngineers2.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var pick = new Button { Content = "Pick folder...", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Quit", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        pick.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Space Engineers 2 Game2 folder" };
            if (dialog.ShowDialog(this) != true)
                return;
            if (Core.GameInstall.Find(dialog.FolderName) is { } dir)
            {
                GameDir = dir;
                DialogResult = true;
            }
            else
                message.Text = $"{Path.GetFileName(dialog.FolderName)} doesn't hold VRage.Library.dll. Pick the Game2 folder of Space Engineers 2.";
        };
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                message,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { pick, cancel } },
            },
        };
    }
}
