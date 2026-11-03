using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Vault;

// 2026-10-10 vault frame fill (v1.5.1): tracks the frames (iframes, nested too) inside one pane's CoreWebView2
// and runs a script only in frames whose current origin equals an approved https origin. The vault decides
// which origins are approved (card host or its allowed sites); the injected script re-checks location.origin
// inside the frame. Nothing here logs or stores page data.
internal sealed class VaultFrames
{
    private sealed class Entry
    {
        public CoreWebView2Frame Frame = null!;
        public string Url = "";
    }

    private readonly List<Entry> _frames = new();
    private const int MaxFrames = 64;

    /// <summary>Raised when a frame finished a successful navigation; argument is its URL.</summary>
    public event Action<string>? FrameLoaded;

    public void Attach(CoreWebView2 core)
    {
        core.FrameCreated += (_, e) => Track(e.Frame);
    }

    private void Track(CoreWebView2Frame frame)
    {
        if (_frames.Count >= MaxFrames) return;
        var en = new Entry { Frame = frame };
        _frames.Add(en);
        frame.NavigationStarting += (_, a) => en.Url = a.Uri ?? "";
        frame.NavigationCompleted += (_, a) =>
        {
            if (a.IsSuccess && _frames.Contains(en)) FrameLoaded?.Invoke(en.Url);
        };
        frame.Destroyed += (_, _) => _frames.Remove(en);
        try { frame.FrameCreated += (_, e) => Track(e.Frame); } catch { }
    }

    public static string? Origin(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps) return null;
        return "https://" + u.IdnHost.ToLowerInvariant() + (u.IsDefaultPort ? "" : ":" + u.Port);
    }

    /// <summary>Current https frame URLs whose origin differs from the top page (one per origin).</summary>
    public List<string> OtherOriginUrls(string? topUrl)
    {
        var top = Origin(topUrl);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var en in _frames)
        {
            var o = Origin(en.Url);
            if (o == null || o == top || !seen.Add(o)) continue;
            list.Add(en.Url);
        }
        return list;
    }

    /// <summary>Runs the script in every live frame whose tracked origin equals <paramref name="origin"/>.
    /// Returns the largest number the script returned, or null when no frame had that origin.</summary>
    public async Task<int?> ExecuteInOriginAsync(string origin, string script)
    {
        int? best = null;
        foreach (var en in _frames.ToArray())
        {
            if (!string.Equals(Origin(en.Url), origin, StringComparison.Ordinal)) continue;
            int n;
            try
            {
                var r = await en.Frame.ExecuteScriptAsync(script);
                n = int.TryParse(r, out var v) ? v : 0;
            }
            catch { continue; } // frame went away
            if (n < 0) continue; // origin changed under us: the in-frame check refused
            best = Math.Max(best ?? 0, n);
        }
        return best;
    }
}
