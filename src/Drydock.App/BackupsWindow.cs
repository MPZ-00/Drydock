using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Drydock.App.ViewModels;

namespace Drydock.App;

/// <summary>Lists a save's backups and puts one back. Restoring backs up the current state first.</summary>
sealed class BackupsWindow : Window
{
    readonly MainViewModel _main;
    readonly ListBox _list = new() { Height = 320 };
    readonly TextBlock _warning = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    BackupsViewModel _vm;

    public BackupsWindow(MainViewModel main)
    {
        _main = main;
        _vm = new BackupsViewModel(main.Backups, Path.GetDirectoryName(main.LoadedSave!.File)!);
        Title = $"Backups of {main.LoadedSave.Name}";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _list.ItemTemplate = ItemLook();
        _list.SelectionChanged += (_, _) => _warning.Text = (_list.SelectedItem as BackupItem)?.WarningText ?? "";
        Fill();

        var restore = new Button { Content = "Restore", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
        var open = new Button { Content = "Open folder", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 4, 12, 4) };
        restore.Click += async (_, _) => await Restore();
        open.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", (_list.SelectedItem as BackupItem)?.Backup.Folder ?? _vm.Store.Root);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = $"Kept in {_vm.Store.Root}, the newest 20 per save.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) },
                _list,
                _warning,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { restore, open, close } },
            },
        };
    }

    void Fill()
    {
        _list.ItemsSource = _vm.Items;
        if (_vm.Items.Count == 0)
            _warning.Text = "No backups yet. Drydock makes one every time it saves.";
    }

    static DataTemplate ItemLook()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(MarginProperty, new Thickness(0, 2, 0, 2));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(BackupItem.TakenText)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.MultiBinding
        {
            StringFormat = "{0}, {1}",
            Bindings = { new System.Windows.Data.Binding(nameof(BackupItem.AgeText)), new System.Windows.Data.Binding(nameof(BackupItem.FilesText)) },
        });
        detail.SetValue(OpacityProperty, 0.75);
        panel.AppendChild(title);
        panel.AppendChild(detail);
        return new DataTemplate { VisualTree = panel };
    }

    async Task Restore()
    {
        if (_list.SelectedItem is not BackupItem item)
        {
            _warning.Text = "Pick a backup.";
            return;
        }
        if (_main.GameRunning)
        {
            MessageBox.Show(this, "Space Engineers 2 is running. Quit it completely, then restore.", "Drydock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var question = _main.PendingCount > 0
            ? $"Restore the backup from {item.TakenText}? Your {_main.PendingCount} pending changes are discarded."
            : $"Restore the backup from {item.TakenText}?";
        if (item.Warnings.Count > 0)
            question += "\n\n" + item.WarningText;
        question += "\n\nThe current save is backed up first, so this can be undone.";
        if (MessageBox.Show(this, question, "Drydock", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        try
        {
            await _main.RestoreAsync(item.Backup);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, e.Message, "Drydock", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _vm = new BackupsViewModel(_vm.Store, _vm.SaveDir);
        Fill();
        _warning.Text = "Restored. The save before restoring is the newest backup now.";
    }
}
