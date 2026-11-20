using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    readonly DispatcherTimer _gamePoll = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _gamePoll.Tick += (_, _) => _vm.RefreshGameRunning();
        Loaded += (_, _) =>
        {
            _vm.RefreshGameRunning();
            _gamePoll.Start();
            _vm.RefreshSaves();
            CheckTheme(_vm.Theme);
            if (_vm.Saves.Count == 0)
                _vm.ShowError($"No saves found in {Saves.Root}.");
            else
                SavePicker.SelectedIndex = 0;
        };
        Closing += (_, e) => e.Cancel = !ConfirmLeave("closing");
        Closed += (_, _) =>
        {
            _gamePoll.Stop();
            _vm.Dispose();
        };
    }

    bool _switching;

    async void OnSaveChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_switching || _vm.SelectedSave is not { } save || save == _vm.LoadedSave)
            return;
        if (!ConfirmLeave("switching saves"))
        {
            _switching = true;
            _vm.SelectedSave = _vm.LoadedSave;
            _switching = false;
            return;
        }
        await _vm.LoadAsync(save);
    }

    /// <summary>Asks before pending edits would be lost. True means go ahead.</summary>
    bool ConfirmLeave(string doing)
    {
        if (_vm.PendingCount == 0)
            return true;
        return MessageBox.Show(this, $"{_vm.PendingCount} pending changes aren't saved. Discard them by {doing}?",
            "Drydock", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    bool _saving;

    async void OnSave(object sender, RoutedEventArgs e) => await SaveOnce();

    async Task SaveOnce()
    {
        if (_saving)
            return;
        _saving = true;
        try
        {
            await Save();
        }
        finally
        {
            _saving = false;
        }
    }

    async void OnStartGame(object sender, RoutedEventArgs e)
    {
        if (_vm.PendingCount > 0)
        {
            var answer = MessageBox.Show(this, $"You have {_vm.PendingCount} unsaved changes. Save them before the game starts?",
                "Drydock", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
                return;
            if (answer == MessageBoxResult.Yes)
            {
                await SaveOnce();
                // a cancelled or failed save keeps the changes, and the game would load the old file
                if (_vm.PendingCount > 0)
                    return;
            }
        }
        AboutWindow.Open(this, AboutInfo.SteamLaunchUrl);
    }

    async Task Save()
    {
        bool writeAnyway = false, allowRunningGame = false;
        while (true)
        {
            var (decision, overrides) = await _vm.CheckSaveAsync(writeAnyway, allowRunningGame);
            switch (decision)
            {
                case SaveDecision.GameRunning:
                    if (!ConfirmRunningGame("Write anyway?"))
                        return;
                    allowRunningGame = true;
                    continue;
                case SaveDecision.Reload:
                    if (MessageBox.Show(this, "The game saved this world since Drydock read it. Writing now would undo that. Reload it? Pending changes are lost.",
                            "Drydock", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                        await _vm.LoadAsync(_vm.LoadedSave!);
                    return;
                case SaveDecision.Confirm:
                    var list = string.Join("\n", overrides.Select(o => "- " + o.Message).Distinct());
                    if (MessageBox.Show(this, $"These changes go past what the game allows:\n\n{list}\n\nWrite anyway?",
                            "Drydock", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    writeAnyway = true;
                    continue;
                case SaveDecision.Write:
                    try
                    {
                        var backup = await _vm.WriteAsync(writeAnyway, allowRunningGame);
                        MessageBox.Show(this, $"Saved. The old version is backed up in\n{backup}", "Drydock", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
                    {
                        MessageBox.Show(this, ex.Message, "Drydock", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
            }
        }
    }

    /// <summary>Asks once, for this write or restore only. True means the user says the world isn't loaded in the game.</summary>
    internal static bool ConfirmRunningGame(Window owner, string question) =>
        MessageBox.Show(owner, $"{SaveWriter.GameRunningReason}\n\nOnly go ahead if the game is at the main menu or has a different world loaded. {question}",
            "Drydock", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    bool ConfirmRunningGame(string question) => ConfirmRunningGame(this, question);

    void OnBackups(object sender, RoutedEventArgs e) =>
        new BackupsWindow(_vm, _vm.SelectedSave!) { Owner = this }.ShowDialog();

    async void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (_vm.PendingCount > 0 && ConfirmLeave("discarding"))
            await _vm.DiscardAsync();
    }

    // rebuilding the tree after an edit clears its selection, the pane keeps showing the last inventory
    void OnTreeSelection(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeNode { Inventory: { } inventory })
            _vm.SelectedInventory = inventory;
    }

    async void OnAmountCommit(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: RowViewModel row } box || _vm.Pane is not { } pane)
            return;
        if (box.Text == row.AmountText)
            return;
        if (!Amounts.TryParse(box.Text, out var amount))
        {
            _vm.ShowEditError($"'{box.Text}' is not an amount.");
            box.Text = row.AmountText;
            return;
        }
        await _vm.ApplyAsync(new SetTotal(pane.Index, row.Item, amount));
    }

    void OnAmountKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            OnAmountCommit(sender, e);
    }

    void OnCopyRows(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        if (_vm.Pane is { } pane)
            CopyText(pane.CopyRows(RowList.SelectedItems.OfType<RowViewModel>()));
    }

    RowViewModel? _menuRow;

    // the row under the mouse, a right-click inside a multi-selection doesn't change SelectedItem
    void OnRowMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _menuRow = (e.OriginalSource as FrameworkElement)?.DataContext as RowViewModel;
        if (_menuRow == null)
            e.Handled = true;
    }

    void OnCopyField(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string field } || _menuRow is not { } row)
            return;
        CopyText(field switch
        {
            "name" => row.Name,
            "key" => row.Key,
            "id" => row.Item.ToString(),
            _ => row.CopyText,
        });
    }

    // the clipboard can be held by another app for a moment
    void CopyText(string text)
    {
        if (text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(text);
            _vm.ShowStatus("Copied");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            _vm.ShowStatus("The clipboard is busy, try again");
        }
    }

    async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RowViewModel { CanRemove: true } row } && _vm.Pane is { } pane)
            await _vm.ApplyAsync(row.Removal(pane.Index));
    }

    async void OnRevert(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RowViewModel { Edit: { } edit } })
            await _vm.RevertAsync(edit);
    }

    async void OnAddItem(object sender, RoutedEventArgs e)
    {
        if (_vm.Pane is not { } pane)
            return;
        var dialog = new AddItemWindow(_vm) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } choice)
            return;
        int target = pane.Index;
        var (accepts, other, message) = await _vm.CheckSlotAsync(target, choice.Item.Guid);
        if (accepts == false)
        {
            var answer = other is { } o
                ? MessageBox.Show(this, $"{message}\n\nYes puts it there. No keeps it here anyway, which the game doesn't do itself.",
                    "Drydock", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)
                : MessageBox.Show(this, $"{message}\n\nAdd it here anyway? The game doesn't do this itself.",
                    "Drydock", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel)
                return;
            if (answer == MessageBoxResult.Yes)
                target = other!.Value;
        }
        if (choice.State is { } state)
            await _vm.ApplyAsync(new AddStacks(target, choice.Item.Guid, (int)choice.Amount, state));
        else
            await _vm.ApplyAsync(new SetTotal(target, choice.Item.Guid, _vm.CurrentTotal(target, choice.Item.Guid) + choice.Amount));
    }

    void OnAbout(object sender, RoutedEventArgs e) =>
        new AboutWindow(Path.GetDirectoryName(Settings.UserPath)!, _vm.Backups.Root) { Owner = this, Icon = Icon }.ShowDialog();

    void OnTheme(object sender, RoutedEventArgs e)
    {
        var theme = (string)((MenuItem)sender).Tag;
        _vm.Theme = theme;
        CheckTheme(theme);
    }

    void CheckTheme(string theme)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(ThemeMenu).OfType<MenuItem>())
            item.IsChecked = (string)item.Tag == theme;
    }
}
