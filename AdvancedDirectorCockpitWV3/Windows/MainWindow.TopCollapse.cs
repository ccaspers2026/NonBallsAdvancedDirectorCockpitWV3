using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-10-12 adc-topcol (WV3 1.5.12 / DB 1.5.10): Ctrl+Alt+T toggles the page's per-group "Collapse top".
// The page alone cannot see the key while an embedded program (Chrome, MT5...) or a web pane has keyboard focus,
// so the host registers Ctrl+Alt+T as a hotkey, but ONLY while this cockpit window or a window it owns (its pane
// frames, pop-ups) is the foreground window; it is unregistered as soon as another program comes to the front, so
// Ctrl+Alt+T keeps working normally everywhere else. Each cockpit window registers for itself (multi-window safe).
public partial class MainWindow
{
    private const int TopColHotkeyId = 0xAD12;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000, VkT = 0x54;
    private const int WmHotkey = 0x0312;
    private const uint EventSystemForeground = 0x0003, WineventOutOfContext = 0x0000;
    private bool _topColInit, _topColRegistered;
    private IntPtr _topColHook;
    private WinEventProc? _topColProc;
    private HwndSource? _topColSource;

    private void InitTopCollapseHotkey()
    {
        if (_topColInit) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _topColInit = true;
        _topColSource = HwndSource.FromHwnd(hwnd);
        _topColSource?.AddHook(TopColWndProc);
        _topColProc = (_, _, _, _, _, _, _) => { try { Dispatcher.BeginInvoke(new Action(UpdateTopColHotkey)); } catch { } };
        _topColHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _topColProc, 0, 0, WineventOutOfContext);
        Closed += (_, _) =>
        {
            try { if (_topColRegistered) UnregisterHotKey(hwnd, TopColHotkeyId); } catch { }
            _topColRegistered = false;
            try { if (_topColHook != IntPtr.Zero) UnhookWinEvent(_topColHook); } catch { }
            _topColHook = IntPtr.Zero;
            try { _topColSource?.RemoveHook(TopColWndProc); } catch { }
        };
        UpdateTopColHotkey();
    }

    // true when the foreground window is this cockpit window or owned by it (pane frames hold the embedded programs).
    private bool CockpitIsForeground(IntPtr me)
    {
        var fg = TcForeground();
        for (var i = 0; fg != IntPtr.Zero && i < 8; i++)
        {
            if (fg == me) return true;
            fg = TcGetWindow(fg, 4 /* GW_OWNER */);
        }
        return false;
    }

    private void UpdateTopColHotkey()
    {
        var me = new WindowInteropHelper(this).Handle;
        if (me == IntPtr.Zero) return;
        var want = CockpitIsForeground(me);
        if (want == _topColRegistered) return;
        if (want) _topColRegistered = RegisterHotKey(me, TopColHotkeyId, ModControl | ModAlt | ModNoRepeat, VkT);
        else { UnregisterHotKey(me, TopColHotkeyId); _topColRegistered = false; }
    }

    private IntPtr TopColWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == TopColHotkeyId)
        {
            handled = true;
            PostShell("{\"type\":\"adc-topcol-toggle\"}");
        }
        return IntPtr.Zero;
    }

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern IntPtr TcForeground();
    [DllImport("user32.dll", EntryPoint = "GetWindow")] private static extern IntPtr TcGetWindow(IntPtr hwnd, uint cmd);
}
