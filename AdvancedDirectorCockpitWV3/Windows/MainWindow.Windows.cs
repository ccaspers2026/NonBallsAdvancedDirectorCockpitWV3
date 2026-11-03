using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-10-09 multi-window. Every cockpit window (the main one, "New window" windows, detached tabs) is
// created by OpenCockpitWindow and lives in this one process. Each window has a key ("main", "w2", "w3",
// "solo:<tab>") and remembers its monitor, position, size and maximized state in
// %LOCALAPPDATA%\AdvancedDirectorCockpitWV3\windows.json. It opens on the monitor it was last on, or on the
// primary monitor when that monitor is gone. New windows ("w2"...) also keep their own active tab and pane
// layout (page storage keys are prefixed per window), and windows that were open when the cockpit closed
// open again on the next start.
public partial class MainWindow
{
    internal sealed class WindowSpec
    {
        public string Key = "main";
        public string? ConfigId;
        public string? Solo;
        public Windows.MainWindow? From;
        public string? Title;
    }

    private string _winKey = "main";
    private static readonly object WinFileGate = new();
    private static DateTime _lastNewWindow = DateTime.MinValue;
    private static bool _appClosing;

    private static string WindowsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdvancedDirectorCockpitWV3", "windows.json");

    // App start: the main window plus every "New window" window that was open when the cockpit last closed.
    internal static void StartWindows()
    {
        var main = OpenCockpitWindow(new WindowSpec { Key = "main" });
        Application.Current.MainWindow = main;
        foreach (var key in LoadWindowStates().Where(kv => kv.Key.StartsWith("w", StringComparison.Ordinal) && kv.Value.Open).Select(kv => kv.Key).OrderBy(k => k))
            OpenCockpitWindow(new WindowSpec { Key = key });
        main.Activate();
    }

    internal static void HandleInstanceCommand(string cmd)
    {
        if (string.Equals(cmd, "new-window", StringComparison.OrdinalIgnoreCase))
        {
            OpenNewWindow();
            return;
        }
        var main = Application.Current.MainWindow as Windows.MainWindow ?? Application.Current.Windows.OfType<Windows.MainWindow>().FirstOrDefault();
        if (main == null) { OpenCockpitWindow(new WindowSpec { Key = "main" }); return; }
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
        main.Topmost = true; main.Topmost = false;
        main.Focus();
    }

    // "New window" button, Ctrl+Shift+N, or exe --new-window.
    internal static Windows.MainWindow? OpenNewWindow()
    {
        if ((DateTime.UtcNow - _lastNewWindow).TotalMilliseconds < 800) return null; // one press, one window
        _lastNewWindow = DateTime.UtcNow;
        var used = Application.Current.Windows.OfType<Windows.MainWindow>().Select(w => w._winKey).ToHashSet();
        var n = 2;
        while (used.Contains("w" + n)) n++;
        var w = OpenCockpitWindow(new WindowSpec { Key = "w" + n });
        w.Activate();
        return w;
    }

    // The one place cockpit windows are created and shown (main, new windows, detached tabs).
    internal static Windows.MainWindow OpenCockpitWindow(WindowSpec spec)
    {
        var w = spec.Solo != null && spec.From != null
            ? new Windows.MainWindow(spec.ConfigId, spec.Solo, spec.From)
            : new Windows.MainWindow(spec.ConfigId);
        w._winKey = spec.Key;
        if (spec.Title != null) w.Title = spec.Title;
        else if (spec.Key.StartsWith("w", StringComparison.Ordinal)) w.Title = "Advanced Director Cockpit WV3 - Window " + spec.Key.Substring(1);
        w.Title = AdcVersion.Titled(w.Title); // 2026-10-09 version in every window title
        var states = LoadWindowStates();
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        if (spec.From != null)
        {
            // default for a detached tab: next to the window it came from
            w.Width = Math.Max(w.MinWidth, spec.From.ActualWidth * 0.8);
            w.Height = Math.Max(w.MinHeight, spec.From.ActualHeight * 0.85);
            w.Left = spec.From.Left + 60;
            w.Top = spec.From.Top + 60;
        }
        else if (spec.Key != "main" && !states.ContainsKey(spec.Key))
        {
            // a brand-new window: on the next monitor after the one the main window is on
            PlaceOnNextMonitor(w);
        }
        else if (spec.Key == "main" && !states.ContainsKey("main"))
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        w.SourceInitialized += (_, _) => w.RestorePlacement();
        w.Closing += (_, _) => w.SavePlacement(closing: true);
        w.LocationChanged += (_, _) => w.SavePlacementSoon();
        w.SizeChanged += (_, _) => w.SavePlacementSoon();
        w.StateChanged += (_, _) => w.SavePlacementSoon();
        w.PreviewKeyDown += (_, e) =>
        {
            var mods = Keyboard.Modifiers;
            if (e.Key == System.Windows.Input.Key.N && mods == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;
                OpenNewWindow();
            }
        };
        if (spec.Key == "main")
        {
            // Closing the main window closes the whole cockpit (all its windows), so the process ends and a
            // staged update can install. The other windows remember they were open and come back next start.
            w.Closed += (_, _) =>
            {
                _appClosing = true;
                foreach (var other in Application.Current.Windows.OfType<Window>().Where(x => x != w).ToList())
                {
                    try { other.Close(); } catch { }
                }
            };
        }
        w.Show();
        return w;
    }

    private static void PlaceOnNextMonitor(Windows.MainWindow w)
    {
        var main = Application.Current.MainWindow as Windows.MainWindow;
        var mons = ListMonitorWorkAreas();
        w.Width = 1600; w.Height = 950;
        if (mons.Count == 0 || main == null) { w.Left = 80; w.Top = 80; return; }
        var center = new Point(main.Left + main.ActualWidth / 2, main.Top + main.ActualHeight / 2);
        var idx = mons.FindIndex(m => m.Contains(center));
        var target = mons[(idx + 1) % mons.Count];
        w.Width = Math.Min(w.Width, target.Width - 48);
        w.Height = Math.Min(w.Height, target.Height - 48);
        w.Left = target.X + 24;
        w.Top = target.Y + 24;
    }

    // ---- placement (Win32 window placement keeps per-monitor DPI right) ----
    private System.Windows.Threading.DispatcherTimer? _placeTimer;

    private void SavePlacementSoon()
    {
        if (!IsLoaded) return;
        _placeTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), System.Windows.Threading.DispatcherPriority.Background, (_, _) => { _placeTimer!.Stop(); SavePlacement(closing: false); }, Dispatcher);
        _placeTimer.Stop();
        _placeTimer.Start();
    }

    private void SavePlacement(bool closing)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref wp)) return;
            var tab = "";
            var st = new WinState
            {
                L = wp.rcNormalPosition.L, T = wp.rcNormalPosition.T, R = wp.rcNormalPosition.R, B = wp.rcNormalPosition.B,
                Max = wp.showCmd == 3 || WindowState == WindowState.Maximized,
                Monitor = MonitorName(hwnd),
                Open = true,
                Tab = tab
            };
            if (closing && _winKey != "main")
            {
                // a new window closed while the cockpit keeps running is not reopened next start;
                // when the whole cockpit is closing, every open window is reopened next start
                st.Open = _appClosing;
            }
            lock (WinFileGate)
            {
                var all = LoadWindowStates();
                all[_winKey] = st;
                Directory.CreateDirectory(Path.GetDirectoryName(WindowsFile)!);
                File.WriteAllText(WindowsFile, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // Placement memory is best effort.
        }
    }

    private void RestorePlacement()
    {
        try
        {
            if (!LoadWindowStates().TryGetValue(_winKey, out var st)) return;
            var rect = new WRect { L = st.L, T = st.T, R = st.R, B = st.B };
            if (rect.R - rect.L < 200 || rect.B - rect.T < 150) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            // the monitor it was on must still exist; otherwise the window opens on the primary monitor
            if (MonitorFromRect(ref rect, 0 /* MONITOR_DEFAULTTONULL */) == IntPtr.Zero)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                var wa = SystemParameters.WorkArea;
                Width = Math.Min(Math.Max(MinWidth, (rect.R - rect.L)), wa.Width);
                Height = Math.Min(Math.Max(MinHeight, (rect.B - rect.T)), wa.Height);
                Left = wa.Left + (wa.Width - Width) / 2;
                Top = wa.Top + (wa.Height - Height) / 2;
                Log("window " + _winKey + ": saved monitor " + st.Monitor + " is gone; opened on the primary monitor");
                return;
            }
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>(), showCmd = st.Max ? 3 : 1, rcNormalPosition = rect, ptMinPosition = new WPt { X = -1, Y = -1 }, ptMaxPosition = new WPt { X = -1, Y = -1 } };
            SetWindowPlacement(hwnd, ref wp);
        }
        catch
        {
            // Default placement.
        }
    }

    private static Dictionary<string, WinState> LoadWindowStates()
    {
        lock (WinFileGate)
        {
            try
            {
                if (File.Exists(WindowsFile))
                    return JsonSerializer.Deserialize<Dictionary<string, WinState>>(File.ReadAllText(WindowsFile)) ?? new();
            }
            catch { }
            return new();
        }
    }

    private static void Log(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdvancedDirectorCockpitWV3");
            File.AppendAllText(Path.Combine(dir, "windows.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
        }
        catch { }
    }

    // Page storage per window: windows "w2", "w3"... keep their own active tab, pane layouts and detached
    // tabs. The main window and detached tabs keep the shared keys (unchanged behaviour).
    private string WindowStorageScript()
    {
        if (!_winKey.StartsWith("w", StringComparison.Ordinal)) return "window.__adcWindowKey='" + _winKey.Replace("'", "") + "';";
        var p = "adc-" + _winKey + ":";
        return "(function(){window.__adcWindowKey='" + _winKey + "';var P='" + p + "',K={'lp-active-tab':1,'lp-tab-stages':1,'lp-stage-sizes':1,'adc-detached-tabs':1};"
            + "var S=Storage.prototype,g=S.getItem,s=S.setItem,r=S.removeItem;"
            + "function own(o,k){try{return o===window.localStorage&&K.hasOwnProperty(k);}catch(e){return false;}}"
            + "S.getItem=function(k){if(own(this,k)){var v=g.call(this,P+k);if(v===null&&k==='lp-active-tab')v=g.call(this,k);return v;}return g.call(this,k);};"
            + "S.setItem=function(k,v){return own(this,k)?s.call(this,P+k,v):s.call(this,k,v);};"
            + "S.removeItem=function(k){return own(this,k)?r.call(this,P+k):r.call(this,k);};})();";
    }

    private void ApplyNewWindow() => OpenNewWindow();

    private sealed class WinState
    {
        public int L { get; set; }
        public int T { get; set; }
        public int R { get; set; }
        public int B { get; set; }
        public bool Max { get; set; }
        public string Monitor { get; set; } = "";
        public bool Open { get; set; }
        public string Tab { get; set; } = "";
    }

    private static string MonitorName(IntPtr hwnd)
    {
        var m = MonitorFromWindow2(hwnd, 2);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return m != IntPtr.Zero && GetMonitorInfoEx(m, ref info) ? info.szDevice : "";
    }

    [StructLayout(LayoutKind.Sequential)] private struct WRect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct WPt { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT { public int length, flags, showCmd; public WPt ptMinPosition, ptMaxPosition; public WRect rcNormalPosition; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX { public int cbSize; public WRect rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref WRect r, uint flags);
    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")] private static extern IntPtr MonitorFromWindow2(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfoEx(IntPtr m, ref MONITORINFOEX info);
}
