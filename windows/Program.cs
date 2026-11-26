using Wallpepper.Commands;
using Wallpepper.Settings;
using Wallpepper.Setup;

namespace Wallpepper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);

        var commandLine = CommandLine.Parse(args);
        return commandLine.Mode switch
        {
            CommandMode.Exit => ExitRunningInstance(),
            CommandMode.CheckShaders => CheckShadersCommand.Run(commandLine),
            CommandMode.Bench => BenchCommand.Run(commandLine),
            CommandMode.UpdateCheck => UpdateCommands.Check(),
            CommandMode.UpdateFetch => UpdateCommands.Fetch(),
            CommandMode.ApplyUpdate => UpdateCommands.Apply(commandLine),
            _ => RunAppCommand.Run(commandLine),
        };
    }

    /// <summary>"--exit": asks a running instance to quit (used by updates and scripts).</summary>
    private static int ExitRunningInstance()
    {
        SingleInstance.SignalExit();
        return 0;
    }
}
