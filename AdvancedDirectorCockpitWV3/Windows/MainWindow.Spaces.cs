using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-10-09 tabs: every page tab is a "space". Panes and embedded apps are keyed by (space, slot),
// so a tab that is not shown keeps its web pages and its programs (hidden, not released).
// A space can be moved to its own window (Detach); closing that window gives it back to the main one.
public partial class MainWindow
{
    private static readonly Dictionary<string, int> SpaceIds = new(StringComparer.Ordinal) { ["launch"] = 0 };
    private string _curSpace = "launch";
    private string? _soloSpace;
    private Windows.MainWindow? _detachedFrom;
    private readonly Dictionary<string, Windows.MainWindow> _detached = new(StringComparer.Ordinal);
    private bool _isClosing;

    // Detached tab window: same page in solo mode (?solo=<tab>).
    private MainWindow(string? configId, string soloSpace, Windows.MainWindow from) : this(configId)
    {
        _soloSpace = soloSpace;
        _curSpace = soloSpace;
        _detachedFrom = from;
    }

    private static int SpaceId(string space)
    {
        lock (SpaceIds)
        {
            if (!SpaceIds.TryGetValue(space, out var id))
            {
                id = SpaceIds.Count;
                SpaceIds[space] = id;
            }
            return id;
        }
    }

    private static string SpaceName(int id)
    {
        lock (SpaceIds)
        {
            foreach (var kv in SpaceIds)
                if (kv.Value == id) return kv.Key;
        }
        return "launch";
    }

    private static int SpaceOfKey(int key) => key / MaxSlots;
    private static int SlotOfKey(int key) => key % MaxSlots;
    private static int Key(string space, int slot) => SpaceId(space) * MaxSlots + slot;

    private string ReadSpace(JsonElement el)
    {
        var s = ReadString(el, "space");
        return string.IsNullOrWhiteSpace(s) ? _curSpace : s;
    }

    // Slot plus the message's tab ("space"); a message without one means the tab on screen.
    private int ReadKey(JsonElement el)
    {
        var slot = ReadSlot(el);
        if (slot < 0 || slot >= MaxSlots) return -1;
        return Key(ReadSpace(el), slot);
    }

    private static int ReadInt(JsonElement el, string name, int fallback)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        return fallback;
    }

    // Pane title-bar drag: the page swapped two slots; swap the native views and programs with them,
    // so web pages are not reloaded and programs are not restarted.
    private void ApplyMovePane(JsonElement root)
    {
        var space = ReadSpace(root);
        var from = ReadInt(root, "from", -1);
        var to = ReadInt(root, "to", -1);
        if (from < 0 || to < 0 || from >= MaxSlots || to >= MaxSlots || from == to) return;
        var a = Key(space, from);
        var b = Key(space, to);
        _panes.Remove(a, out var pa);
        _panes.Remove(b, out var pb);
        if (pa != null) { pa.Key = b; pa.Index = to; Panel.SetZIndex(pa.View, 20 + to); _panes[b] = pa; }
        if (pb != null) { pb.Key = a; pb.Index = from; Panel.SetZIndex(pb.View, 20 + from); _panes[a] = pb; }
        var hasUa = _urls.Remove(a, out var ua);
        var hasUb = _urls.Remove(b, out var ub);
        if (hasUa && ua != null) _urls[b] = ua;
        if (hasUb && ub != null) _urls[a] = ub;
        _embeds.Swap(a, b);
        _ = RequestSlotBoundsAsync();
    }

    // A tab was closed: its pages are dropped and its programs closed (WM_CLOSE, never killed).
    private void ApplySpaceClose(JsonElement root)
    {
        var space = ReadString(root, "space");
        if (string.IsNullOrWhiteSpace(space)) return; // v1.5.0: no special first tab ("launch" is only the default id)
        if (_detached.TryGetValue(space, out var win))
        {
            _detached.Remove(space);
            win._detachedFrom = null;
            try { win.Close(); } catch { }
            return;
        }
        var id = SpaceId(space);
        foreach (var pane in _panes.Values.Where(p => SpaceOfKey(p.Key) == id).ToList())
        {
            _panes.Remove(pane.Key);
            _urls.Remove(pane.Key);
            try { PaneHost.Children.Remove(pane.View); pane.View.Dispose(); } catch { }
        }
        _embeds.ReleaseWhere(k => SpaceOfKey(k) == id);
    }

    // v1.5.0: pane menu > Reload. Reloads one WebView2 pane of a tab; embedded programs are left alone.
    private void ApplyReloadPane(JsonElement root)
    {
        var key = ReadKey(root);
        if (key < 0 || !_panes.TryGetValue(key, out var pane)) return;
        try { pane.View.Reload(); } catch { }
    }

    // Detach: the tab gets its own top-level window. Its WebView2 panes and embedded program windows
    // are moved (reparented), not reloaded or restarted.
    private void ApplyDetachSpace(JsonElement root)
    {
        var space = ReadString(root, "space");
        if (string.IsNullOrWhiteSpace(space) || _soloSpace != null) return;
        var label = ReadString(root, "label");
        if (_detached.TryGetValue(space, out var open))
        {
            try { open.Activate(); } catch { }
            _ = AckDetachAsync(space);
            return;
        }
        // 2026-10-09: same code path as "New window" (MainWindow.Windows.cs); remembers its own placement.
        var child = OpenCockpitWindow(new WindowSpec { Key = "solo:" + space, ConfigId = _bootConfigId, Solo = space, From = this, Title = "ADC - " + (string.IsNullOrWhiteSpace(label) ? space : label) });
        _detached[space] = child;
        MoveSpace(this, child, space);
        if (_curSpace == space) _curSpace = "launch";
        _ = AckDetachAsync(space);
    }

    private async Task AckDetachAsync(string space)
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        try
        {
            await core.ExecuteScriptAsync("window.__adcDetachAck && window.__adcDetachAck(" + JsonSerializer.Serialize(space) + ")");
        }
        catch
        {
            // The page shows the tab again after its wait.
        }
    }

    private static void MoveSpace(Windows.MainWindow from, Windows.MainWindow to, string space)
    {
        var id = SpaceId(space);
        foreach (var pane in from._panes.Values.Where(p => SpaceOfKey(p.Key) == id).ToList())
        {
            from._panes.Remove(pane.Key);
            pane.Wanted = false;
            pane.View.Visibility = Visibility.Collapsed;
            try { from.PaneHost.Children.Remove(pane.View); } catch { }
            to.PaneHost.Children.Add(pane.View);
            to._panes[pane.Key] = pane;
            if (from._urls.Remove(pane.Key, out var u) && u != null) to._urls[pane.Key] = u;
        }
        foreach (var key in from._urls.Keys.Where(k => SpaceOfKey(k) == id).ToList())
        {
            to._urls[key] = from._urls[key];
            from._urls.Remove(key);
        }
        to._embeds.Adopt(from._embeds.Take(k => SpaceOfKey(k) == id));
        _ = to.RequestSlotBoundsAsync();
    }

    // Closing a detached window re-attaches its tab to the main window (programs keep running).
    private void HandBackDetached()
    {
        _isClosing = true;
        foreach (var kv in _detached.ToList())
        {
            // Main window closing: the detached windows stay open on their own.
            kv.Value._detachedFrom = null;
        }
        _detached.Clear();
        var main = _detachedFrom;
        var space = _soloSpace;
        _detachedFrom = null;
        if (main == null || space == null || main._isClosing || !main.IsLoaded) return;
        main._detached.Remove(space);
        MoveSpace(this, main, space);
        _ = main.NotifyReattachAsync(space);
    }

    private async Task NotifyReattachAsync(string space)
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        try
        {
            await core.ExecuteScriptAsync("window.__adcReattach && window.__adcReattach(" + JsonSerializer.Serialize(space) + ")");
        }
        catch
        {
            // The tab chip comes back on the next reload.
        }
    }
}
