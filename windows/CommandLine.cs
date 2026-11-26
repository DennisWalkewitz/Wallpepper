namespace Wallpepper;

/// <summary>What the exe was asked to do.</summary>
internal enum CommandMode
{
    /// <summary>Run the tray application (or offer the setup first).</summary>
    RunApp,

    /// <summary>"--exit": ask a running instance to quit.</summary>
    Exit,

    /// <summary>"--check &lt;shaderDir&gt; &lt;outFile&gt;": compile every shader and measure its GPU cost.</summary>
    CheckShaders,

    /// <summary>"--bench [--quick] [--json] [--out FILE] [--size WxH]": the GPU benchmark.</summary>
    Bench,

    /// <summary>"--update-check": print what the latest release is.</summary>
    UpdateCheck,

    /// <summary>"--update-fetch": download and verify the latest release.</summary>
    UpdateFetch,

    /// <summary>"--apply-update &lt;exe&gt; &lt;sha256&gt;": replace the installed exe.</summary>
    ApplyUpdate,

    /// <summary>"--uninstall [--quiet]"</summary>
    Uninstall,

    /// <summary>"--selftest [--out FILE] [--only NAME]": check on the real desktop that wallpapers can be set.</summary>
    SelfTest,
}

/// <summary>The parsed command line. Unknown arguments are ignored.</summary>
internal sealed record CommandLine
{
    public CommandMode Mode { get; private init; } = CommandMode.RunApp;

    /// <summary>The values that follow a mode switch ("--check DIR FILE", "--apply-update EXE SHA").</summary>
    public IReadOnlyList<string> Values { get; private init; } = Array.Empty<string>();

    /// <summary>"--tray": start without the library window.</summary>
    public bool Tray { get; private init; }

    /// <summary>"--portable": run from where the exe is, without offering the setup.</summary>
    public bool Portable { get; private init; }

    /// <summary>"--install": install or update without asking.</summary>
    public bool Install { get; private init; }

    /// <summary>"--autostart" (with "--install"): start with Windows.</summary>
    public bool Autostart { get; private init; }

    /// <summary>"--updated": this start follows an update.</summary>
    public bool Updated { get; private init; }

    public bool Quiet { get; private init; }

    // ---- benchmark
    public bool Quick { get; private init; }

    public bool Json { get; private init; }

    /// <summary>"--progress": machine-readable progress lines (used by the settings page).</summary>
    public bool Progress { get; private init; }

    /// <summary>"--out FILE"</summary>
    public string? OutFile { get; private init; }

    /// <summary>"--size WxH"</summary>
    public string? Size { get; private init; }

    /// <summary>"--shader FILE": test the benchmark machinery with another shader.</summary>
    public string? ShaderFile { get; private init; }

    /// <summary>"--only NAME": run one part of the self-test.</summary>
    public string? Only { get; private init; }

    private static readonly (string Switch, CommandMode Mode, int ValueCount)[] Modes =
    {
        ("--exit", CommandMode.Exit, 0),
        ("--check", CommandMode.CheckShaders, 2),
        ("--bench", CommandMode.Bench, 0),
        ("--update-check", CommandMode.UpdateCheck, 0),
        ("--update-fetch", CommandMode.UpdateFetch, 0),
        ("--apply-update", CommandMode.ApplyUpdate, 2),
        ("--uninstall", CommandMode.Uninstall, 0),
        ("--selftest", CommandMode.SelfTest, 0),
    };

    public static CommandLine Parse(string[] args)
    {
        bool Has(string name) => Array.IndexOf(args, name) >= 0;
        string? ValueOf(string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var mode = CommandMode.RunApp;
        IReadOnlyList<string> values = Array.Empty<string>();
        foreach (var (name, candidate, valueCount) in Modes)
        {
            int index = Array.IndexOf(args, name);
            if (index >= 0)
            {
                mode = candidate;
                values = args.Skip(index + 1).Take(valueCount).ToArray();
                break;
            }
        }

        return new CommandLine
        {
            Mode = mode,
            Values = values,
            Tray = Has("--tray"),
            Portable = Has("--portable"),
            Install = Has("--install"),
            Autostart = Has("--autostart"),
            Updated = Has("--updated"),
            Quiet = Has("--quiet"),
            Quick = Has("--quick"),
            Json = Has("--json"),
            Progress = Has("--progress"),
            OutFile = ValueOf("--out"),
            Size = ValueOf("--size"),
            ShaderFile = ValueOf("--shader"),
            Only = ValueOf("--only"),
        };
    }
}
