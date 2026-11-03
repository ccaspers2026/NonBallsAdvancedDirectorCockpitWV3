using System.Globalization;

namespace AdvancedDirectorCockpitWV3.Embedding;

// adc-taskbar (2026-10-11): the pure decisions behind EmbeddedApps.Shell.cs, kept free of Win32 and WPF so they
// are unit-tested on the box. Everything that touches a window goes through IShellOps (faked in the tests).
//   - Style while embedded / restore: the program's window is a WS_CHILD of the pane frame; it also gets
//     WS_EX_TOOLWINDOW (never a taskbar button or Alt+Tab entry) and its taskbar tab is deleted, because the
//     shell keeps a stale button for a top-level window that was reparented. On release every style bit comes
//     back exactly as it was and the taskbar tab is added again.
//   - Persisted record: one line per embedded window (hwnd, pid, original style and ex style, image, target),
//     so the next start can repair a window that was left with embedded styles (crash or kill).
//   - Foreground routing: a window of an embedded program that is activated from outside (a link opened in
//     the embedded Chrome, a notification) shows its cockpit tab; a NEW top-level window of an embedded
//     program that opens (almost) off-screen is moved onto the screen (Chrome places a new window next to its
//     last active window, which may be the parked pane window).
internal static class EmbedShell
{
    public const long WsChild = 0x40000000L;
    public const long WsVisible = 0x10000000L;
    public const long WsExDlgModalFrame = 0x00000001L;
    public const long WsExTopmost = 0x00000008L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExWindowEdge = 0x00000100L;
    public const long WsExClientEdge = 0x00000200L;
    public const long WsExStaticEdge = 0x00020000L;
    public const long WsExAppWindow = 0x00040000L;

    // Ex style while embedded: the frame edges EmbeddedApps.Attach already removed, plus WS_EX_TOOLWINDOW and never WS_EX_APPWINDOW.
    public static long EmbeddedExStyle(long originalEx)
    {
        var ex = originalEx & ~(WsExAppWindow | WsExWindowEdge | WsExDlgModalFrame | WsExTopmost | WsExClientEdge | WsExStaticEdge);
        return ex | WsExToolWindow;
    }

    // Did the window have a taskbar button before it was embedded? (top-level, not a tool window; owned only with WS_EX_APPWINDOW)
    public static bool ShouldHaveTab(long originalStyle, long originalEx, bool hasOwner)
    {
        if ((originalStyle & WsChild) != 0) return false;
        if ((originalEx & WsExToolWindow) != 0) return false;
        return !hasOwner || (originalEx & WsExAppWindow) != 0;
    }

    // Programs that always open a NEW window for --new-window or a plain start into a running copy. For them the
    // hand-off never takes the user's own running window (the "adopt the running window" fallback for 1Password-like apps).
    private static readonly HashSet<string> NewWindowBrowsers = new(StringComparer.OrdinalIgnoreCase)
    { "chrome", "msedge", "brave", "vivaldi", "opera", "opera_gx", "chromium", "firefox" };

    public static bool NeverAdoptRunning(string image) => !string.IsNullOrEmpty(image) && NewWindowBrowsers.Contains(image);

    // ---- persisted record ----
    public static string Format(OrphanRecord r) => string.Join("\t", new[]
    {
        "v1", r.Hwnd.ToString(CultureInfo.InvariantCulture), r.Pid.ToString(CultureInfo.InvariantCulture),
        r.Style.ToString("X", CultureInfo.InvariantCulture), r.ExStyle.ToString("X", CultureInfo.InvariantCulture),
        r.HadTab ? "1" : "0", Clean(r.Image), Clean(r.Target)
    });

    public static OrphanRecord? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var p = line.Split('\t');
        if (p.Length < 8 || p[0] != "v1") return null;
        if (!long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hwnd) || hwnd == 0) return null;
        if (!uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid == 0) return null;
        if (!long.TryParse(p[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var style)) return null;
        if (!long.TryParse(p[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ex)) return null;
        return new OrphanRecord(hwnd, pid, style, ex, p[5] == "1", p[6], p[7]);
    }

    public static List<OrphanRecord> ParseAll(IEnumerable<string> lines)
    {
        var list = new List<OrphanRecord>();
        foreach (var l in lines)
        {
            var r = Parse(l);
            if (r != null && !list.Any(o => o.Hwnd == r.Hwnd)) list.Add(r);
        }
        return list;
    }

    // Adds or replaces the line for one window.
    public static List<OrphanRecord> Upsert(IEnumerable<OrphanRecord> list, OrphanRecord r)
    {
        var l = list.Where(o => o.Hwnd != r.Hwnd).ToList();
        l.Add(r);
        return l;
    }

    public static List<OrphanRecord> Remove(IEnumerable<OrphanRecord> list, long hwnd) => list.Where(o => o.Hwnd != hwnd).ToList();

    private static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    // ---- orphan repair (next start) ----
    public static RepairAction Decide(OrphanRecord r, WindowFacts f)
    {
        if (!f.Exists || f.Pid != r.Pid) return RepairAction.Forget;      // gone, or the handle now belongs to another program
        if (f.HeldHere) return RepairAction.Keep;                           // embedded in a pane of this cockpit right now
        if (f.Parent != 0) return RepairAction.Keep;                        // still inside another live window (another cockpit instance): not ours to touch
        var styleOff = f.Style != r.Style || f.ExStyle != r.ExStyle;
        return styleOff || r.HadTab ? RepairAction.Restore : RepairAction.Forget;
    }

    // What a repair (or a normal release) does to a top-level window that has the record r.
    public static RestorePlan PlanRestore(OrphanRecord r, WindowFacts f, IReadOnlyList<Box> workAreas, Box preferredWork)
    {
        var plan = new RestorePlan
        {
            SetStyle = f.Style != r.Style ? r.Style : null,
            SetExStyle = f.ExStyle != r.ExStyle ? r.ExStyle : null,
            AddTab = r.HadTab
        };
        if (VisibleFraction(f.Rect, workAreas) < 0.5) plan.MoveTo = Rescue(f.Rect, preferredWork);
        return plan;
    }

    // ---- foreground routing ----
    public static ForegroundRoute Route(ForegroundFacts f)
    {
        if (f.OwnFrameOrEmbedded)
        {
            // A click inside a pane that is on screen is the normal case: nothing to do.
            if (f.CockpitMinimized || f.Parked || !f.PaneVisible) return ForegroundRoute.ShowCockpitTab;
            return ForegroundRoute.Ignore;
        }
        if (f.TopLevelOfEmbeddedProgram && f.JustShown && f.VisibleFraction < 0.5) return ForegroundRoute.RescueOffscreen;
        return ForegroundRoute.Ignore;
    }

    // Share of the rectangle's area that lies on some monitor work area (0..1).
    public static double VisibleFraction(Box r, IReadOnlyList<Box> workAreas)
    {
        long area = (long)Math.Max(0, r.Width) * Math.Max(0, r.Height);
        if (area <= 0) return 0;
        long seen = 0;
        foreach (var w in workAreas)
        {
            var x1 = Math.Max(r.Left, w.Left); var y1 = Math.Max(r.Top, w.Top);
            var x2 = Math.Min(r.Right, w.Right); var y2 = Math.Min(r.Bottom, w.Bottom);
            if (x2 > x1 && y2 > y1) seen += (long)(x2 - x1) * (y2 - y1);
        }
        return Math.Min(1.0, (double)seen / area);
    }

    // Same size where it fits (at least 640x480, at most the work area less a 24 px margin), centred on the work area.
    public static Box Rescue(Box r, Box work)
    {
        const int margin = 24;
        var maxW = Math.Max(200, work.Width - 2 * margin);
        var maxH = Math.Max(150, work.Height - 2 * margin);
        var w = Math.Min(Math.Max(r.Width, Math.Min(640, maxW)), maxW);
        var h = Math.Min(Math.Max(r.Height, Math.Min(480, maxH)), maxH);
        var x = work.Left + (work.Width - w) / 2;
        var y = work.Top + (work.Height - h) / 2;
        return new Box(x, y, x + w, y + h);
    }

    // ---- executors (Win32 behind IShellOps) ----

    // After a window was reparented into a pane: tool-window style, no taskbar tab, record written.
    public static OrphanRecord Embedded(IShellOps ops, IntPtr window, uint pid, long originalStyle, long originalEx, bool hasOwner, string image, string target)
    {
        var rec = new OrphanRecord(window.ToInt64(), pid, originalStyle, originalEx, ShouldHaveTab(originalStyle, originalEx, hasOwner), image, target);
        ops.SaveRecords(Upsert(ops.LoadRecords(), rec));
        ops.SetExStyle(window, EmbeddedExStyle(originalEx));
        ops.DeleteTab(window);
        return rec;
    }

    // After the window was handed back (styles already restored by EmbeddedApps.Detach): taskbar tab back, record removed.
    public static void Returned(IShellOps ops, IntPtr window, OrphanRecord? rec)
    {
        if (rec != null)
        {
            var f = ops.Facts(window);
            if (f.Exists && f.Parent == 0)
            {
                if (f.ExStyle != rec.ExStyle) ops.SetExStyle(window, rec.ExStyle);
                if (f.Style != rec.Style) ops.SetStyle(window, rec.Style);
                if (rec.HadTab) ops.AddTab(window);
            }
        }
        ops.SaveRecords(Remove(ops.LoadRecords(), window.ToInt64()));
    }

    // Start of the cockpit: every recorded window that is no longer embedded gets its styles and taskbar tab back.
    public static List<string> RepairAll(IShellOps ops)
    {
        var done = new List<string>();
        var keep = new List<OrphanRecord>();
        foreach (var r in ops.LoadRecords())
        {
            var hwnd = new IntPtr(r.Hwnd);
            var f = ops.Facts(hwnd);
            var action = Decide(r, f);
            if (action == RepairAction.Keep) { keep.Add(r); continue; }
            if (action == RepairAction.Forget) { done.Add("forget 0x" + r.Hwnd.ToString("X") + " (" + (f.Exists ? "pid changed or nothing to fix" : "window gone") + ")"); continue; }
            var plan = PlanRestore(r, f, ops.WorkAreas(), ops.PreferredWorkArea());
            if (plan.SetStyle is long st) ops.SetStyle(hwnd, st);
            if (plan.SetExStyle is long ex) ops.SetExStyle(hwnd, ex);
            if (plan.MoveTo is Box b) ops.Move(hwnd, b);
            if (plan.AddTab) ops.AddTab(hwnd);
            done.Add("repaired 0x" + r.Hwnd.ToString("X") + " " + r.Image + (plan.SetStyle != null || plan.SetExStyle != null ? " (styles restored)" : "") + (plan.MoveTo != null ? " (moved on screen)" : "") + (plan.AddTab ? " (taskbar tab)" : ""));
        }
        ops.SaveRecords(keep);
        return done;
    }
}

internal sealed record OrphanRecord(long Hwnd, uint Pid, long Style, long ExStyle, bool HadTab, string Image, string Target);

internal enum RepairAction { Forget, Keep, Restore }

internal enum ForegroundRoute { Ignore, ShowCockpitTab, RescueOffscreen }

internal readonly record struct Box(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal sealed class WindowFacts
{
    public bool Exists { get; init; }
    public uint Pid { get; init; }
    public long Parent { get; init; }
    public long Style { get; init; }
    public long ExStyle { get; init; }
    public Box Rect { get; init; }
    public bool HeldHere { get; init; }
}

internal sealed class RestorePlan
{
    public long? SetStyle { get; set; }
    public long? SetExStyle { get; set; }
    public Box? MoveTo { get; set; }
    public bool AddTab { get; set; }
}

internal sealed class ForegroundFacts
{
    public bool OwnFrameOrEmbedded { get; init; }      // the foreground window is a pane frame of this cockpit window (or the program inside it)
    public bool Parked { get; init; }                  // that pane is parked off-screen (tab not shown, or under a popup)
    public bool PaneVisible { get; init; }             // that pane is shown on screen
    public bool CockpitMinimized { get; init; }
    public bool TopLevelOfEmbeddedProgram { get; init; } // a normal top-level window of a process that also has a window in a pane
    public bool JustShown { get; init; }               // that window was shown in the last few seconds (a new window)
    public double VisibleFraction { get; init; } = 1;
}

internal interface IShellOps
{
    WindowFacts Facts(IntPtr hwnd);
    void SetStyle(IntPtr hwnd, long style);
    void SetExStyle(IntPtr hwnd, long ex);
    void Move(IntPtr hwnd, Box to);
    void AddTab(IntPtr hwnd);
    void DeleteTab(IntPtr hwnd);
    IReadOnlyList<Box> WorkAreas();
    Box PreferredWorkArea();
    List<OrphanRecord> LoadRecords();
    void SaveRecords(List<OrphanRecord> records);
}
