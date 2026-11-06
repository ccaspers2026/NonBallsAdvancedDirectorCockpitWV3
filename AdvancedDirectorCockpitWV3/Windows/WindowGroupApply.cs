namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-11-06 decisions for applying a saved cockpit group. Kept separate from the Win32 calls so the
// shell tests can lock the contract: already-maximized windows must be placed as normal first, and
// cockpit windows that are not in the group are hidden (never closed).
internal static class WindowGroupApply
{
    internal const int ShowNormal = 1;       // SW_SHOWNORMAL
    internal const int ShowMaximized = 3;    // SW_SHOWMAXIMIZED

    internal enum Dismiss { Keep, Hide }

    internal readonly record struct PlacementPlan(int[] ShowCmds);

    // placeThenMaximize: the window is (or was) maximized. SW_SHOWMAXIMIZED alone then only updates
    // the hidden restore rect and leaves the window on its current monitor.
    internal static PlacementPlan PlanPlacement(bool maximize, bool placeThenMaximize)
    {
        if (maximize && placeThenMaximize)
            return new PlacementPlan(new[] { ShowNormal, ShowMaximized });
        return new PlacementPlan(new[] { maximize ? ShowMaximized : ShowNormal });
    }

    // Detached tabs are not part of a group. Anything else that is open but absent is hidden.
    internal static Dismiss PlanDismiss(string? key, bool detached, IEnumerable<string> groupKeys)
    {
        if (detached || string.IsNullOrEmpty(key)) return Dismiss.Keep;
        foreach (var k in groupKeys)
            if (string.Equals(k, key, StringComparison.Ordinal)) return Dismiss.Keep;
        return Dismiss.Hide;
    }

    // Unknown window desktop (Guid.Empty) still moves when the current desktop is known.
    // No current desktop, or no HWND: leave the window where it is.
    internal static bool ShouldMove(Guid currentDesktop, Guid windowDesktop, IntPtr hwnd) =>
        hwnd != IntPtr.Zero && currentDesktop != Guid.Empty && windowDesktop != currentDesktop;
}
