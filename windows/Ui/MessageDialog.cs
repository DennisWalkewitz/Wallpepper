using System.Windows;
using System.Windows.Controls;

namespace Wallpepper.Ui;

/// <summary>A message with one button, for when there is no library window to show it in (install errors).</summary>
internal sealed class MessageDialog : AppDialog
{
    private MessageDialog(string title, string text, string button) : base("Wallpepper", 460, 250)
    {
        Body.Children.Add(Header(title, null));
        Body.Children.Add(Paragraph(text));
        var ok = new Button
        {
            Style = Theme.Style("Primary"),
            Content = button,
            MinWidth = 96,
            Height = 36,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 28, 24),
        };
        ok.Click += (_, _) => Close();
        Root.Children.Add(ok);
        Loaded += (_, _) => Activate();
    }

    public static void Show(string title, string text, string button) => new MessageDialog(title, text, button).ShowDialog();
}
