using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-11-04 window groups (slice 1): page dropdown lists named groups, "Save current" snapshots every open
// cockpit window (main + "New window" windows; detached tabs are skipped) with its monitor and position,
// and picking a group opens/moves those windows to their saved monitor + position.
// 2026-11-06: that placement lands on the virtual desktop the user is viewing. Cockpit windows that are
// open but not in the group are hidden (not closed: closing main exits the app, and closing any cockpit
// window hands its embedded programs back to the desktop). Detached tabs stay.
public partial class MainWindow
{
    private void ApplyWindowGroupsList() => _ = ReplyWindowGroupsAsync(null, null);

    private void ApplyWindowGroupSave(JsonElement root)
    {
        var name = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "").Trim() : "";
        if (name.Length == 0) { _ = ReplyWindowGroupsAsync("Group name is required.", null); return; }
        var group = new WindowGroup { Name = name, SavedUtc = DateTime.UtcNow };
        foreach (var w in Application.Current.Windows.OfType<MainWindow>())
        {
            if (w._soloSpace != null || !w.IsVisible) continue; // hidden = not in the current layout
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) continue;
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref wp)) continue;
            group.Windows.Add(new WindowGroupWindow
            {
                Key = w._winKey,
                Monitor = MonitorName(hwnd),
                L = wp.rcNormalPosition.L, T = wp.rcNormalPosition.T, R = wp.rcNormalPosition.R, B = wp.rcNormalPosition.B,
                Max = wp.showCmd == 3 || w.WindowState == WindowState.Maximized,
                ConfigId = w._bootConfigId
            });
        }
        group.Windows.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        WindowGroupStore.Upsert(group);
        Log("window group saved: " + name + " (" + group.Windows.Count + " windows)");
        _ = ReplyWindowGroupsAsync("Saved group \"" + name + "\" (" + group.Windows.Count + " windows).", name);
    }

    private void ApplyWindowGroupOpen(JsonElement root)
    {
        var name = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "").Trim() : "";
        var group = WindowGroupStore.Find(name);
        if (group == null) { _ = ReplyWindowGroupsAsync("Group \"" + name + "\" not found.", null); return; }
        var desktop = VirtualDesktops.TryCurrent(new WindowInteropHelper(this).Handle);
        if (desktop == null) Log("window group " + name + ": current virtual desktop unavailable; placing only");
        var keys = group.Windows.Select(w => w.Key).ToArray();
        var opened = 0;
        foreach (var gw in group.Windows)
        {
            try
            {
                // Seed windows.json so a newly created window restores to the saved spot on SourceInitialized.
                WriteWindowState(gw.Key, new WinState { L = gw.L, T = gw.T, R = gw.R, B = gw.B, Max = gw.Max, Monitor = gw.Monitor, Open = true });
                var w = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(x => x._soloSpace == null && x._winKey == gw.Key);
                if (w == null)
                {
                    if (gw.Key == "main") continue;
                    // SourceInitialized moves the new window onto this desktop, then places the saved rect.
                    OpenCockpitWindow(new WindowSpec { Key = gw.Key, ConfigId = gw.ConfigId, CurrentDesktop = desktop, GroupPlacement = true });
                }
                else
                {
                    // Move before restoring. ShowWindow(SW_RESTORE) activates, and activating a window on
                    // another virtual desktop switches to that desktop instead of bringing the window here.
                    w._placementSuspended = false;
                    w.MoveOntoDesktop(desktop);
                    if (w.WindowState != WindowState.Normal) w.WindowState = WindowState.Normal;
                    if (!w.IsVisible) w.Show();
                    w.RestorePlacement(placeThenMaximize: true);
                }
                opened++;
            }
            catch (Exception ex) { Log("window group " + name + ": " + gw.Key + " failed: " + ex.Message); }
        }
        var self = WindowGroupApply.PlanDismiss(_winKey, _soloSpace != null, keys);
        foreach (var w in Application.Current.Windows.OfType<MainWindow>().ToList())
        {
            if (ReferenceEquals(w, this)) continue;
            var action = WindowGroupApply.PlanDismiss(w._winKey, w._soloSpace != null, keys);
            if (action != WindowGroupApply.Dismiss.Keep) w.DismissNotInGroup(group.Name, action);
        }
        var toast = self == WindowGroupApply.Dismiss.Keep ? this
            : Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => WindowGroupApply.PlanDismiss(w._winKey, w._soloSpace != null, keys) == WindowGroupApply.Dismiss.Keep) ?? this;
        Log("window group opened: " + name + " (" + opened + "/" + group.Windows.Count + ")");
        _ = toast.ReplyWindowGroupsAsync("Opened group \"" + group.Name + "\" (" + opened + " windows).", group.Name);
        if (self != WindowGroupApply.Dismiss.Keep)
        {
            // The click landed on a cockpit that is not in the group. Let its toast start, then dismiss it.
            if (ReferenceEquals(toast, this)) Dispatcher.BeginInvoke(new Action(() => DismissNotInGroup(group.Name, self)));
            else DismissNotInGroup(group.Name, self);
        }
    }

    private void MoveOntoDesktop(Guid? desktop)
    {
        if (desktop is not Guid id || id == Guid.Empty) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) { Log("window " + _winKey + ": virtual desktop unavailable"); return; }
        var result = VirtualDesktops.Move(hwnd, id);
        // Some maximized windows refuse the move until they are restored. Only then is it safe to
        // activate: the first attempt did not switch the desktop the user is viewing.
        if (result != "moved" && result != "already" && WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
            var again = VirtualDesktops.Move(new WindowInteropHelper(this).Handle, id);
            Log("window " + _winKey + ": virtual desktop " + again + " after restore (" + result + ")");
            return;
        }
        Log("window " + _winKey + ": virtual desktop " + result);
    }

    // Hide, do not close. Close() hands embedded programs back to the desktop and, for main, exits the app.
    private void DismissNotInGroup(string groupName, WindowGroupApply.Dismiss action)
    {
        if (action != WindowGroupApply.Dismiss.Hide) return;
        _placementSuspended = true; // a late placement timer must not record the hidden window as the layout
        _placeTimer?.Stop();
        try { if (IsVisible) Hide(); }
        catch (Exception ex) { Log("window group " + groupName + ": hide " + _winKey + " failed: " + ex.Message); return; }
        _placeTimer?.Stop();
        Log("window group " + groupName + ": hid " + _winKey);
    }

    private static void WriteWindowState(string key, WinState st)
    {
        lock (WinFileGate)
        {
            var all = LoadWindowStates();
            all[key] = st;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowsFile)!);
            System.IO.File.WriteAllText(WindowsFile, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private async Task ReplyWindowGroupsAsync(string? message, string? selected)
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = "adc-window-groups",
                groups = WindowGroupStore.Load().Select(g => new { name = g.Name, count = g.Windows.Count }),
                message,
                selected
            });
            await core.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('adc-host',{detail:" + payload + "}));");
        }
        catch { }
    }
}
