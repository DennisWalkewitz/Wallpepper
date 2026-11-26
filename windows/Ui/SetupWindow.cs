using System.Windows;
using System.Windows.Controls;
using Wallpepper.Setup;

namespace Wallpepper.Ui;

internal enum SetupChoice
{
    Cancel,
    Install,
    JustRun,
}

/// <summary>Install / update dialog shown when the exe is started from outside its install folder.</summary>
internal sealed class SetupWindow : AppDialog
{
    private readonly CheckBox _autostart = new CheckBox();

    /// <param name="installedVersion">version of the copy that is already installed, null for a first install</param>
    public SetupWindow(string? installedVersion) : base("Wallpepper setup", 500, 356)
    {
        string version = Installer.VersionText;
        bool upgrade = installedVersion != null;
        bool same = installedVersion == version;
        string title = !upgrade ? "Install Wallpepper" : same ? "Reinstall Wallpepper" : "Update Wallpepper";
        string action = !upgrade ? "Install Wallpepper" : same ? "Reinstall" : "Update to " + version;
        string text;
        if (!upgrade)
        {
            text = "Installs for you only, without administrator rights. You can remove it any time from Windows Settings › Apps.";
        }
        else if (same)
        {
            text = $"Version {installedVersion} is already installed. This replaces it with this copy and restarts it. Your library and settings are kept.";
        }
        else
        {
            text = $"Replaces the installed version {installedVersion} with {version} and restarts it. Your library and settings are kept.";
        }

        Body.Children.Add(Header(title, "Animated shader wallpapers  ·  version " + version));
        Body.Children.Add(Paragraph(text));
        _autostart.IsChecked = !upgrade || Installer.AutostartOrDefault();
        Body.Children.Add(SwitchGroup("Start with Windows", "Starts quietly in the notification area.", _autostart));

        // two clear choices; closing the window (or Esc) cancels
        var run = new Button
        {
            Content = "Run without installing",
            HorizontalAlignment = HorizontalAlignment.Left,
            Height = 40,
            Padding = new Thickness(16, 0, 16, 0),
            ToolTip = "Try it from where it is. Nothing is copied or registered.",
        };
        run.Click += (_, _) => Choose(SetupChoice.JustRun);
        var install = new Button
        {
            Style = Theme.Style("Primary"),
            Content = action,
            MinWidth = 150,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
        };
        install.Click += (_, _) => Choose(SetupChoice.Install);
        var buttons = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(run);
        buttons.Children.Add(install);
        Body.Children.Add(buttons);
        Loaded += (_, _) =>
        {
            Activate();
            install.Focus();
        };
    }

    public SetupChoice Choice { get; private set; } = SetupChoice.Cancel;

    public bool StartWithWindows => _autostart.IsChecked == true;

    private void Choose(SetupChoice choice)
    {
        Choice = choice;
        Close();
    }
}
