using System.Drawing;

namespace Wallpepper.Web;

/// <summary>One page the app wants on screen: on a single display, or across all of them in span mode.</summary>
/// <param name="Key">display id, or <see cref="SpanKey"/></param>
/// <param name="Path">the .wallpepperweb file</param>
/// <param name="Bounds">where the page goes, in screen coordinates</param>
/// <param name="DisplayIds">the displays it covers</param>
/// <param name="Primary">it is on the primary display (which plays the sound when several pages have some)</param>
internal sealed record WebPageRequest(string Key, string Path, WebInfo Info, Rectangle Bounds, IReadOnlyList<string> DisplayIds, bool Primary)
{
    public const string SpanKey = "span";
}

/// <summary>How far a page has come.</summary>
internal enum WebPageState
{
    NotStarted,
    Starting,
    Loading,
    Loaded,
    Failed,
}
