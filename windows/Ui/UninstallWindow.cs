using System.Windows;
using System.Windows.Controls;
using Wallpepper.Setup;

namespace Wallpepper.Ui;

/// <summary>Uninstall confirmation with the choice to keep the library and settings.</summary>
internal sealed class UninstallWindow : AppDialog
{
    private readonly CheckBox _keep = new CheckBox { IsChecked = true };

    public UninstallWindow() : base("Remove Wallpepper", 500, 330)
    {
        Body.Children.Add(Header("Remove Wallpepper?", "Version " + (Installer.InstalledVersion ?? Installer.VersionText)));
        Body.Children.Add(Paragraph(
            "Wallpepper is closed and your normal wallpaper comes back. The program, its Start menu entry and the start with Windows are removed."));
        Body.Children.Add(SwitchGroup(
            "Keep my library and settings",
            "Off: settings and Wallpepper's own shader folder are deleted too. A folder shared with Windows Terminal is never touched.",
            _keep));

        var cancel = new Button { Content = "Cancel", MinWidth = 88, Height = 36, IsDefault = true };
        cancel.Click += (_, _) => Close();
        var remove = new Button
        {
            Style = Theme.Style("DangerBtn"),
            Content = "Remove",
            MinWidth = 96,
            Height = 36,
            Margin = new Thickness(8, 0, 0, 0),
        };
        remove.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 24, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(remove);
        Body.Children.Add(buttons);
        Loaded += (_, _) =>
        {
            Activate();
            cancel.Focus();
        };
    }

    public bool Confirmed { get; private set; }

    public bool KeepData => _keep.IsChecked == true;
}
