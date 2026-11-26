using Wallpepper.Settings;

namespace Wallpepper.SelfTesting;

/// <summary>The self-test's result file: one line per check, notes in between, a summary at the end.</summary>
internal sealed class SelfTestReport
{
    private readonly List<string> _lines = new List<string>();
    private readonly string? _outFile;

    public SelfTestReport(string? outFile)
    {
        _outFile = outFile;
    }

    public int Passed { get; private set; }

    public int Failed { get; private set; }

    /// <summary>A check: name | OK or FAIL | detail.</summary>
    public void Check(string name, bool ok, string detail)
    {
        if (ok)
        {
            Passed++;
        }
        else
        {
            Failed++;
        }
        _lines.Add($"{name} | {(ok ? "OK" : "FAIL")} | {detail}");
        Save(_outFile + ".partial");
    }

    /// <summary>A line that is information, not a check.</summary>
    public void Note(string text)
    {
        _lines.Add(text);
        Save(_outFile + ".partial");
    }

    public void InsertHeader(string text) => _lines.Insert(0, text);

    public void Finish(string summary)
    {
        _lines.Add("");
        _lines.Add(summary);
        Save(_outFile);
        if (_outFile != null)
        {
            Try.Run("remove the partial self-test result", () => File.Delete(_outFile + ".partial"));
        }
    }

    private void Save(string? file)
    {
        if (_outFile != null && file != null)
        {
            Try.Run("write the self-test result", () => File.WriteAllLines(file, _lines));
        }
    }
}
