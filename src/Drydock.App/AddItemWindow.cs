using System.Windows;
using System.Windows.Controls;
using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App;

/// <param name="State">How new stacks start, for items with their own state. Null for plain items.</param>
sealed record AddChoice(ItemInfo Item, decimal Amount, StackState? State);

/// <summary>Picks an item from the game's catalog, an amount, and for items with state how the new stacks start.</summary>
sealed class AddItemWindow : Window
{
    readonly MainViewModel _vm;
    readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly ListBox _items = new() { Height = 320, ItemTemplate = ItemLook() };
    readonly TextBox _amount = new() { Text = "1", Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _statePanel = new() { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    readonly RadioButton _new = new() { Content = "New (the game's default, a full cell or magazine)", IsChecked = true, GroupName = "state" };
    readonly RadioButton _copy = new() { Content = "Copy of", GroupName = "state" };
    readonly ComboBox _copyFrom = new() { MinWidth = 300, Margin = new Thickness(24, 2, 0, 4) };
    readonly RadioButton _empty = new() { Content = "Empty", GroupName = "state" };
    readonly RadioButton _custom = new() { Content = "Custom", GroupName = "state" };
    readonly TextBox _customValue = new() { Width = 80, Margin = new Thickness(24, 2, 6, 0) };
    readonly TextBlock _customUnit = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public AddChoice? Result { get; private set; }

    // the same picture, name and dimmer key as the pane, so an item can be found by its look
    static DataTemplate ItemLook()
    {
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(MarginProperty, new Thickness(0, 1, 0, 1));
        var icon = new FrameworkElementFactory(typeof(Image));
        icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(ItemInfo.Icon)) { Converter = new IconConverter() });
        icon.SetValue(WidthProperty, 28.0);
        icon.SetValue(HeightProperty, 28.0);
        icon.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        var text = new FrameworkElementFactory(typeof(StackPanel));
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ItemInfo.Name)));
        var key = new FrameworkElementFactory(typeof(TextBlock));
        key.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ItemInfo.Key)));
        key.SetValue(TextBlock.FontSizeProperty, 11.0);
        key.SetValue(OpacityProperty, 0.6);
        text.AppendChild(name);
        text.AppendChild(key);
        row.AppendChild(icon);
        row.AppendChild(text);
        return new DataTemplate { VisualTree = row };
    }

    public AddItemWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = "Add item";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _search.TextChanged += (_, _) => Filter();
        _items.SelectionChanged += (_, _) => ItemChosen();
        Filter();

        var ok = new Button { Content = "Add", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 4, 12, 4) };
        ok.Click += (_, _) => Confirm();

        _statePanel.Children.Add(new TextBlock { Text = "New stacks start as", Margin = new Thickness(0, 0, 0, 4) });
        _statePanel.Children.Add(_new);
        _statePanel.Children.Add(_copy);
        _statePanel.Children.Add(_copyFrom);
        _statePanel.Children.Add(_empty);
        _statePanel.Children.Add(_custom);
        _statePanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _customValue, _customUnit } });
        _copyFrom.SelectionChanged += (_, _) => _copy.IsChecked = true;
        _customValue.GotFocus += (_, _) => _custom.IsChecked = true;

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "Search", Margin = new Thickness(0, 0, 0, 4) },
                _search,
                _items,
                new TextBlock { Text = "Amount to add", Margin = new Thickness(0, 12, 0, 4) },
                _amount,
                _statePanel,
                _error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { ok, cancel } },
            },
        };
        Loaded += (_, _) => _search.Focus();
    }

    void Filter()
    {
        var query = _search.Text.Trim();
        _items.ItemsSource = _vm.Catalog
            .Where(i => query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (_items.Items.Count == 1)
            _items.SelectedIndex = 0;
    }

    void ItemChosen()
    {
        _error.Text = "";
        if (_items.SelectedItem is not ItemInfo item || !_vm.StatefulItems.Contains(item.Guid))
        {
            _statePanel.Visibility = Visibility.Collapsed;
            return;
        }
        _statePanel.Visibility = Visibility.Visible;
        _copyFrom.ItemsSource = _vm.StacksOf(item.Guid)
            .Select(s => new ComboBoxItem { Content = $"{s.Inventory.Ref}  {s.Row.State}", Tag = new CopyState(s.Inventory.Index, s.Row.Stack!.Value) })
            .ToList();
        _copyFrom.SelectedIndex = -1;
        _new.IsChecked = true;
        bool hasCodec = _vm.States.TryGetValue(item.Guid, out var info);
        _empty.IsEnabled = _custom.IsEnabled = _customValue.IsEnabled = hasCodec;
        _customUnit.Text = !hasCodec ? "can't be set for this item" : info!.Percent ? "%" : $"of {info.Max:0} rounds";
    }

    void Confirm()
    {
        if (_items.SelectedItem is not ItemInfo item)
        {
            _error.Text = "Pick an item.";
            return;
        }
        if (!Amounts.TryParse(_amount.Text, out var amount) || amount <= 0)
        {
            _error.Text = $"'{_amount.Text}' is not an amount.";
            return;
        }
        StackState? state = null;
        if (_vm.StatefulItems.Contains(item.Guid))
        {
            if (amount != decimal.Truncate(amount))
            {
                _error.Text = "This item comes one per stack, use a whole number.";
                return;
            }
            var info = _vm.States.GetValueOrDefault(item.Guid);
            if (_copy.IsChecked == true)
            {
                if ((_copyFrom.SelectedItem as ComboBoxItem)?.Tag is not CopyState copy)
                {
                    _error.Text = "Pick the stack to copy.";
                    return;
                }
                state = copy;
            }
            else if (_empty.IsChecked == true)
                state = new ValueState(0);
            else if (_custom.IsChecked == true)
            {
                if (info == null || !Amounts.TryParse(_customValue.Text, out var value))
                {
                    _error.Text = $"'{_customValue.Text}' is not a value.";
                    return;
                }
                state = new ValueState(info.Percent ? value / 100 * info.Max : value);
            }
            else
                state = new NewState();
        }
        Result = new AddChoice(item, amount, state);
        DialogResult = true;
    }
}
