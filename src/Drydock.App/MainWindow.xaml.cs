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

    void OnTreeSelection(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.SelectedInventory = (e.NewValue as TreeNode)?.Inventory;

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
