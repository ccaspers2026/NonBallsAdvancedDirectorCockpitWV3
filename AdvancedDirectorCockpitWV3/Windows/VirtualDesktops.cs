using System.Runtime.InteropServices;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-11-06 documented IVirtualDesktopManager (Windows 10 1607+ / Windows 11).
// Cockpit HWNDs only. The undocumented IVirtualDesktopManagerInternal changes every Windows 11 build, so it is not used.
internal static class VirtualDesktops
{
    internal static Guid? TryCurrent(IntPtr reference)
    {
        try
        {
            var mgr = Create();
            if (mgr == null) return null;
            try
            {
                if (reference != IntPtr.Zero && OnCurrent(mgr, reference) && TryId(mgr, reference, out var id))
                    return id;
                var probe = Probe();
                if (probe == IntPtr.Zero) return null;
                try { return TryId(mgr, probe, out var probed) ? probed : null; }
                finally { DestroyWindow(probe); }
            }
            finally { Release(mgr); }
        }
        catch { return null; }
    }

    // "moved", "already", "unavailable", or "failed ...". Never throws.
    internal static string Move(IntPtr hwnd, Guid desktop)
    {
        if (hwnd == IntPtr.Zero || desktop == Guid.Empty) return "unavailable";
        try
        {
            var mgr = Create();
            if (mgr == null) return "unavailable";
            try
            {
                var windowDesktop = Guid.Empty;
                TryId(mgr, hwnd, out windowDesktop);
                if (!WindowGroupApply.ShouldMove(desktop, windowDesktop, hwnd)) return "already";
                var hr = mgr.MoveWindowToDesktop(hwnd, ref desktop);
                return hr == 0 ? "moved" : "failed hr=0x" + hr.ToString("X8");
            }
            finally { Release(mgr); }
        }
        catch (Exception ex) { return "failed " + ex.GetType().Name; }
    }

    private static bool OnCurrent(IVirtualDesktopManager mgr, IntPtr hwnd)
    {
        var hr = mgr.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent);
        return hr == 0 && onCurrent != 0;
    }

    private static bool TryId(IVirtualDesktopManager mgr, IntPtr hwnd, out Guid id)
    {
        id = Guid.Empty;
        return mgr.GetWindowDesktopId(hwnd, out id) == 0 && id != Guid.Empty;
    }

    private static IVirtualDesktopManager? Create()
    {
        try
        {
            var created = (object)new VirtualDesktopManagerCom();
            if (created is IVirtualDesktopManager mgr) return mgr;
            Release(created);
        }
        catch { }
        return null;
    }

    private static void Release(object com) { try { Marshal.ReleaseComObject(com); } catch { } }

    // A new visible popup is assigned to the virtual desktop the user is viewing.
    private static IntPtr Probe() => CreateWindowExW(
        0x00000080 | 0x08000000, // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        "Static", "",
        unchecked((int)0x80000000) | 0x10000000, // WS_POPUP | WS_VISIBLE
        -32000, -32000, 1, 1,
        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerCom { }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }
}
