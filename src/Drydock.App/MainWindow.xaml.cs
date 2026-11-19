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
        Closed += (_, _) =>
        {
            _gamePoll.Stop();
            _vm.Dispose();
        };
    }

    async void OnSaveChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.SelectedSave is { } save)
            await _vm.LoadAsync(save);
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

    async void OnRemoveStack(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RowViewModel { Stack: { } stack } } && _vm.Pane is { } pane)
            await _vm.ApplyAsync(new RemoveStack(pane.Index, stack));
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
