using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AdvancedDirectorCockpitWV3.Embedding;

// adc-taskbar (2026-10-11): an embedded program must never take over a normal launch of the same program.
// Cause: a window moved into a pane with SetParent keeps its taskbar button (the shell is not told that it is now
// a child window). A click on that button, or Start, then went to the hidden pane window, and Chrome placed new
// windows next to the parked (off-screen) pane window. Now, while a window is embedded:
//   - it has WS_EX_TOOLWINDOW and its taskbar tab is deleted (ITaskbarList::DeleteTab), checked again every 5 s;
//     the original style, ex style and taskbar tab come back exactly on release, on cockpit close, on process exit
//     and on an unhandled exception (AppDomain), and a record (embedded-windows.txt) lets the next start repair a
//     window that was left behind by a crash or a kill;
//   - a window of that program that is activated from outside (a link opened in the embedded Chrome, a toast)
//     shows the cockpit tab that holds the pane (Reveal), instead of an invisible activation;
//   - a NEW top-level window of that program that opens mostly off-screen is moved onto the screen under the mouse.
// The pure decisions are in EmbedShellLogic.cs (unit-tested).
internal sealed partial class EmbeddedApps
{
    // Set by MainWindow: show the tab that holds this slot and bring the cockpit window forward.
    public Action<int>? Reveal { get; set; }

    private static bool _shellStarted;
    private static readonly object ShellGate = new();
    private static readonly Dictionary<IntPtr, OrphanRecord> ShellRecords = new();
    private static readonly Dictionary<uint, IntPtr> ShowHooks = new();
    private static WinEventProc? _foregroundProc, _showProc;
    private static IntPtr _foregroundHook;
    private static bool _emergencyDone;
    private int _shellTick;

    private static string ShellRecordPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        typeof(EmbeddedApps).Namespace ?? "AdvancedDirectorCockpit", "embedded-windows.txt");

    // Once per process, from the first EmbeddedApps (UI thread): repair windows left by a crash, watch activations, hand back on exit.
    private static void ShellStart()
    {
        if (_shellStarted) return;
        _shellStarted = true;
        try
        {
            foreach (var line in EmbedShell.RepairAll(new Win32ShellOps())) Log("start: " + line);
        }
        catch (Exception ex) { Log("start: repair of left-behind windows failed: " + ex.Message); }
        try
        {
            _foregroundProc = OnForegroundEvent;
            _showProc = OnShowEvent;
            _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _foregroundProc, 0, 0, WinEventOutOfContext);
            if (_foregroundHook == IntPtr.Zero) Log("taskbar: foreground hook not set");
        }
        catch (Exception ex) { Log("taskbar: foreground hook failed: " + ex.Message); }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => EmergencyHandBack("process exit");
        // A Dispatcher exception that no handler marks Handled is raised again as this (the process is ending).
        AppDomain.CurrentDomain.UnhandledException += (_, _) => EmergencyHandBack("unhandled exception");
    }

    // After Attach reparented the window: tool-window style, taskbar tab deleted, record written.
    private void ShellEmbedded(Session s)
    {
        try
        {
            var rec = EmbedShell.Embedded(new Win32ShellOps(), s.Window, s.WindowPid, s.OriginalStyle.ToInt64(), s.OriginalExStyle.ToInt64(),
                false, s.ImageName, s.Target);
            lock (ShellGate) ShellRecords[s.Window] = rec;
            Log("slot " + s.Slot + ": taskbar button and Alt+Tab entry of hwnd 0x" + s.Window.ToInt64().ToString("X") + " removed while embedded");
        }
        catch (Exception ex) { Log("slot " + s.Slot + ": taskbar: " + ex.Message); }
        SyncShowHooks();
    }

    // After Detach gave the window back with its original styles: taskbar tab back, record removed.
    private static void ShellReturned(IntPtr window, Session s)
    {
        try
        {
            OrphanRecord? rec;
            lock (ShellGate)
            {
                ShellRecords.Remove(window, out rec);
            }
            rec ??= new OrphanRecord(window.ToInt64(), s.WindowPid, s.OriginalStyle.ToInt64(), s.OriginalExStyle.ToInt64(),
                EmbedShell.ShouldHaveTab(s.OriginalStyle.ToInt64(), s.OriginalExStyle.ToInt64(), false), s.ImageName, s.Target);
            EmbedShell.Returned(new Win32ShellOps(), window, rec);
        }
        catch (Exception ex) { Log("taskbar: hand-back of 0x" + window.ToInt64().ToString("X") + ": " + ex.Message); }
    }

    // Every 5 s (CheckAlive): some programs re-create their taskbar button or reset their ex style; take it away again.
    private void ShellTick()
    {
        if (++_shellTick % 10 != 0) return;
        var ops = new Win32ShellOps();
        foreach (var s in _sessions.Values.ToList())
        {
            if (s.Window == IntPtr.Zero || !IsWindow(s.Window)) continue;
            try
            {
                var ex = GetWindowLongPtr(s.Window, GwlExStyle).ToInt64();
                if ((ex & WsExToolWindow) == 0 || (ex & WsExAppWindow) != 0) ops.SetExStyle(s.Window, EmbedShell.EmbeddedExStyle(ex));
                ops.DeleteTab(s.Window);
            }
            catch { }
        }
        lock (ShellGate)
        {
            foreach (var gone in ShellRecords.Keys.Where(h => !IsWindow(h)).ToList()) ShellRecords.Remove(gone);
        }
        SyncShowHooks();
    }

    // One EVENT_OBJECT_SHOW hook per process that has a window in a pane (new windows of that program are watched).
    private static void SyncShowHooks()
    {
        try
        {
            HashSet<uint> pids;
            lock (AllGate) pids = All.SelectMany(a => a._sessions.Values).Where(s => s.Window != IntPtr.Zero && s.WindowPid != 0).Select(s => s.WindowPid).ToHashSet();
            lock (ShellGate)
            {
                foreach (var pid in ShowHooks.Keys.Where(p => !pids.Contains(p)).ToList())
                {
                    UnhookWinEvent(ShowHooks[pid]);
                    ShowHooks.Remove(pid);
                }
                foreach (var pid in pids.Where(p => !ShowHooks.ContainsKey(p)).ToList())
                {
                    var hook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _showProc!, pid, 0, WinEventOutOfContext);
                    if (hook != IntPtr.Zero) ShowHooks[pid] = hook;
                }
            }
        }
        catch (Exception ex) { Log("taskbar: show hooks: " + ex.Message); }
    }

    // A window became the foreground window. When it is a pane window of a cockpit and the pane is not on screen
    // (other tab, popup, cockpit minimized), the cockpit shows that tab.
    private static void OnForegroundEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            List<EmbeddedApps> apps;
            lock (AllGate) apps = All.ToList();
            var root = GetAncestor(hwnd, GaRoot);
            foreach (var a in apps)
            {
                var s = a._sessions.Values.FirstOrDefault(o => o.Frame != IntPtr.Zero && (o.Frame == hwnd || o.Frame == root || (o.Window != IntPtr.Zero && o.Window == hwnd)));
                if (s == null) continue;
                var facts = new ForegroundFacts
                {
                    OwnFrameOrEmbedded = true,
                    Parked = s.Parked || (a.Suspended && (a._suspendHit == null || a._suspendHit(s.Rect))),
                    PaneVisible = s.Visible && IsWindowVisible(s.Frame),
                    CockpitMinimized = a._owner.WindowState == WindowState.Minimized
                };
                if (EmbedShell.Route(facts) == ForegroundRoute.ShowCockpitTab)
                {
                    Log("slot " + s.Slot + ": " + s.ImageName + " window activated from outside while its pane is not shown; showing its tab");
                    a.Reveal?.Invoke(s.Slot);
                }
                return;
            }
        }
        catch (Exception ex) { Log("taskbar: foreground: " + ex.Message); }
    }

    // A window of an embedded program was shown. A new top-level window that is mostly off-screen (Chrome places a
    // new window next to its last active window, which can be the parked pane window) is moved onto the screen.
    private static void OnShowEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
        try
        {
            if (!IsTopLevelCandidate(hwnd)) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                try { RescueIfOffscreen(hwnd); } catch (Exception ex) { Log("taskbar: rescue: " + ex.Message); }
            };
            t.Start();
        }
        catch { }
    }

    private static bool IsTopLevelCandidate(IntPtr hwnd)
    {
        if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) return false;
        if (GetAncestor(hwnd, GaParent) != GetDesktopWindow()) return false; // a child window
        if (GetWindow(hwnd, GwOwner) != IntPtr.Zero) return false;           // a menu, tooltip or dialog of another window
        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if ((ex & WsExToolWindow) != 0 && (ex & WsExAppWindow) == 0) return false;
        // Only real main windows (title bar, or forced onto the taskbar); menus, bubbles and tooltips are left alone.
        if ((GetWindowLongPtr(hwnd, GwlStyle).ToInt64() & WsCaption) != WsCaption && (ex & WsExAppWindow) == 0) return false;
        lock (AllGate)
            if (All.Any(a => a._sessions.Values.Any(s => s.Frame == hwnd || s.Window == hwnd))) return false;
        return true;
    }

    private static void RescueIfOffscreen(IntPtr hwnd)
    {
        if (!IsTopLevelCandidate(hwnd) || IsIconic(hwnd) || IsZoomed(hwnd)) return;
        if (!GetWindowRect(hwnd, out var r)) return;
        var ops = new Win32ShellOps();
        var box = new Box(r.Left, r.Top, r.Right, r.Bottom);
        var facts = new ForegroundFacts { TopLevelOfEmbeddedProgram = true, JustShown = true, VisibleFraction = EmbedShell.VisibleFraction(box, ops.WorkAreas()) };
        if (EmbedShell.Route(facts) != ForegroundRoute.RescueOffscreen) return;
        var to = EmbedShell.Rescue(box, ops.PreferredWorkArea());
        ops.Move(hwnd, to);
        GetWindowThreadProcessId(hwnd, out var pid);
        Log("taskbar: new window 0x" + hwnd.ToInt64().ToString("X") + " of pid " + pid + " opened off-screen at " + r.Left + "," + r.Top
            + "; moved to " + to.Left + "," + to.Top + " " + to.Width + "x" + to.Height);
    }

    // Process exit or a crash: give every embedded window back (styles, taskbar tab), as on a normal close.
    private static void EmergencyHandBack(string why)
    {
        if (_emergencyDone) return;
        _emergencyDone = true;
        List<EmbeddedApps> apps;
        try { lock (AllGate) apps = All.ToList(); } catch { return; }
        foreach (var a in apps)
        {
            try
            {
                if (a._sessions.Count == 0) continue;
                Log(why + ": handing back " + a._sessions.Count + " embedded window(s)");
                a.HandBackAll();
            }
            catch { }
        }
        try
        {
            if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
            lock (ShellGate)
            {
                foreach (var h in ShowHooks.Values) UnhookWinEvent(h);
                ShowHooks.Clear();
            }
        }
        catch { }
    }

    // Win32 behind EmbedShell (IShellOps).
    private sealed class Win32ShellOps : IShellOps
    {
        public WindowFacts Facts(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return new WindowFacts { Exists = false };
            GetWindowThreadProcessId(hwnd, out var pid);
            var parent = GetAncestor(hwnd, GaParent);
            GetWindowRect(hwnd, out var r);
            bool held;
            lock (AllGate) held = All.Any(a => a._sessions.Values.Any(s => s.Window == hwnd));
            return new WindowFacts
            {
                Exists = true,
                Pid = pid,
                Parent = parent == GetDesktopWindow() ? 0 : parent.ToInt64(),
                Style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64(),
                ExStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64(),
                Rect = new Box(r.Left, r.Top, r.Right, r.Bottom),
                HeldHere = held
            };
        }

        public void SetStyle(IntPtr hwnd, long style)
        {
            SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpNoSize | SwpNoMove);
        }

        public void SetExStyle(IntPtr hwnd, long ex)
        {
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(ex));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpNoSize | SwpNoMove);
        }

        public void Move(IntPtr hwnd, Box to) =>
            SetWindowPos(hwnd, IntPtr.Zero, to.Left, to.Top, to.Width, to.Height, SwpNoZOrder | SwpNoActivate);

        public void AddTab(IntPtr hwnd) => Taskbar()?.AddTab(hwnd);

        public void DeleteTab(IntPtr hwnd) => Taskbar()?.DeleteTab(hwnd);

        public IReadOnlyList<Box> WorkAreas()
        {
            var list = new List<Box>();
            MonitorEnumProc proc = (IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info)) list.Add(new Box(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            return list;
        }

        public Box PreferredWorkArea()
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            GetCursorPos(out var p);
            var monitor = MonitorFromPoint(p, 2);
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info)) return new Box(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
            var areas = WorkAreas();
            return areas.Count > 0 ? areas[0] : new Box(0, 0, 1280, 720);
        }

        public List<OrphanRecord> LoadRecords()
        {
            lock (ShellGate)
            {
                try { return File.Exists(ShellRecordPath) ? EmbedShell.ParseAll(File.ReadAllLines(ShellRecordPath)) : new List<OrphanRecord>(); }
                catch { return new List<OrphanRecord>(); }
            }
        }

        public void SaveRecords(List<OrphanRecord> records)
        {
            lock (ShellGate)
            {
                try
                {
                    if (records.Count == 0)
                    {
                        if (File.Exists(ShellRecordPath)) File.Delete(ShellRecordPath);
                        return;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(ShellRecordPath)!);
                    File.WriteAllLines(ShellRecordPath, records.Select(EmbedShell.Format));
                }
                catch { }
            }
        }
    }

    // ITaskbarList, one per thread (the UI thread normally; the exit thread on an emergency hand-back).
    [ThreadStatic] private static ITaskbarList? _taskbar;

    private static ITaskbarList? Taskbar()
    {
        if (_taskbar != null) return _taskbar;
        try
        {
            var t = (ITaskbarList)new TaskbarListCoClass();
            t.HrInit();
            _taskbar = t;
        }
        catch (Exception ex) { Log("taskbar: ITaskbarList unavailable: " + ex.Message); }
        return _taskbar;
    }

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarListCoClass
    {
    }

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint GaRoot = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
