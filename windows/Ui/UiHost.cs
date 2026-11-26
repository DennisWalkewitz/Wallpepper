using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wallpepper.Settings;

namespace Wallpepper.Ui;

/// <summary>
/// What the user interface needs from WPF, set up in two steps so that a Wallpepper that only sits in the tray loads as
/// little of it as possible: the message loop's basics at start, the application object with the theme when the first
/// window or menu is about to show.
/// </summary>
internal static class UiHost
{
    private static bool _themed;
    private static bool _errorShown;

    /// <summary>Prepares the calling thread to be the UI thread. Returns its dispatcher; run it with Dispatcher.Run().</summary>
    public static Dispatcher Start()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        // awaits anywhere on this thread come back to it, also before the message loop has started
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.UnhandledException += OnUnhandledException;
        return dispatcher;
    }

    /// <summary>Makes sure the application object and the theme's resources exist. Call before creating any WPF element.</summary>
    public static void EnsureTheme()
    {
        if (_themed)
        {
            return;
        }
        _themed = true;
        // The window is drawn on the CPU. It is small and mostly still, and this way it neither needs a second graphics
        // device (which costs a few hundred megabytes with some drivers) nor waits behind a heavy shader for the GPU.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Wallpepper;component/Ui/Theme.xaml", UriKind.Relative),
        });
    }

    /// <summary>
    /// An exception nobody caught on the UI thread. The wallpaper keeps running (a tray app that vanishes is worse than
    /// one with a broken button); the error is logged, and the user is told once.
    /// </summary>
    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("ui", e.Exception);
        e.Handled = true;
        if (!_errorShown)
        {
            _errorShown = true;
            MessageDialog.Show(
                "Something went wrong",
                "Wallpepper ran into an error and carries on. If something no longer works, quit it from the tray and start it again. "
                + "Details are in log.txt in " + AppPaths.DataDir + ".",
                "Close");
        }
    }
}
