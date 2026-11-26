using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Wallpepper.Ui.Settings;
using Wallpepper.Web;

namespace Wallpepper.Ui;

/// <summary>The inspector's controls for a web wallpaper (in place of the shader properties): address, mute, interact, reload.</summary>
internal sealed class WebPanel
{
    private readonly App _app;
    private readonly StackPanel _host;
    private readonly Action<string, bool> _toast;

    public WebPanel(App app, StackPanel host, Action<string, bool> toast)
    {
        _app = app;
        _host = host;
        _toast = toast;
    }

    /// <summary>Shows the controls for a web wallpaper file; null (or a shader) hides the panel.</summary>
    public void Show(string? path)
    {
        _host.Children.Clear();
        WebInfo? info = path != null && WebInfo.IsWeb(path) ? _app.Library.Metadata(path).Web : null;
        _host.Visibility = info != null ? Visibility.Visible : Visibility.Collapsed;
        if (info == null || path == null)
        {
            return;
        }
        bool ready = _app.Web.IsReady(path);
        bool lifted = ready && _app.Web.Interacting;

        var address = new TextBlock
        {
            Text = info.Error ?? info.Url,
            Foreground = Theme.Brush("Text2"),
            FontFamily = Theme.Font("Mono"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 250,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = info.Url,
        };
        Grid? mute = !info.Audio ? null : SettingsRows.SwitchRow(
            "Mute", "Silences the page. With several displays only the main one plays sound.", _app.Web.IsMuted(path),
            muted => _app.Web.SetMuted(path, muted));

        var buttons = new UniformGrid { Rows = 1, Margin = new Thickness(10, 10, 10, 10) };
        buttons.Children.Add(Button(
            Glyphs.Interact, lifted ? "Done" : "Interact",
            "Bring the page in front so you can click it (press play, pick a station). Done or Esc sends it back behind the icons.",
            ready, () => _app.Web.Interact(path, !lifted)));
        buttons.Children.Add(Button(Glyphs.Reload, "Reload", "Load the page again", ready, () =>
        {
            _app.Web.Reload(path);
            _toast("Reloading " + _app.Library.Title(path), false);
        }));
        buttons.Children.Add(Button(Glyphs.Browser, "Browser", "Open the page in your browser", info.Error == null, () => Shell.OpenUrl(info.Url)));

        _host.Children.Add(SettingsRows.Caption("Web page"));
        _host.Children.Add(SettingsRows.Group(SettingsRows.Row("Address", null, address), mute, buttons));
        _host.Children.Add(SettingsRows.Hint(
            ready
                ? "The desktop itself cannot be clicked. Interact lifts the page in front for a moment; its sound keeps playing when a window covers it."
                : "Set it as wallpaper to load the page. It needs an internet connection.",
            new Thickness(2, -12, 0, 0)));
    }

    private static Button Button(string glyph, string text, string tip, bool enabled, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, Style = Theme.Style("Glyph"), Margin = new Thickness(0, 1, 7, 0) });
        content.Children.Add(new TextBlock { Text = text });
        var button = new Button
        {
            Content = content,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(4, 0, 4, 0),
            ToolTip = tip,
            IsEnabled = enabled,
        };
        button.Click += (_, _) => click();
        return button;
    }
}
