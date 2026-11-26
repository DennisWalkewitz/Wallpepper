using Microsoft.Web.WebView2.Core;
using Wallpepper.Settings;

namespace Wallpepper.Web;

/// <summary>
/// What a web wallpaper may do. It runs unattended behind the desktop, so it gets far less than a browser tab: it
/// stays on its own site, cannot open windows or other programs by itself, and gets no permissions, downloads,
/// dialogs, autofill or host access. Links the user clicks while interacting open in the default browser.
/// </summary>
internal static class WebViewPolicy
{
    /// <param name="info">the wallpaper's file (its address names the one site the page may be on)</param>
    /// <param name="isInteracting">whether the user has lifted the page in front right now</param>
    public static void Apply(CoreWebView2 web, WebInfo info, Func<bool> isInteracting)
    {
        var settings = web.Settings;
        settings.AreDevToolsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.IsSwipeNavigationEnabled = false;

        // a wallpaper gets no say about the microphone, location, notifications or downloads
        web.PermissionRequested += (_, e) =>
        {
            e.State = CoreWebView2PermissionState.Deny;
            e.Handled = true;
        };
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;

        // WebView2 has no popup blocker of its own: a new window only opens (in the browser) for the user's own click
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.IsUserInitiated && isInteracting())
            {
                Shell.OpenUrl(e.Uri);
            }
        };

        string home = info.Host;
        web.NavigationStarting += (_, e) =>
        {
            if (!WebInfo.IsAllowedAddress(e.Uri))
            {
                e.Cancel = true;
                return;
            }
            bool sameSite = Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) && SameSite(target.Host, home);
            bool userWentThere = e.IsUserInitiated && isInteracting();
            if (!sameSite && !userWentThere)
            {
                e.Cancel = true;
                Log.Write("web: navigation to " + (target?.Host ?? "another site") + " was blocked");
            }
        };
    }

    /// <summary>The same host, or a subdomain of it (a page may load its own "www." or "app." variant).</summary>
    public static bool SameSite(string host, string home) =>
        string.Equals(host, home, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + home, StringComparison.OrdinalIgnoreCase)
        || home.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
}
