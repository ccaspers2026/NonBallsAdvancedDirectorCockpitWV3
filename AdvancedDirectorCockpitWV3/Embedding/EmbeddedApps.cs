using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AdvancedDirectorCockpitWV3.Embedding;

// Shows a desktop program (any .exe card: Bambu Studio, Chrome, ...) inside a stage pane.
// The program is started, its main top-level window is found, and that window is reparented
// with SetParent into a borderless frame window owned by the cockpit. The frame is kept on the
// pane rectangle in screen pixels. An owned frame is used, not a child of the cockpit's client
// area, because WebView2 draws its page with DirectComposition above every child HWND, so a
// child window under the page would never be seen. An owned window always sits above its owner,
// moves in z-order with it, and hides when the cockpit is minimized.
// When the pane is cleared or replaced, or the cockpit closes, the program's window is handed
// back as a normal top-level window. The program is never killed. If it exits, the page clears
// the pane.
// Single-instance programs (Chrome, Edge, many others) hand off: the exe that was started passes
// the request to the copy that is already running and exits at once, and the new window belongs to
// that older process. When the started process exits before showing a window, the search moves to
// a NEW window (not open before the drop, not held by another pane) of any process with the same
// exe name. Such a window shares its process with other windows, so releasing it closes only that
// window (WM_CLOSE to the window), and the pane is cleared when that window goes away.
// adc-taskbar (2026-10-11): partial; the taskbar / Alt+Tab / activation handling is in EmbeddedApps.Shell.cs.
internal sealed partial class EmbeddedApps
{
    private readonly Window _owner;
    private readonly Canvas _host;
    private readonly Dispatcher _dispatcher;
    private readonly Action<int> _ended;
    private readonly Dictionary<int, Session> _sessions = new();
    // Serialize graceful closes so two Bambus never write BambuNetworkEngine.conf at once.
    private static readonly object CloseGate = new();
    private static readonly List<EmbeddedApps> All = new();
    private static readonly object AllGate = new();

    // True when another cockpit window of this process already runs this target (exe card).
    private bool OwnedElsewhere(string target)
    {
        lock (AllGate)
            return All.Any(o => o != this && o._sessions.Values.Any(s => string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase)));
    }

    public EmbeddedApps(Window owner, Canvas host, Action<int> ended)
    {
        _owner = owner;
        _host = host;
        _dispatcher = host.Dispatcher;
        _ended = ended;
        _owner.LocationChanged += (_, _) => PlaceAll();
        _owner.SizeChanged += (_, _) => PlaceAll();
        _owner.StateChanged += (_, _) => PlaceAll();
        _owner.DpiChanged += (_, _) => PlaceAll();
        // 2026-10-09 multi-window: every cockpit window's EmbeddedApps is known, so one app runs in one window only.
        lock (AllGate) All.Add(this);
        _owner.Closed += (_, _) => { lock (AllGate) All.Remove(this); };
        // A window in a shared process (handed-off Chrome) has no process exit to watch,
        // so every embedded window is checked once a second.
        _alive = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _alive.Tick += (_, _) => CheckAlive();
        _alive.Start();
        ShellStart(); // adc-taskbar: once per process: repair windows left by a crash, activation hook, exit hand-back
    }

    private readonly DispatcherTimer _alive;

    private void CheckAlive()
    {
        foreach (var s in _sessions.Values.ToList())
        {
            if (s.Window != IntPtr.Zero && !IsWindow(s.Window)) OnWindowGone(s, "window closed");
        }
        foreach (var d in _sessions.Values.ToList())
            if (Drifted(d)) Apply(d);
        ShellTick(); // adc-taskbar: keep embedded windows off the taskbar and Alt+Tab
    }

    public bool Has(int slot) => _sessions.ContainsKey(slot);

    // True while a page popup is open. The frames (and the apps inside them) are hidden, not
    // released, so each app keeps its window, its process, and its state.
    public bool Suspended { get; private set; }

    // adc-v156: per-window popup cover. With a hit test, only apps whose pane rectangle the popup covers are parked.
    private Func<Rect, bool>? _suspendHit;

    public void Suspend(bool on, Func<Rect, bool>? hit = null)
    {
        if (Suspended == on && !on) return;
        Suspended = on;
        _suspendHit = on ? hit : null;
        PlaceAll();
    }

    public bool Shows(int slot, string target)
    {
        return _sessions.TryGetValue(slot, out var s)
            && string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase);
    }

    // Starts the program for a slot, unless that slot already holds the same target.
    public void Embed(int slot, string target, string exe, string args = "")
    {
        if (_sessions.TryGetValue(slot, out var existing))
        {
            if (string.Equals(existing.Target, target, StringComparison.OrdinalIgnoreCase)) return;
            Release(slot);
        }
        if (OwnedElsewhere(target))
        {
            // 2026-10-09 multi-window: never start or grab a second copy; the app stays in the window that has it.
            Log("slot " + slot + ": " + target + " already runs in another cockpit window; not started here");
            return;
        }
        var ownerHwnd = new WindowInteropHelper(_owner).Handle;
        if (ownerHwnd == IntPtr.Zero) return;
        if (TryReclaim(slot, target, ownerHwnd)) return; // 2026-10-09: same window as before the cockpit closed
        var frame = WithMixedDpiHosting(() => CreateWindowEx((int)WsExToolWindow, "STATIC", "Embedded app", (uint)(WsPopup | WsClipChildren),
            0, 0, 1, 1, ownerHwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        HookFrame(frame);
        if (frame == IntPtr.Zero)
        {
            Log("slot " + slot + ": could not create the pane frame");
            return;
        }
        var session = new Session(slot, target, frame);
        session.Args = args ?? "";
        session.ImageName = Path.GetFileNameWithoutExtension(exe);
        session.Exe = exe; // adc-v157
        session.Before = WindowsOfImage(session.ImageName);
        session.HiddenBefore = HiddenOnly(session.Before); // adc-v156: tray apps re-show a hidden window on hand-off
        _sessions[slot] = session;
        Log("slot " + slot + ": start " + exe + (session.Args.Length > 0 ? " " + session.Args : "") + " (frame 0x" + frame.ToInt64().ToString("X") + ")");
        _ = StartAsync(session, exe);
    }

    // Keeps the frame on the pane rectangle (WPF units, PaneHost coordinates).
    public void Place(int slot, Rect rect, bool visible)
    {
        if (!_sessions.TryGetValue(slot, out var s)) return;
        s.Rect = rect;
        s.Visible = visible;
        Apply(s);
    }

    public void ReleaseExcept(ICollection<int> keep)
    {
        foreach (var slot in _sessions.Keys.ToList())
        {
            if (!keep.Contains(slot)) Release(slot);
        }
    }

    public void ReleaseAll()
    {
        foreach (var slot in _sessions.Keys.ToList()) Release(slot);
    }

    // Gives the window back, asks the app to close with WM_CLOSE, waits, never force-kills.
    public void Release(int slot)
    {
        if (!_sessions.Remove(slot, out var s)) return;
        s.Released = true;
        var window = s.Window;
        var shared = s.HandedOff || HasOtherWindows(s.WindowPid, window);
        Detach(s);
        DestroyFrame(s);
        if (shared && window != IntPtr.Zero)
        {
            // The process also runs windows that are not in this pane: close only this window.
            if (IsWindow(window)) PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            Log("slot " + slot + ": released: WM_CLOSE window 0x" + window.ToInt64().ToString("X") + " (shared pid " + s.WindowPid + ")");
            return;
        }
        var releasedProcess = s.Process; // 2026-10-09: the wait runs off the UI thread (it froze the cockpit up to 60 s)
        _ = Task.Run(() => GracefulClose(releasedProcess, "slot " + slot + ": released"));
    }

    // WM_CLOSE only. Waits up to 60 s under a process-wide lock. Never Process.Kill.
    private static void GracefulClose(Process? process, string logLabel)
    {
        if (process == null) return;
        lock (CloseGate)
        {
            try
            {
                if (SafeExited(process))
                {
                    Log(logLabel + " (already exited)");
                    return;
                }
                Log(logLabel + ": WM_CLOSE pid " + process.Id);
                process.CloseMainWindow();
                if (!process.WaitForExit(60000))
                    Log(logLabel + ": still running after 60 s; left alone (never force-killed)");
                else
                    Log(logLabel + ": exited");
            }
            catch (Exception ex)
            {
                Log(logLabel + ": close failed: " + ex.Message);
            }
        }
    }

    private void PlaceAll()
    {
        foreach (var s in _sessions.Values) Apply(s);
    }

    private void Apply(Session s)
    {
        if (s.Frame == IntPtr.Zero) return;
        if (_owner.WindowState == WindowState.Minimized) return;
        if ((Suspended && (_suspendHit == null || _suspendHit(s.Rect))) || !s.Visible || s.Rect.Width < 8 || s.Rect.Height < 8 || s.Window == IntPtr.Zero)
        {
            // 2026-10-09 keep-alive: an app that has been shown is PARKED off-screen (frame still shown, same
            // size, same parent; never hidden, minimized or resized), so calls, audio and renderers keep running
            // on inactive tabs and under page popups.
            if (s.Window != IntPtr.Zero && s.Shown) Park(s);
            else ShowWindow(s.Frame, SwHide);
            return;
        }
        Point topLeft;
        Point bottomRight;
        try
        {
            topLeft = _host.PointToScreen(s.Rect.TopLeft);
            bottomRight = _host.PointToScreen(s.Rect.BottomRight);
        }
        catch
        {
            return;
        }
        var x = (int)Math.Round(topLeft.X);
        var y = (int)Math.Round(topLeft.Y);
        var w = Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X));
        var h = Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y));
        SetWindowPos(s.Frame, IntPtr.Zero, x, y, w, h, SwpNoZOrder | SwpNoActivate | SwpShowWindow);
        s.Shown = true;
        s.Parked = false;
        FitChild(s, w, h);
    }

    private async Task StartAsync(Session s, string exe)
    {
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                Arguments = s.Args,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            });
        }
        catch (Exception ex)
        {
            Log("slot " + s.Slot + ": start failed: " + ex.Message);
        }
        if (process == null)
        {
            Fail(s);
            return;
        }
        s.Process = process;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _dispatcher.BeginInvoke(new Action(() => OnExited(s)));
        }
        catch
        {
            // Exit is also noticed when the window disappears.
        }

        // Wait for the real main window. A splash screen or a window that is still changing is skipped:
        // the same resizable, titled window must be found on four polls in a row (about one second).
        // Several instances starting together (a Workshop layout) can take well over 45 s each.
        var deadline = DateTime.UtcNow.AddSeconds(StartTimeoutSeconds);
        IntPtr last = IntPtr.Zero;
        var stable = 0;
        IntPtr main = IntPtr.Zero;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            if (s.Released) return;
            IntPtr found;
            if (!SafeExited(process))
            {
                found = FindMainWindow(new HashSet<uint> { (uint)process.Id }, null);
            }
            else
            {
                // Hand-off: the started exe passed the request to a running copy and exited.
                if (!s.HandedOff)
                {
                    s.HandedOff = true;
                    s.HandOffAt = DateTime.UtcNow;
                    var handoff = DateTime.UtcNow.AddSeconds(HandOffTimeoutSeconds);
                    if (handoff < deadline) deadline = handoff;
                    Log("slot " + s.Slot + ": pid " + SafeId(process) + " exited before showing a window; looking for a new " + s.ImageName + " window");
                }
                // adc-v156: a window that was HIDDEN before the drop (app closed to the tray) may be the one the
                // running copy shows for this hand-off, so it is not skipped. Windows that were visible before stay skipped.
                var held = HeldWindows(s); // adc-v157: windows held by any pane of any cockpit window
                var skip = new HashSet<IntPtr>(s.Before);
                skip.ExceptWith(s.HiddenBefore);
                skip.UnionWith(held);
                var handPids = PidsOfImage(s.ImageName);
                found = FindMainWindow(handPids, skip);
                var since = (DateTime.UtcNow - s.HandOffAt).TotalSeconds;
                // adc-taskbar: never for a browser: Chrome always opens a new window for --new-window, so the running
                // window is the user's own browser window (taking it hid the user's Chrome in a pane).
                if (found == IntPtr.Zero && since >= HandOffAdoptSeconds && !EmbedShell.NeverAdoptRunning(s.ImageName))
                {
                    // adc-v157: no NEW window came. A single-instance app (1Password) may already show its main
                    // window; take that running window when no pane holds it (the real window, not a copy).
                    found = FindMainWindow(handPids, held);
                    if (found != IntPtr.Zero && !s.AdoptLogged)
                    {
                        s.AdoptLogged = true;
                        Log("slot " + s.Slot + ": no new window after the hand-off; taking the running " + s.ImageName + " window 0x" + found.ToInt64().ToString("X") + " (no pane holds it)");
                    }
                }
                if (found == IntPtr.Zero)
                {
                    LogCandidates(s, handPids, skip);
                    if (!s.HiddenLogged)
                    {
                        s.HiddenLogged = true;
                        LogHidden(s, handPids);
                    }
                    if (!s.AutoShown && since >= 1.5 && ShowArgs(s.ImageName) is string showArgs && FindMainWindow(handPids, null) == IntPtr.Zero)
                    {
                        s.AutoShown = true;
                        ShowRunning(s, showArgs);
                    }
                    if (!s.NoteSent && since >= HandOffNoteSeconds)
                    {
                        s.NoteSent = true;
                        if (TryOpenDocumentInRunningBrowser(s))
                        {
                            s.OpenedInRunningBrowser = true;
                            SendNote(s.Slot, "Opened in your running " + s.ImageName + " (it was already open).");
                            deadline = DateTime.UtcNow; // stop waiting; document is in the existing browser
                        }
                        else
                        {
                            var longer = DateTime.UtcNow.AddSeconds(HandOffNoteWaitSeconds);
                            if (longer > deadline) deadline = longer;
                            Log("slot " + s.Slot + ": no window from the running " + s.ImageName + " " + HandOffNoteSeconds + " s after the hand-off; pane asks to open it from its tray icon (waiting " + HandOffNoteWaitSeconds + " s more)");
                            SendNote(s.Slot, s.ImageName + " is already running in the background (tray icon) and did not open its window. Open " + s.ImageName + " from its tray icon and it moves into this pane.");
                        }
                    }
                }
            }
            stable = found != IntPtr.Zero && found == last ? stable + 1 : 0;
            last = found;
            if (found != IntPtr.Zero && stable >= 4)
            {
                main = found;
                break;
            }
        }
        if (s.Released) return;
        if (main == IntPtr.Zero)
        {
            Log("slot " + s.Slot + ": no main window from " + (s.HandedOff ? "any new " + s.ImageName + " window" : "pid " + SafeId(process))
                + " within the start wait");
            if (s.HandedOff)
            {
                // Nothing to close: the started exe already exited.
                if (_sessions.TryGetValue(s.Slot, out var cur) && ReferenceEquals(cur, s)) _sessions.Remove(s.Slot);
                s.Released = true;
                DestroyFrame(s);
                if (s.NoteSent)
                {
                    // adc-v157: keep the explanation in the pane instead of a silent blank pane.
                    // 2026-11-01: if we already opened the document in the running browser, leave that note.
                    if (!s.OpenedInRunningBrowser)
                        SendNote(s.Slot, s.ImageName + " did not open a window. Open it from its tray icon, then drop the card on this pane again.");
                    return;
                }
                _ended(s.Slot);
                return;
            }
            Fail(s);
            return;
        }
        Attach(s, main);
    }

    private void Attach(Session s, IntPtr window)
    {
        s.OriginalStyle = GetWindowLongPtr(window, GwlStyle);
        s.OriginalExStyle = GetWindowLongPtr(window, GwlExStyle);
        GetWindowRect(window, out s.OriginalRect);
        if (IsIconic(window) || IsZoomed(window))
            ShowWindow(window, SwRestore);

        var style = s.OriginalStyle.ToInt64();
        style &= ~(WsPopup | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        style |= WsChild | WsVisible | WsClipSiblings | WsClipChildren;
        var ex = s.OriginalExStyle.ToInt64();
        ex &= ~(WsExAppWindow | WsExWindowEdge | WsExDlgModalFrame | WsExTopmost | WsExClientEdge | WsExStaticEdge);

        SetWindowLongPtr(window, GwlStyle, new IntPtr(style));
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(ex));
        var prevParent = SetParent(window, s.Frame);
        var parentError = prevParent == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        if (GetAncestor(window, GaParent) != s.Frame)
        {
            // adc-v157: Windows refused the reparent. Say so (error and DPI awareness of both windows) and give the
            // window back unchanged; nothing else is shown in its place.
            Log("slot " + s.Slot + ": SetParent of hwnd 0x" + window.ToInt64().ToString("X") + " into frame 0x" + s.Frame.ToInt64().ToString("X")
                + " refused (error " + parentError + "; dpi awareness window " + Awareness(window) + ", frame " + Awareness(s.Frame) + ")");
            SetWindowLongPtr(window, GwlStyle, s.OriginalStyle);
            SetWindowLongPtr(window, GwlExStyle, s.OriginalExStyle);
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpNoSize | SwpNoMove);
            if (_sessions.TryGetValue(s.Slot, out var cur) && ReferenceEquals(cur, s)) _sessions.Remove(s.Slot);
            s.Released = true;
            DestroyFrame(s);
            SendNote(s.Slot, "Windows refused to move " + s.ImageName + " into this pane (error " + parentError + "). It stays in its own window.");
            return;
        }
        SetWindowPos(window, IntPtr.Zero, 0, 0, 1, 1, SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
        s.Window = window;
        s.AttachedAt = DateTime.UtcNow;
        GetWindowThreadProcessId(window, out var windowPid);
        s.WindowPid = windowPid;
        ShellEmbedded(s); // adc-taskbar: no taskbar button / Alt+Tab entry while embedded; record for crash repair
        Apply(s);
        RefitSoon(s);
        Log("slot " + s.Slot + ": embedded hwnd 0x" + window.ToInt64().ToString("X") + " pid " + windowPid
            + (s.HandedOff ? " (handed off from pid " + SafeId(s.Process) + ")" : "")
            + " into frame 0x" + s.Frame.ToInt64().ToString("X"));
    }

    private static void Detach(Session s)
    {
        var window = s.Window;
        s.Window = IntPtr.Zero;
        if (window == IntPtr.Zero || !IsWindow(window)) return;
        try
        {
            GetWindowRect(window, out var now);
            SetParent(window, IntPtr.Zero);
            SetWindowLongPtr(window, GwlStyle, s.OriginalStyle);
            SetWindowLongPtr(window, GwlExStyle, s.OriginalExStyle);
            var w = Math.Max(640, s.OriginalRect.Right - s.OriginalRect.Left);
            var h = Math.Max(480, s.OriginalRect.Bottom - s.OriginalRect.Top);
            var x = now.Left;
            var y = now.Top;
            // Keep the returned window on the monitor it was shown on.
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            var monitor = MonitorFromWindow(window, 2);
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                w = Math.Min(w, info.Work.Right - info.Work.Left);
                h = Math.Min(h, info.Work.Bottom - info.Work.Top);
                x = Math.Max(info.Work.Left, Math.Min(x, info.Work.Right - w));
                y = Math.Max(info.Work.Top, Math.Min(y, info.Work.Bottom - h));
            }
            SetWindowPos(window, IntPtr.Zero, x, y, w, h, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
            ShellReturned(window, s); // adc-taskbar: exact original styles checked, taskbar button back, record removed
        }
        catch
        {
            // The program keeps running either way.
        }
    }

    private static void DestroyFrame(Session s)
    {
        var frame = s.Frame;
        s.Frame = IntPtr.Zero;
        if (frame != IntPtr.Zero) DestroyWindow(frame);
    }

    private void OnExited(Session s)
    {
        // A launcher that exits before any window was attached may have handed off to a running
        // copy; StartAsync keeps looking. A window owned by another process is watched by CheckAlive.
        if (s.Window == IntPtr.Zero) return;
        if (s.Process != null && s.WindowPid != 0 && s.WindowPid != (uint)SafeId(s.Process)) return;
        OnWindowGone(s, "program exited");
    }

    private void OnWindowGone(Session s, string why)
    {
        if (!_sessions.TryGetValue(s.Slot, out var current) || !ReferenceEquals(current, s)) return;
        if (TryFollow(s, why)) return;
        ClearGone(s, why);
    }

    private void ClearGone(Session s, string why)
    {
        if (!_sessions.TryGetValue(s.Slot, out var current) || !ReferenceEquals(current, s)) return;
        _sessions.Remove(s.Slot);
        s.Released = true;
        s.Window = IntPtr.Zero;
        DestroyFrame(s);
        Log("slot " + s.Slot + ": " + why + "; pane cleared");
        _ended(s.Slot);
    }

    private static int SafeId(Process? p)
    {
        try { return p?.Id ?? 0; }
        catch { return 0; }
    }

    // Process ids of every running copy of an exe (by file name without .exe).
    private static HashSet<uint> PidsOfImage(string image)
    {
        var set = new HashSet<uint>();
        if (string.IsNullOrEmpty(image)) return set;
        try
        {
            foreach (var p in Process.GetProcessesByName(image))
            {
                try { set.Add((uint)p.Id); } catch { }
                p.Dispose();
            }
        }
        catch { }
        return set;
    }

    // adc-v156: the subset of windows that were not visible at the drop (e.g. a main window hidden in the tray).
    private static HashSet<IntPtr> HiddenOnly(HashSet<IntPtr> windows)
    {
        var set = new HashSet<IntPtr>();
        foreach (var h in windows)
            if (IsWindow(h) && !IsWindowVisible(h)) set.Add(h);
        return set;
    }

    // adc-v156: during a hand-off wait, logs each visible top-level window of the program that was not taken as
    // the main window (class, style, size, title length; never the title text), once per window, so a refusal can be read.
    private static void LogCandidates(Session s, HashSet<uint> pids, HashSet<IntPtr> skip)
    {
        if (s.Described.Count >= 12) return;
        try
        {
            EnumWindows((h, _) =>
            {
                if (s.Described.Count >= 12) return false;
                if (skip.Contains(h) || s.Described.Contains(h)) return true;
                GetWindowThreadProcessId(h, out var owner);
                if (!pids.Contains(owner) || !IsWindowVisible(h)) return true;
                s.Described.Add(h);
                var cls = new System.Text.StringBuilder(128);
                GetClassName(h, cls, cls.Capacity);
                GetWindowRect(h, out var r);
                Log("slot " + s.Slot + ": hand-off candidate not taken: hwnd 0x" + h.ToInt64().ToString("X") + " pid " + owner
                    + " class " + cls + " style 0x" + GetWindowLongPtr(h, GwlStyle).ToInt64().ToString("X")
                    + " ex 0x" + GetWindowLongPtr(h, GwlExStyle).ToInt64().ToString("X")
                    + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + " owned " + (GetWindow(h, GwOwner) != IntPtr.Zero)
                    + " titleLen " + GetWindowTextLength(h) + (s.HiddenBefore.Contains(h) ? " (hidden before the drop)" : ""));
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    // Top-level windows that copies of this exe already had before the drop.
    private static HashSet<IntPtr> WindowsOfImage(string image)
    {
        var pids = PidsOfImage(image);
        var set = new HashSet<IntPtr>();
        if (pids.Count == 0) return set;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var owner);
            if (pids.Contains(owner)) set.Add(h);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    // True when the process has another visible, unowned, titled top-level window besides this one.
    private static bool HasOtherWindows(uint pid, IntPtr except)
    {
        if (pid == 0) return false;
        var other = false;
        EnumWindows((h, _) =>
        {
            if (h == except) return true;
            GetWindowThreadProcessId(h, out var owner);
            if (owner != pid || !IsWindowVisible(h)) return true;
            if (GetWindow(h, GwOwner) != IntPtr.Zero) return true;
            if (GetWindowTextLength(h) == 0) return true;
            var ex = GetWindowLongPtr(h, GwlExStyle).ToInt64();
            if ((ex & WsExToolWindow) != 0) return true;
            other = true;
            return false;
        }, IntPtr.Zero);
        return other;
    }

    private void Fail(Session s)
    {
        if (_sessions.TryGetValue(s.Slot, out var current) && ReferenceEquals(current, s))
            _sessions.Remove(s.Slot);
        s.Released = true;
        DestroyFrame(s);
        var failedProcess = s.Process; var failedSlot = s.Slot; // 2026-10-09: off the UI thread
        _ = Task.Run(() => GracefulClose(failedProcess, "slot " + failedSlot + ": embed failed"));
    }

    private static bool SafeExited(Process p)
    {
        try { return p.HasExited; }
        catch { return false; }
    }

    // The largest visible, unowned, resizable or captioned window of the process with a title.
    // Tool windows (splash screens) are skipped.
    private static IntPtr FindMainWindow(HashSet<uint> pids, HashSet<IntPtr>? skip)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((h, _) =>
        {
            if (skip != null && skip.Contains(h)) return true;
            GetWindowThreadProcessId(h, out var owner);
            if (!pids.Contains(owner) || !IsWindowVisible(h)) return true;
            if (GetWindow(h, GwOwner) != IntPtr.Zero) return true;
            var style = GetWindowLongPtr(h, GwlStyle).ToInt64();
            var ex = GetWindowLongPtr(h, GwlExStyle).ToInt64();
            if ((ex & WsExToolWindow) != 0) return true;
            if ((style & WsChild) != 0) return true;
            if ((style & WsThickFrame) == 0 && (style & WsCaption) != WsCaption) return true;
            if (GetWindowTextLength(h) == 0) return true;
            GetWindowRect(h, out var r);
            if (r.Right - r.Left < 400 || r.Bottom - r.Top < 300) return true;
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = h;
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static void Log(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdvancedDirectorCockpitWV3");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "embed.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
        }
        catch
        {
            // Logging is best effort.
        }
    }



    // ---- 2026-10-09 follow a replacement window ----
    // A program whose first window closes soon after it was embedded (Chrome's profile picker, a
    // splash or login window) usually opens its real window next. Within FollowSeconds of embedding,
    // a NEW top-level window of the same program (not open before the drop, not in another pane)
    // is embedded instead of clearing the pane. Windows that were open before the drop are never taken.
    private const int FollowSeconds = 180;
    private const int FollowWaitSeconds = 45;

    private bool TryFollow(Session s, string why)
    {
        if (s.Following || string.IsNullOrEmpty(s.ImageName)) return false;
        if ((DateTime.UtcNow - s.AttachedAt).TotalSeconds > FollowSeconds) return false;
        s.Following = true;
        var gone = s.Window;
        s.Window = IntPtr.Zero;
        if (s.Frame != IntPtr.Zero) ShowWindow(s.Frame, SwHide);
        Log("slot " + s.Slot + ": " + why + " soon after embedding; watching for a new " + s.ImageName + " window (" + FollowWaitSeconds + " s)");
        _ = FollowAsync(s, gone, why);
        return true;
    }

    private async Task FollowAsync(Session s, IntPtr gone, string why)
    {
        var deadline = DateTime.UtcNow.AddSeconds(FollowWaitSeconds);
        IntPtr last = IntPtr.Zero;
        var stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(300);
            if (s.Released || !_sessions.TryGetValue(s.Slot, out var cur) || !ReferenceEquals(cur, s)) return;
            var skip = new HashSet<IntPtr>(s.Before) { gone };
            foreach (var other in _sessions.Values)
                if (!ReferenceEquals(other, s) && other.Window != IntPtr.Zero) skip.Add(other.Window);
            var found = FindMainWindow(PidsOfImage(s.ImageName), skip);
            if (found != IntPtr.Zero && found == last) stable++;
            else stable = found != IntPtr.Zero ? 1 : 0;
            last = found;
            if (stable >= 3)
            {
                s.Following = false;
                s.HandedOff = true; // released with WM_CLOSE to this window only
                Log("slot " + s.Slot + ": following the new " + s.ImageName + " window 0x" + found.ToInt64().ToString("X"));
                Attach(s, found);
                return;
            }
        }
        s.Following = false;
        ClearGone(s, why + " (no new window followed)");
    }

    // ---- 2026-10-09 tabs / pane move / detach ----
    public List<int> Keys() => _sessions.Keys.ToList();

    // Pane title-bar move: the two slots trade programs (no restart).
    public void Swap(int a, int b)
    {
        _sessions.Remove(a, out var sa);
        _sessions.Remove(b, out var sb);
        if (sa != null) { sa.Slot = b; _sessions[b] = sa; }
        if (sb != null) { sb.Slot = a; _sessions[a] = sb; }
        if (sa != null || sb != null) Log("slot " + a + " <-> slot " + b + ": moved (no restart)");
    }

    // Programs of a tab that is not on screen: hidden, still running.
    public void HideWhere(Func<int, bool> pick)
    {
        foreach (var s in _sessions.Values.ToList())
        {
            if (!pick(s.Slot) || !s.Visible) continue;
            s.Visible = false;
            Apply(s);
        }
    }

    public void ReleaseWhere(Func<int, bool> pick)
    {
        foreach (var slot in _sessions.Keys.ToList())
        {
            if (pick(slot)) Release(slot);
        }
    }

    // Detach / re-attach: hand sessions to another window's EmbeddedApps (frame owner changes, program keeps running).
    public List<object> Take(Func<int, bool> pick)
    {
        var list = new List<object>();
        foreach (var slot in _sessions.Keys.ToList())
        {
            if (!pick(slot)) continue;
            _sessions.Remove(slot, out var s);
            if (s == null) continue;
            s.Visible = false;
            if (s.Frame != IntPtr.Zero) { if (s.Shown && s.Window != IntPtr.Zero) Park(s); else ShowWindow(s.Frame, SwHide); }
            list.Add(s);
        }
        return list;
    }

    public void Adopt(IEnumerable<object> items)
    {
        var ownerHwnd = new WindowInteropHelper(_owner).Handle;
        foreach (var o in items)
        {
            if (o is not Session s) continue;
            if (s.Frame != IntPtr.Zero && ownerHwnd != IntPtr.Zero)
                SetWindowLongPtr(s.Frame, GwlHwndParent, ownerHwnd);
            s.Visible = false;
            _sessions[s.Slot] = s;
            Log("slot " + s.Slot + ": moved to another cockpit window (no restart)");
        }
    }

    // Fills the frame with the program's client area: x=0, y=0, frame width and height.
    // Insets are the program's own leftover border (Electron keeps ~8 px); they are pushed outside the frame.
    // A program with a minimum size larger than the pane stays anchored top-left and is clipped by the frame.
    private void FitChild(Session s, int w, int h)
    {
        var win = s.Window;
        if (win == IntPtr.Zero) return;
        if (GetWindowRect(win, out var wr) && GetClientRect(win, out var cr))
        {
            var p = new NativePoint();
            if (ClientToScreen(win, ref p))
            {
                var l = p.X - wr.Left;
                var t = p.Y - wr.Top;
                var r = (wr.Right - wr.Left) - cr.Right - l;
                var b = (wr.Bottom - wr.Top) - cr.Bottom - t;
                if (l >= 0 && l <= 40 && t >= 0 && t <= 60 && r >= 0 && r <= 40 && b >= 0 && b <= 40)
                {
                    s.InsetL = l; s.InsetT = t; s.InsetR = r; s.InsetB = b;
                }
            }
        }
        SetWindowPos(win, IntPtr.Zero, -s.InsetL, -s.InsetT, w + s.InsetL + s.InsetR, h + s.InsetT + s.InsetB,
            SwpNoZOrder | SwpNoActivate);
    }

    // Some programs move or resize their own window after it is embedded (seen with Grok Bot / Electron:
    // window shifted right, gray frame strip on the left). Checked twice a second and put back.
    private bool Drifted(Session s)
    {
        if (Suspended || !s.Visible || s.Window == IntPtr.Zero || s.Frame == IntPtr.Zero) return false;
        if (_owner.WindowState == WindowState.Minimized) return false;
        if (!IsWindowVisible(s.Frame)) return false;
        if (!GetWindowRect(s.Frame, out var fr) || !GetWindowRect(s.Window, out var wr)) return false;
        var fw = fr.Right - fr.Left;
        var fh = fr.Bottom - fr.Top;
        var off = Math.Abs(wr.Left - (fr.Left - s.InsetL)) > 1 || Math.Abs(wr.Top - (fr.Top - s.InsetT)) > 1;
        var small = (wr.Right - wr.Left) < fw + s.InsetL + s.InsetR - 1 || (wr.Bottom - wr.Top) < fh + s.InsetT + s.InsetB - 1;
        if ((off || small) && !s.FitLogged)
        {
            s.FitLogged = true;
            Log("slot " + s.Slot + ": program moved its window inside the pane (" + (wr.Left - fr.Left) + "," + (wr.Top - fr.Top)
                + " " + (wr.Right - wr.Left) + "x" + (wr.Bottom - wr.Top) + " in " + fw + "x" + fh + "); put back to fill the pane");
        }
        return off || small;
    }

    private void RefitSoon(Session s)
    {
        foreach (var ms in new[] { 150, 500, 1200, 2500, 5000 })
        {
            var t = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (!s.Released && _sessions.TryGetValue(s.Slot, out var cur) && ReferenceEquals(cur, s)) Apply(s);
            };
            t.Start();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private const int GwlHwndParent = -8;

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    // ---- 2026-10-09 r6: keep-alive park, hand back on close, keyboard focus ----
    // Cause of the dropped calls: the running build released (WM_CLOSE) the embedded program when a tab
    // switch reused its slot number, and again when the cockpit closed; the program then restarted.
    // Now: tabs keep their own programs (space keys), inactive ones are parked, close hands windows back.

    // Parks a shown frame outside every monitor: still shown, same size, same parent, no resize, no minimize.
    private static void Park(Session s)
    {
        if (s.Frame == IntPtr.Zero) return;
        var x = GetSystemMetrics(SmXVirtualScreen) + GetSystemMetrics(SmCxVirtualScreen) + 4000;
        var y = GetSystemMetrics(SmYVirtualScreen);
        SetWindowPos(s.Frame, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpShowWindow);
        if (!s.Parked) Log("slot " + s.Slot + ": parked off-screen (kept running, not hidden or resized)");
        s.Parked = true;
    }

    // Cockpit closing: every program window is handed back as a normal window, still running (no WM_CLOSE),
    // and remembered so the next cockpit start embeds the same window again instead of starting a new copy.
    public void HandBackAll()
    {
        var keep = new List<string>();
        foreach (var slot in _sessions.Keys.ToList())
        {
            if (!_sessions.Remove(slot, out var s)) continue;
            s.Released = true;
            var window = s.Window;
            Detach(s);
            DestroyFrame(s);
            if (window != IntPtr.Zero && IsWindow(window))
            {
                keep.Add(window.ToInt64() + "\t" + s.WindowPid + "\t" + s.Target);
                Log("slot " + slot + ": cockpit closing: window 0x" + window.ToInt64().ToString("X") + " pid " + s.WindowPid + " handed back running (not closed)");
            }
        }
        lock (HandBackGate)
        {
            var lines = ReadHandBack().Where(l => !keep.Any(k => k.Split('\t')[0] == l.Split('\t')[0])).ToList();
            lines.AddRange(keep);
            try { File.WriteAllLines(HandBackPath, lines); } catch { }
        }
    }

    private static readonly object HandBackGate = new();
    private static string HandBackPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdvancedDirectorCockpitWV3", "handback.txt");

    // Live entries only: the window still exists and still belongs to the same process.
    private static List<string> ReadHandBack()
    {
        var list = new List<string>();
        try
        {
            if (!File.Exists(HandBackPath)) return list;
            foreach (var line in File.ReadAllLines(HandBackPath))
            {
                var p = line.Split('\t');
                if (p.Length < 3 || !long.TryParse(p[0], out var h) || !uint.TryParse(p[1], out var pid)) continue;
                var hwnd = new IntPtr(h);
                if (!IsWindow(hwnd)) continue;
                GetWindowThreadProcessId(hwnd, out var now);
                if (now == pid) list.Add(line);
            }
        }
        catch { }
        return list;
    }

    private bool TryReclaim(int slot, string target, IntPtr ownerHwnd)
    {
        lock (HandBackGate)
        {
            var lines = ReadHandBack();
            foreach (var line in lines)
            {
                var p = line.Split('\t');
                if (!string.Equals(p[2], target, StringComparison.OrdinalIgnoreCase)) continue;
                var hwnd = new IntPtr(long.Parse(p[0]));
                if (GetParent(hwnd) != IntPtr.Zero || _sessions.Values.Any(o => o.Window == hwnd)) continue;
                var frame = WithMixedDpiHosting(() => CreateWindowEx((int)WsExToolWindow, "STATIC", "Embedded app", (uint)(WsPopup | WsClipChildren),
                    0, 0, 1, 1, ownerHwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
                if (frame == IntPtr.Zero) return false;
                HookFrame(frame);
                var s = new Session(slot, target, frame) { HandedOff = true };
                try { s.ImageName = Process.GetProcessById((int)uint.Parse(p[1])).ProcessName; } catch { }
                _sessions[slot] = s;
                lines.Remove(line);
                try { File.WriteAllLines(HandBackPath, lines); } catch { }
                Log("slot " + slot + ": re-embedding the running window 0x" + hwnd.ToInt64().ToString("X") + " handed back when the cockpit closed (no restart)");
                Attach(s, hwnd);
                return true;
            }
        }
        return false;
    }

    // Keyboard: a program window embedded across processes never got keyboard focus (the click activated
    // the frame, focus stayed on the frame). The frame now passes focus on to the program window.
    private readonly Dictionary<IntPtr, IntPtr> _frameOldProcs = new();
    private WndProcDelegate? _frameProc;

    private void HookFrame(IntPtr frame)
    {
        if (frame == IntPtr.Zero) return;
        _frameProc ??= FrameProc;
        var old = SetWindowLongPtr(frame, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_frameProc));
        if (old != IntPtr.Zero) _frameOldProcs[frame] = old;
    }

    private IntPtr FrameProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        _frameOldProcs.TryGetValue(hwnd, out var old);
        if (msg == WmSetFocus)
        {
            var s = _sessions.Values.FirstOrDefault(o => o.Frame == hwnd);
            if (s != null && s.Window != IntPtr.Zero && IsWindow(s.Window))
            {
                FocusApp(s);
                return IntPtr.Zero;
            }
        }
        if (msg == WmMouseActivate) return new IntPtr(MaActivate);
        if (msg == WmActivate)
        {
            // adc-keyfocus: pass the frame's activation on to the embedded window (see ForwardActivation).
            var s = _sessions.Values.FirstOrDefault(o => o.Frame == hwnd);
            if (s != null && s.Window != IntPtr.Zero && IsWindow(s.Window))
                ForwardActivation(s, (wParam.ToInt64() & 0xFFFF) != WaInactive);
        }
        if (msg == WmNcDestroy)
        {
            _frameOldProcs.Remove(hwnd);
            if (old != IntPtr.Zero) SetWindowLongPtr(hwnd, GwlWndProc, old);
        }
        return old != IntPtr.Zero ? CallWindowProc(old, hwnd, msg, wParam, lParam) : DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // adc-keyfocus (v1.5.11): a WS_CHILD window never gets WM_ACTIVATE / WM_NCACTIVATE from Windows; the pane frame
    // is the active window instead. Chromium apps (Chrome, Electron) only give their input method / text input keyboard
    // focus on activation, so without this typed keys and Enter were dropped (paste by menu still worked).
    // Sent with a short timeout (SMTO_ABORTIFHUNG) so a hung program can never freeze the cockpit.
    private static void ForwardActivation(Session s, bool active)
    {
        if (s.Window == IntPtr.Zero || !IsWindow(s.Window)) return;
        if (s.ForwardedActive == active) return;
        s.ForwardedActive = active;
        SendMessageTimeout(s.Window, WmNcActivate, new IntPtr(active ? 1 : 0), IntPtr.Zero, SmtoAbortIfHung, 300, out _);
        SendMessageTimeout(s.Window, WmActivate, new IntPtr(active ? WaActive : WaInactive), IntPtr.Zero, SmtoAbortIfHung, 300, out _);
        if (!s.ActivationLogged) { s.ActivationLogged = true; Log("slot " + s.Slot + ": activation passed on to the program window (" + (active ? "active" : "inactive") + ")"); }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    private const uint WmActivate = 0x0006;
    private const uint WmNcActivate = 0x0086;
    private const int WaInactive = 0;
    private const int WaActive = 1;
    private const uint SmtoAbortIfHung = 0x0002;

    private static void FocusApp(Session s)
    {
        ForwardActivation(s, true); // adc-keyfocus: activation first, then focus (the order Windows uses for top-level windows)
        var target = s.Window;
        SetFocus(target);
        var f = GetFocus();
        var ok = f == target || IsChild(target, f);
        if (!ok)
        {
            var me = GetCurrentThreadId();
            var them = GetWindowThreadProcessId(target, out _);
            var attached = them != 0 && them != me && AttachThreadInput(me, them, true);
            SetFocus(target);
            f = GetFocus();
            ok = f == target || IsChild(target, f);
            if (attached) AttachThreadInput(me, them, false);
        }
        if (!s.FocusLogged) { s.FocusLogged = true; Log("slot " + s.Slot + ": keyboard focus to the program window " + (ok ? "ok" : "FAILED")); }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int GwlWndProc = -4;
    private const uint WmSetFocus = 0x0007;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmNcDestroy = 0x0082;
    private const int MaActivate = 1;
    private const uint SwpNoSize = 0x0001;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;

    // ---- adc-v157: hand-off helpers ----
    // The pane shows this line (page __adcEmbedNote); used when a running app gives no window.
    public Action<int, string>? Note { get; set; }

    private void SendNote(int slot, string text)
    {
        try { Note?.Invoke(slot, text); }
        catch { }
    }

    private const int HandOffAdoptSeconds = 5;
    private const int HandOffNoteSeconds = 5;
    private const int HandOffNoteWaitSeconds = 120;

    // Windows held by any pane in any cockpit window of this process (never taken twice).
    private static HashSet<IntPtr> HeldWindows(Session self)
    {
        var set = new HashSet<IntPtr>();
        lock (AllGate)
        {
            foreach (var apps in All)
                foreach (var other in apps._sessions.Values)
                    if (!ReferenceEquals(other, self) && other.Window != IntPtr.Zero) set.Add(other.Window);
        }
        return set;
    }

    // Arguments that make a running copy show its main window, per exe name. Only apps where this was tested.
    // 2026-11-01: when a browser card hands off to an already-running Chrome/Edge and no new
    // window appears for embedding, open the card's HTML/URL in that existing instance (new tab)
    // instead of blocking with the tray-icon note.
    private bool TryOpenDocumentInRunningBrowser(Session s)
    {
        if (string.IsNullOrEmpty(s.Exe)) return false;
        if (!EmbedShell.NeverAdoptRunning(s.ImageName)) return false;
        var doc = DocumentFromBrowserArgs(s.Args);
        if (doc == null) return false;
        try
        {
            var arg = "\"" + doc.Replace("\"", "") + "\"";
            using var p = Process.Start(new ProcessStartInfo(s.Exe)
            {
                UseShellExecute = false,
                Arguments = arg,
                WorkingDirectory = Path.GetDirectoryName(s.Exe) ?? ""
            });
            Log("slot " + s.Slot + ": " + s.ImageName + " already running; opened document in that instance: " + doc);
            return true;
        }
        catch (Exception ex)
        {
            Log("slot " + s.Slot + ": open-in-running-browser failed: " + ex.Message);
            return false;
        }
    }

    private static string? DocumentFromBrowserArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return null;
        var candidates = new List<string>();
        var a = args;
        var i = 0;
        while (i < a.Length)
        {
            while (i < a.Length && char.IsWhiteSpace(a[i])) i++;
            if (i >= a.Length) break;
            if (a[i] == '"')
            {
                var j = a.IndexOf('"', i + 1);
                if (j < 0) break;
                candidates.Add(a.Substring(i + 1, j - i - 1));
                i = j + 1;
            }
            else
            {
                var j = i;
                while (j < a.Length && !char.IsWhiteSpace(a[j])) j++;
                candidates.Add(a.Substring(i, j - i));
                i = j;
            }
        }
        for (var k = candidates.Count - 1; k >= 0; k--)
        {
            var v = candidates[k].Trim();
            if (v.StartsWith("--", StringComparison.Ordinal)) continue;
            if (IsOpenableDocument(v)) return v;
        }
        return null;
    }

    private static bool IsOpenableDocument(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return false;
        if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) return true;
        try { return File.Exists(v); } catch { return false; }
    }

    private static string? ShowArgs(string image)
    {
        return ShowArgsByImage.TryGetValue(image, out var a) ? a : null;
    }

    private static readonly Dictionary<string, string> ShowArgsByImage = new(StringComparer.OrdinalIgnoreCase)
    {
        // none proven yet
    };

    private void ShowRunning(Session s, string args)
    {
        try
        {
            if (string.IsNullOrEmpty(s.Exe)) return;
            using var p = Process.Start(new ProcessStartInfo(s.Exe) { UseShellExecute = false, Arguments = args, WorkingDirectory = Path.GetDirectoryName(s.Exe) ?? "" });
            Log("slot " + s.Slot + ": no window from the running " + s.ImageName + "; started it again with " + args + " to show its main window (pid " + SafeId(p) + ")");
        }
        catch (Exception ex)
        {
            Log("slot " + s.Slot + ": show-window start failed: " + ex.Message);
        }
    }

    // Once per hand-off: hidden top-level windows of the program (class, size; never the title text).
    private static void LogHidden(Session s, HashSet<uint> pids)
    {
        try
        {
            var total = 0;
            var parts = new List<string>();
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var owner);
                if (!pids.Contains(owner) || IsWindowVisible(h)) return true;
                total++;
                GetWindowRect(h, out var r);
                var w = r.Right - r.Left;
                var hh = r.Bottom - r.Top;
                if (w < 200 || hh < 100 || parts.Count >= 6) return true;
                var cls = new System.Text.StringBuilder(128);
                GetClassName(h, cls, cls.Capacity);
                parts.Add("0x" + h.ToInt64().ToString("X") + " " + cls + " " + w + "x" + hh + " style 0x" + GetWindowLongPtr(h, GwlStyle).ToInt64().ToString("X"));
                return true;
            }, IntPtr.Zero);
            Log("slot " + s.Slot + ": " + s.ImageName + " has " + total + " hidden top-level windows" + (parts.Count > 0 ? "; larger ones: " + string.Join("; ", parts) : ""));
        }
        catch { }
    }

    private static string Awareness(IntPtr hwnd)
    {
        try
        {
            var a = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd));
            return a switch { 0 => "unaware", 1 => "system", 2 => "per-monitor", _ => a.ToString() };
        }
        catch { return "?"; }
    }

    // The pane frame is created with mixed DPI hosting, so a window with another DPI awareness (1Password is
    // per-monitor aware, the cockpit is system aware) can be reparented into it. Older Windows: plain create.
    private static IntPtr WithMixedDpiHosting(Func<IntPtr> create)
    {
        var prev = -1;
        try { prev = SetThreadDpiHostingBehavior(DpiHostingMixed); }
        catch { prev = -1; }
        try { return create(); }
        finally
        {
            if (prev >= 0)
            {
                try { SetThreadDpiHostingBehavior(prev); }
                catch { }
            }
        }
    }

    private const int DpiHostingMixed = 1;
    private const uint GaParent = 1;
    private const uint SwpNoMove = 0x0002;

    [DllImport("user32.dll")]
    private static extern int SetThreadDpiHostingBehavior(int value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    private sealed class Session
    {
        public Session(int slot, string target, IntPtr frame)
        {
            Slot = slot;
            Target = target;
            Frame = frame;
        }

        public int Slot { get; set; }
        public string Target { get; }
        public IntPtr Frame { get; set; }
        public Process? Process { get; set; }
        public IntPtr Window { get; set; }
        public bool? ForwardedActive { get; set; } // adc-keyfocus: last activation state passed on (null = never)
        public bool ActivationLogged { get; set; }
        public IntPtr OriginalStyle;
        public IntPtr OriginalExStyle;
        public NativeRect OriginalRect;
        public Rect Rect { get; set; }
        public bool Visible { get; set; }
        public bool Released { get; set; }
        public string ImageName { get; set; } = "";
        public HashSet<IntPtr> Before { get; set; } = new();
        public HashSet<IntPtr> HiddenBefore { get; set; } = new(); // adc-v156
        public HashSet<IntPtr> Described { get; } = new(); // adc-v156: hand-off windows already logged
        public bool HandedOff { get; set; }
        public DateTime HandOffAt; // adc-v157
        public bool AdoptLogged, HiddenLogged, AutoShown, NoteSent, OpenedInRunningBrowser; // adc-v157; OpenedInRunningBrowser 2026-11-01
        public string Exe { get; set; } = ""; // adc-v157
        public uint WindowPid { get; set; }
        public int InsetL, InsetT, InsetR, InsetB;
        public bool FitLogged;
        public string Args { get; set; } = "";
        public DateTime AttachedAt;
        public bool Following;
        public bool Shown;
        public bool Parked;
        public bool FocusLogged;
    }

    private const int StartTimeoutSeconds = 150;
    private const int HandOffTimeoutSeconds = 30;
    private const uint WmClose = 0x0010;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint GwOwner = 4;
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const long WsPopup = 0x80000000L;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsClipSiblings = 0x04000000L;
    private const long WsClipChildren = 0x02000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsExDlgModalFrame = 0x00000001L;
    private const long WsExTopmost = 0x00000008L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;
    private const long WsExStaticEdge = 0x00020000L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max); // adc-v156

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}