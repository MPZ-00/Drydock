using System.Windows;
using System.Windows.Controls;

namespace Drydock.App;

/// <summary>Shows an unexpected error with a way to copy the details for a bug report. The app keeps running.</summary>
static class ErrorWindow
{
    public static void Show(Window? owner, Exception error)
    {
        var details = error.ToString();
        var copy = new Button { Content = "Copy details", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsDefault = true, IsCancel = true };
        var window = new Window
        {
            Title = "Something went wrong",
            Owner = owner?.IsVisible == true ? owner : null,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner?.IsVisible == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { copy, close } },
                },
            },
        };
        copy.Click += (_, _) => Clipboard.SetText(details);
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }
}
