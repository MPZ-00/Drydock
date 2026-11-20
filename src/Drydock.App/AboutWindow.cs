using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Drydock.App.ViewModels;

namespace Drydock.App;

sealed class AboutWindow : Window
{
    public AboutWindow(string settingsFolder, string backupsFolder)
    {
        Title = "About Drydock";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var found = AboutInfo.FoundGameVersion();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new Image { Source = IconCache.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Drydock.png"))), Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0) },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Drydock", FontSize = 22, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = $"Version {AboutInfo.AppVersion}, by {AboutInfo.Author}", Opacity = 0.75 },
                    },
                },
            },
        });
        panel.Children.Add(Text("Inventory editor for Space Engineers 2 saves."));
        panel.Children.Add(Text($"Tested with game version {AboutInfo.TestedGameVersion}. Found on this PC: {found ?? "unknown"}."));
        if (AboutInfo.GameVersionNote(found) is { } note)
            panel.Children.Add(Text(note, Brushes.DarkOrange));
        panel.Children.Add(Link("Source code on GitHub", AboutInfo.RepoUrl));
        panel.Children.Add(Link("Report a bug", AboutInfo.IssuesUrl));
        panel.Children.Add(Link("License: AGPL-3.0", AboutInfo.LicenseUrl));
        panel.Children.Add(Text("Not affiliated with Keen Software House.", opacity: 0.7));

        var settings = new Button { Content = "Settings folder", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var backups = new Button { Content = "Backups folder", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Padding = new Thickness(12, 4, 12, 4) };
        settings.Click += (_, _) => OpenFolder(settingsFolder);
        backups.Click += (_, _) => OpenFolder(backupsFolder);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { settings, backups, close },
        });
        Content = panel;
    }

    static TextBlock Text(string text, Brush? color = null, double opacity = 1)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = opacity };
        // without a color the text follows the light or dark theme
        if (color != null)
            block.Foreground = color;
        return block;
    }

    static TextBlock Link(string text, string url)
    {
        var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, e) => Open(e.Uri.AbsoluteUri);
        return new TextBlock(link) { Margin = new Thickness(0, 0, 0, 6) };
    }

    static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Open(folder);
    }

    static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
