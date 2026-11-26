using System.Threading;
using System.Windows.Threading;
using Wallpepper.SelfTesting;
using Wallpepper.Settings;
using Wallpepper.Setup;
using Wallpepper.Ui;

namespace Wallpepper.Commands;

/// <summary>
/// Everything with a user interface: the setup and uninstall dialogs, the tray application, the self-test. Makes sure
/// only one instance runs and reports every unhandled error.
/// </summary>
internal static class RunAppCommand
{
    private const int SelfTestTakeoverMs = 8000;

    public static int Run(CommandLine args)
    {
        try
        {
            Dispatcher ui = UiHost.Start();
            if (args.Mode == CommandMode.Uninstall)
            {
                Installer.Uninstall(args.Quiet);
                return 0;
            }

            bool selfTest = args.Mode == CommandMode.SelfTest;
            // the self-test takes the place of a running instance and starts it again afterwards
            string? resume = selfTest ? ProcessControl.StopRunningInstance() : null;
            if (!selfTest && Installer.RunSetupIfNeeded(args))
            {
                return 0;
            }

            using var instance = SingleInstance.TryAcquire(selfTest ? SelfTestTakeoverMs : 0);
            if (instance == null)
            {
                SingleInstance.SignalShow();   // already running: bring up its library window
                return 0;
            }

            var app = new App(instance, showLibrary: !args.Tray && !selfTest, justUpdated: args.Updated);
            if (selfTest)
            {
                ui.InvokeAsync(() => SelfTest.Start(app, args.OutFile, args.Only, resume));
            }
            Dispatcher.Run();
            GC.KeepAlive(app);
            return Environment.ExitCode;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("startup", e);
            return 1;
        }
    }
}
