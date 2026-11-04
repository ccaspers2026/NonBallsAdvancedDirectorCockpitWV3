using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Windows;

// Open-group windows, shared WebView2 profile, monitor placement, and freshness title watch.
public partial class MainWindow
{
    // appsettings Cockpit:PubExePath / env Cockpit__PubExePath (relative → next to this app).
    // Unset no-ops, except a process already running from a publish folder is that exe.
    private static string PubExePath => MachineLocalPaths.ResolvePubExe(
        AdcSettings.Cockpit.PubExePath, AppContext.BaseDirectory, Environment.ProcessPath);

    private static readonly string[] AssetFileNames =
    {
        "advanced-director-cockpit.html",
        "advanced-director-cockpit.css",
        "advanced-director-cockpit.js"
    };

    private static CoreWebView2Environment? SharedEnvironment;
    private static readonly object EnvLock = new();

    private DispatcherTimer? _freshTimer;
    private DateTime _ownExeStamp;
    private DateTime _assetStamp;
    private string _baseTitle = "";
    private bool _reloadPending;

    private async Task<CoreWebView2Environment> EnsureSharedEnvironmentAsync(string userData)
    {
        lock (EnvLock)
        {
            if (SharedEnvironment != null)
                return SharedEnvironment;
        }
        var env = await CoreWebView2Environment.CreateAsync(null, userData);
        lock (EnvLock)
        {
            SharedEnvironment ??= env;
            return SharedEnvironment;
        }
    }

    private string BuildStartUrl()
    {
        var url = "https://" + VirtualHost + "/advanced-director-cockpit.html?v=" + DateTime.UtcNow.Ticks;
        if (!string.IsNullOrWhiteSpace(_bootConfigId))
            url += "&config=" + Uri.EscapeDataString(_bootConfigId);
        if (!string.IsNullOrWhiteSpace(_soloSpace))
            url += "&solo=" + Uri.EscapeDataString(_soloSpace);
        return url;
    }

    private void ApplyOpenConfigWindows(JsonElement root)
    {
        if (!root.TryGetProperty("ids", out var idsEl) || idsEl.ValueKind != JsonValueKind.Array)
            return;
        var ids = new List<string>();
        foreach (var item in idsEl.EnumerateArray())
        {
            var id = item.GetString();
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }
        if (ids.Count == 0)
        {
            _ = ReplyOpenGroupAsync(0, Array.Empty<string>());
            return;
        }

        var monitors = ListMonitorWorkAreas();
        var opened = 0;
        var failed = new List<string>();
        foreach (var id in ids)
        {
            try
            {
                var child = new Windows.MainWindow(id);
                PlaceAcrossMonitors(child, opened, monitors);
                child.Show();
                opened++;
            }
            catch
            {
                failed.Add(id);
            }
        }
        _ = ReplyOpenGroupAsync(opened, failed);
    }

    private async Task ReplyOpenGroupAsync(int opened, IReadOnlyList<string> failed)
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = "adc-open-config-windows-result",
                opened,
                failed
            });
            // Escape for embedding in a JS string literal.
            var js = "window.dispatchEvent(new CustomEvent('adc-host',{detail:" + payload + "}));";
            await core.ExecuteScriptAsync(js);
        }
        catch
        {
            // Page also toasts after posting the request.
        }
    }

    private void PlaceAcrossMonitors(Window window, int index, List<Rect> monitors)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = Math.Min(1680, ActualWidth > 100 ? ActualWidth : 1680);
        window.Height = Math.Min(960, ActualHeight > 100 ? ActualHeight : 960);
        if (monitors.Count == 0)
        {
            window.Left = Left + 32 * (index + 1);
            window.Top = Top + 32 * (index + 1);
            return;
        }
        var m = monitors[index % monitors.Count];
        var cascade = (index / monitors.Count) * 36;
        var left = m.X + 24 + cascade;
        var top = m.Y + 24 + cascade;
        var maxLeft = m.X + Math.Max(0, m.Width - window.Width - 24);
        var maxTop = m.Y + Math.Max(0, m.Height - window.Height - 24);
        window.Left = Math.Min(left, maxLeft);
        window.Top = Math.Min(top, maxTop);
    }

    private static List<Rect> ListMonitorWorkAreas()
    {
        var list = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMon, hdc, lprc, dw) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(hMon, ref info))
                {
                    var r = info.Work;
                    list.Add(new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                }
                return true;
            }, IntPtr.Zero);
        list.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        return list;
    }

    private void StartFreshnessWatch()
    {
        // 2026-10-09: every cockpit window (main, New window, detached tab) shows the build version (AdcVersion.cs).
        _baseTitle = AdcVersion.Titled(string.IsNullOrWhiteSpace(_bootConfigId) ? Title : Title + " [" + _bootConfigId + "]");
        Title = _baseTitle;
        _ownExeStamp = FileStamp(Environment.ProcessPath);
        _assetStamp = AssetStamp(_www);
        _freshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _freshTimer.Tick += (_, _) => CheckFreshness();
        _freshTimer.Start();
        CheckFreshness();
    }

    private void CheckFreshness()
    {
        var own = Environment.ProcessPath ?? "";
        var notes = new List<string>();
        // 2026-10-31: dynamic runtime context only (no hard-coded "TEST COPY").
        notes.Add(DescribeRuntimeEnvironment(own));
        var pub = FileStamp(PubExePath);
        if (!SamePath(own, PubExePath) && pub > _ownExeStamp.AddSeconds(2))
            notes.Add("OLDER BUILD than Pub " + pub.ToLocalTime().ToString("MMM d h:mm tt") + ", restart from the cockpit shortcut");
        var title = notes.Count == 0 ? _baseTitle : _baseTitle + " - " + string.Join(" - ", notes);
        if (Title != title) Title = title;

        var assets = AssetStamp(_www);
        if (assets > _assetStamp && !_reloadPending)
            _ = ReloadWhenIdleAsync(assets);
    }

    private async Task ReloadWhenIdleAsync(DateTime assets)
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        _reloadPending = true;
        try
        {
            // Only reload page code when Director is not mid-edit. Embedded Bambu frames stay;
            // the page re-posts slot bounds after reload and Place keeps them on the panes.
            var result = await core.ExecuteScriptAsync(
                "(function(){var L=window.__adcLive;if(L&&typeof L.busy==='function'&&L.busy())return 'busy';location.reload();return 'reloaded';})()");
            if (result != null && result.Contains("reloaded"))
                _assetStamp = assets;
        }
        catch
        {
            // Retry on the next tick.
        }
        finally
        {
            _reloadPending = false;
        }
    }

    private static DateTime AssetStamp(string www)
    {
        var max = DateTime.MinValue;
        foreach (var f in AssetFileNames)
        {
            var stamp = FileStamp(Path.Combine(www, f));
            if (stamp > max) max = stamp;
        }
        return max;
    }

    private static DateTime FileStamp(string? path)
    {
        try
        {
            return !string.IsNullOrEmpty(path) && File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.MinValue;
        }
        catch { return DateTime.MinValue; }
    }

    // Runtime host context for the window title (Source Debug / Source / Pub). Not a product env badge.
    private static string DescribeRuntimeEnvironment(string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return "[Unknown host]";
        if (SamePath(processPath, PubExePath)) return "[Pub]";
        var full = "";
        try { full = Path.GetFullPath(processPath); } catch { full = processPath; }
        var isSource = full.IndexOf(@"\Source\NonBalls\Advanced Director Cockpit WV3\", StringComparison.OrdinalIgnoreCase) >= 0
                       || full.IndexOf(@"\Advanced Director Cockpit WV3\", StringComparison.OrdinalIgnoreCase) >= 0;
        var isDebug = full.IndexOf(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isSource && isDebug) return "[Source Debug]";
        if (isSource) return "[Source]";
        return "[Host: " + (Path.GetDirectoryName(full) ?? full) + "]";
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
}
