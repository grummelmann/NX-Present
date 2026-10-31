using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NXPresent;

/// <summary>A small modal input window in the look of the application.</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox input;
    private PromptWindow(string title, string label, string initial, bool multiline)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        input = new TextBox { Text = initial, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = multiline ? 130 : double.NaN, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled, Margin = new Thickness(0, 6, 0, 18) };
        var ok = new Button { Content = "Hinzufügen", IsDefault = !multiline, Style = (Style)Application.Current.Resources["PrimaryButton"], MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Children = { new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["Muted"], Margin = new Thickness(0, 12, 0, 0) }, input, buttons }
        };
        Background = (Brush)Application.Current.Resources["Panel"];
        Foreground = (Brush)Application.Current.Resources["Ink"];
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }

    /// <summary>Returns the entered text, or null if the user cancelled.</summary>
    public static string? Ask(Window owner, string title, string label, string initial = "", bool multiline = false)
    {
        var window = new PromptWindow(title, label, initial, multiline) { Owner = owner };
        return window.ShowDialog() == true ? window.input.Text : null;
    }
}
