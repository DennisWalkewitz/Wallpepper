using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wallpepper.Library;
using Wallpepper.Settings;
using Wallpepper.Setup;

namespace Wallpepper.Ui.Settings;

/// <summary>Settings > General: start with Windows, the background colour, the library folder, updates, about.</summary>
internal sealed class GeneralPage
{
    private readonly App _app;
    private readonly StackPanel _host;
    private readonly Window _owner;
    private readonly Action<string, bool> _toast;
    private Button? _updateButton;

    public GeneralPage(App app, StackPanel host, Window owner, Action<string, bool> toast)
    {
        _app = app;
        _host = host;
        _owner = owner;
        _toast = toast;
    }

    private Config Config => _app.Settings.Config;

    public void Build()
    {
        _host.Children.Clear();
        _host.Children.Add(SettingsRows.Caption("Startup"));
        _host.Children.Add(SettingsRows.Group(SettingsRows.SwitchRow(
            "Start with Windows", "Starts quietly in the notification area.", Installer.AutostartOrDefault(),
            on =>
            {
                if (!Try.Run("change autostart", () => Installer.Autostart = on))
                {
                    _toast("The setting could not be changed", true);
                }
            })));

        _host.Children.Add(SettingsRows.Caption("Shaders"));
        Color background = ColorPicker.TryParse(Config.Background, out Color parsed) ? parsed : Colors.Black;
        Button swatch = ColorPicker.Swatch(background);
        swatch.Click += (_, _) => ColorPicker.Show(swatch, ColorPicker.TryParse(Config.Background, out Color current) ? current : Colors.Black, colour =>
        {
            ColorPicker.SetSwatch(swatch, colour);
            _app.Settings.Change(SettingsArea.Rendering, config => config.Background = ColorPicker.Hex(colour));
        });
        _host.Children.Add(SettingsRows.Group(
            SettingsRows.Row("Background colour", "What shaders receive as the terminal's “Background”.", swatch)));

        _host.Children.Add(SettingsRows.Caption("Library"));
        _host.Children.Add(SettingsRows.Group(LibraryFolder()));

        _host.Children.Add(SettingsRows.Caption("Updates"));
        _host.Children.Add(SettingsRows.Group(
            SettingsRows.SwitchRow(
                "Check for updates automatically",
                "Once a day Wallpepper asks GitHub for the latest release. Nothing about you or this PC is sent.", Config.CheckUpdates,
                on => _app.Settings.Change(SettingsArea.None, config => config.CheckUpdates = on)),
            SettingsRows.SwitchRow(
                "Install updates automatically", "Off: you are told when a new version is there and decide yourself.", Config.AutoInstallUpdates,
                on => _app.Settings.Change(SettingsArea.None, config => config.AutoInstallUpdates = on))));

        _host.Children.Add(SettingsRows.Caption("About"));
        _host.Children.Add(SettingsRows.Group(About()));
        RefreshUpdate();
    }

    /// <summary>The update state changed: the button in "About" follows.</summary>
    public void RefreshUpdate()
    {
        if (_updateButton == null)
        {
            return;
        }
        var updates = _app.Updates;
        _updateButton.IsEnabled = !updates.Busy;
        if (updates.Busy)
        {
            _updateButton.Content = updates.Available != null ? "Downloading…" : "Checking…";
        }
        else if (updates.Available != null)
        {
            _updateButton.Content = (Installer.IsRunningInstalled ? "Update to " : "Get version ") + updates.Available.Version;
        }
        else
        {
            _updateButton.Content = "Check for updates";
        }
    }

    private StackPanel LibraryFolder()
    {
        var path = SettingsRows.Hint(_app.Library.Folder, new Thickness(0, 4, 0, 0));
        path.FontFamily = Theme.Font("Mono");
        path.FontSize = 11;
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, -6) };
        void Add(string text, string tip, Action click)
        {
            var button = new Button
            {
                Content = text,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = tip,
            };
            button.Click += (_, _) => click();
            buttons.Children.Add(button);
        }

        Add("Open", "Open the folder in File Explorer", () => Shell.OpenFolder(_app.Library.Folder));
        Add("Change…", "Use a different folder", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _app.Library.Folder, Title = "Shader library folder" };
            if (dialog.ShowDialog(_owner) == true)
            {
                UseFolder(dialog.FolderName, "Library folder changed", path);
            }
        });
        Add("Share with Terminal", "Use Windows Terminal's shaders folder, so both see the same files", () =>
        {
            string? folder = ShaderLibrary.TerminalShadersFolder;
            if (folder == null)
            {
                _toast("Windows Terminal was not found", true);
                return;
            }
            UseFolder(folder, "Library now shared with Windows Terminal", path);
        });
        Add("Restore bundled", "Put back any bundled shaders that are missing", () =>
        {
            int restored = _app.Library.RestoreBundled();
            _toast(restored == 0 ? "All bundled shaders are already there" : restored + " bundled shaders restored", false);
        });

        var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        panel.Children.Add(new TextBlock { Text = "Folder", Style = Theme.Style("RowLabel") });
        panel.Children.Add(path);
        panel.Children.Add(buttons);
        return panel;
    }

    private void UseFolder(string folder, string message, TextBlock shownPath)
    {
        try
        {
            _app.Library.SetFolder(folder);
            _app.Shaders.Reload();
            shownPath.Text = _app.Library.Folder;
            _toast(message, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("change the library folder", e);
            _toast("The folder cannot be used: " + e.Message, true);
        }
    }

    private StackPanel About()
    {
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock
        {
            Text = "Wallpepper",
            FontFamily = Theme.Font("UiDisplay"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        });
        name.Children.Add(new TextBlock { Text = "Version " + Installer.VersionText, Foreground = Theme.Brush("Text3"), FontSize = 12 });
        var identity = new StackPanel { Orientation = Orientation.Horizontal };
        identity.Children.Add(new Image { Source = Theme.Image("Logo"), Width = 44, Height = 44, Margin = new Thickness(-4, 0, 8, 0) });
        identity.Children.Add(name);

        _updateButton = new Button
        {
            Height = 30,
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
        };
        _updateButton.Click += (_, _) =>
        {
            if (_app.Updates.Available != null)
            {
                _app.Updates.Install();
            }
            else
            {
                _app.Updates.Check(manual: true);
            }
        };

        TextBlock text = SettingsRows.Hint(
            "Runs Windows Terminal pixel shaders (HLSL) as an animated desktop wallpaper, and costs nothing while the desktop is covered.\n\n"
            + "Shortcuts: Ctrl+Win+Alt+→ / ← next and previous shader, Ctrl+Win+Alt+P pause.\n\n"
            + "Open source under the MIT licence. Some bundled shaders come from Hammster's windows-terminal-shaders collection (CC0). "
            + "Uninstall from Windows Settings › Apps.",
            new Thickness(0, 12, 0, 0));
        text.LineHeight = 17;

        var about = new StackPanel { Margin = new Thickness(14, 14, 14, 14) };
        about.Children.Add(identity);
        about.Children.Add(_updateButton);
        about.Children.Add(text);
        return about;
    }
}
