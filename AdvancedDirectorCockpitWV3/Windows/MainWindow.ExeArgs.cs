using System;
using System.IO;
using System.Text.Json;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-10-09: start arguments for an embedded program. A card may set "args" (used as-is) or
// "profile" (Chrome/Edge profile folder). Without either, Chrome and Edge start with the last used
// profile and --new-window, so the profile picker never shows and the new window lands in the pane.
public partial class MainWindow
{
    private static string EmbedArgs(string exe, string cardArgs, string cardProfile)
    {
        // 2026-11-01: card args that open an HTML/URL in Chrome/Edge keep --new-window so embed can
        // capture a new window; if Chrome hands off anyway, EmbeddedApps opens the doc in the running instance.
        if (!string.IsNullOrWhiteSpace(cardArgs))
        {
            var a = cardArgs.Trim();
            if (BrowserUserData(exe) != null
                && a.IndexOf("--new-window", StringComparison.OrdinalIgnoreCase) < 0
                && LooksLikeBrowserDocumentArgs(a))
                return "--new-window " + a;
            return a;
        }
        var userData = BrowserUserData(exe);
        if (userData == null) return "";
        var profile = string.IsNullOrWhiteSpace(cardProfile) ? LastUsedProfile(userData) : cardProfile.Trim();
        var args = "--new-window";
        if (!string.IsNullOrWhiteSpace(profile))
            args = "--profile-directory=\"" + profile.Replace("\"", "") + "\" " + args;
        return args;
    }

    private static bool LooksLikeBrowserDocumentArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return false;
        var lower = args.ToLowerInvariant();
        if (lower.Contains(".html") || lower.Contains(".htm")) return true;
        if (lower.Contains("http://") || lower.Contains("https://") || lower.Contains("file:")) return true;
        return false;
    }

    private static string? BrowserUserData(string exe)
    {
        var name = Path.GetFileName(exe).ToLowerInvariant();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (name == "chrome.exe") return Path.Combine(local, "Google", "Chrome", "User Data");
        if (name == "msedge.exe") return Path.Combine(local, "Microsoft", "Edge", "User Data");
        return null;
    }

    // "profile.last_used" in User Data\Local State (read only).
    private static string? LastUsedProfile(string userData)
    {
        try
        {
            var path = Path.Combine(userData, "Local State");
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            if (doc.RootElement.TryGetProperty("profile", out var p)
                && p.TryGetProperty("last_used", out var lu) && lu.ValueKind == JsonValueKind.String)
            {
                var v = lu.GetString();
                if (!string.IsNullOrWhiteSpace(v) && Directory.Exists(Path.Combine(userData, v))) return v;
            }
            return Directory.Exists(Path.Combine(userData, "Default")) ? "Default" : null;
        }
        catch
        {
            return null;
        }
    }
}
