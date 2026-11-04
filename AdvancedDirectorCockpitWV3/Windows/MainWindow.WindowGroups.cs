using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-11-04 window groups (slice 1): page dropdown lists named groups, "Save current" snapshots every open
// cockpit window (main + "New window" windows; detached tabs are skipped) with its monitor and position,
// and picking a group opens/moves those windows to their saved monitor + position. No virtual desktops.
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
            if (w._soloSpace != null) continue;
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
                    w = OpenCockpitWindow(new WindowSpec { Key = gw.Key, ConfigId = gw.ConfigId });
                }
                else
                {
                    if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                    w.RestorePlacement();
                }
                opened++;
            }
            catch (Exception ex) { Log("window group " + name + ": " + gw.Key + " failed: " + ex.Message); }
        }
        Log("window group opened: " + name + " (" + opened + "/" + group.Windows.Count + ")");
        _ = ReplyWindowGroupsAsync("Opened group \"" + group.Name + "\" (" + opened + " windows).", group.Name);
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
