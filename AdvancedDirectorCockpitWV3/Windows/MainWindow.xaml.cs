using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace AdvancedDirectorCockpitWV3.Windows;

public partial class MainWindow : Window
{
    private static string VirtualHost => AdcSettings.Cockpit.VirtualHost; // 2026-10-31 appsettings Cockpit:*
    private static int MaxSlots => AdcSettings.Cockpit.MaxSlots;


    private readonly Dictionary<int, SlotPane> _panes = new();
    private readonly Dictionary<int, string> _urls = new();
    private readonly object _adcSaveLock = new();

    private CoreWebView2Environment? _environment;
    private int _focused;
    private readonly EmbeddedApps _embeds;
    private bool _popupOpen;
    private string _www = "";
    private readonly string? _bootConfigId;

    public MainWindow() : this(null) { }

    // configId: open already restored to that saved config (?config=id).
    public MainWindow(string? configId)
    {
        _bootConfigId = configId;
        InitializeComponent();
        // A desktop app card dropped on a pane runs inside that pane (see EmbeddedApps.cs).
        _embeds = new EmbeddedApps(this, PaneHost, slot => _ = NotifyEmbedEndedAsync(slot));
        _embeds.Note = (slot, text) => _ = NotifyEmbedNoteAsync(slot, text); // adc-v157
        _embeds.Reveal = RevealEmbed; // adc-taskbar: a pane program activated from outside shows its tab
        Closing += (_, _) => { HandBackDetached(); _embeds.HandBackAll(); }; // 2026-10-09: apps keep running (no WM_CLOSE)
        Loaded += MainWindow_Loaded;
        SizeChanged += (_, _) => { _ = RequestSlotBoundsAsync(); };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            ShowError("The cockpit shell failed to start." + Environment.NewLine + Environment.NewLine + ex.Message);
        }
    }

    private async Task StartAsync()
    {
        var www = FindWww();
        if (www == null)
        {
            ShowError(MissingWwwMessage());
            return;
        }

        _www = www;
        if (!CfdSql.Connected)
        {
            ShowError("The cockpit board lives in the CFD database (Board* tables) and the database is not available."
                + Environment.NewLine + Environment.NewLine + CfdSql.Status);
            return;
        }

        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdvancedDirectorCockpitWV3",
            "WebView2");
        Directory.CreateDirectory(userData);

        // One shared environment/profile so Open group windows see the same lp-configs.
        _environment = await EnsureSharedEnvironmentAsync(userData);
        await ShellView.EnsureCoreWebView2Async(_environment);
        if (ShellView.CoreWebView2 == null)
            throw new InvalidOperationException("WebView2 did not start.");

        AttachShell(ShellView.CoreWebView2);
        // ServeLocal: GET www + canonical data (no-store), POST /adc-data. Origin stays adc-wv2.local.
        ShellView.CoreWebView2.AddWebResourceRequestedFilter(
            "https://" + VirtualHost + "/*",
            CoreWebView2WebResourceContext.All);
        ShellView.CoreWebView2.WebResourceRequested += ServeLocal;
        StartFreshnessWatch();
        // A fresh ?v= tag on every start keeps WebView2 from serving a cached copy of the page.
        // The page itself adds a fresh tag to its CSS and JS, and reads the board from adc-board/* (SQL) with no-store.
        // 2026-10-09 multi-window: per-window page storage for "New window" windows (MainWindow.Windows.cs).
        await ShellView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(WindowStorageScript());
        await ShellView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(AdcVersion.PageScript()); // host version for the page label
        ShellView.CoreWebView2.Navigate(BuildStartUrl());
    }

    private void AttachShell(CoreWebView2 core)
    {
        InitTopCollapseHotkey(); // adc-topcol: Ctrl+Alt+T while the cockpit is in front
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += (_, args) => OnShellMessage(args);
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
                _ = RequestSlotBoundsAsync();
        };
        core.NavigationStarting += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Uri)) return;
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
            var http = uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
            if (!http) return;
            if (uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase)) return;
            args.Cancel = true;
            if (IsDirectHttp(args.Uri))
                RememberUrl(_focused, args.Uri);
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            var uri = args.Uri ?? "";
            if (IsDirectHttp(uri))
                RememberUrl(_focused, uri);
        };
    }

    private void OnShellMessage(CoreWebView2WebMessageReceivedEventArgs args)
    {
        string json;
        try { json = args.WebMessageAsJson; }
        catch { return; }
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyShellMessage(json));
            return;
        }
        ApplyShellMessage(json);
    }

    private void ApplyShellMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();
            if (type == "adc-open-pane")
                ApplyOpen(root);
            else if (type == "adc-slot-bounds")
                ApplyBounds(root);
            else if (type == "adc-launch")
                ApplyLaunch(root);
            else if (type == "adc-embed-app")
                ApplyEmbed(root);
            else if (type == "adc-modal")
                ApplyModal(root);
            else if (type == "adc-open-config-windows")
                ApplyOpenConfigWindows(root);
            else if (type == "adc-move-pane")
                ApplyMovePane(root);
            else if (type == "adc-space-close")
                ApplySpaceClose(root);
            else if (type == "adc-reload-pane")
                ApplyReloadPane(root);
            else if (type == "adc-detach-space")
                ApplyDetachSpace(root);
            else if (type == "adc-vault-fill")
                ApplyVaultFill(root);
            else if (type == "adc-new-window")
                ApplyNewWindow(); // MainWindow.Windows.cs
            else if (type == "adc-window-groups-list")
                ApplyWindowGroupsList(); // MainWindow.WindowGroups.cs
            else if (type == "adc-window-group-save")
                ApplyWindowGroupSave(root);
            else if (type == "adc-window-group-open")
                ApplyWindowGroupOpen(root);
        }
        catch
        {
            // A bad page message must not take the shell down.
        }
    }

    // The page's drop handler calls this with the slot that received the drop.
    // Slot 0 is the left stage slot. This stores the url for that index only.
    private void ApplyOpen(JsonElement root)
    {
        var slot = ReadKey(root);
        if (slot < 0) return;
        var url = ReadString(root, "url");
        if (!IsDirectHttp(url))
            return;
        RememberUrl(slot, url);
        if (_panes.TryGetValue(slot, out var pane) && pane.Wanted)
            _ = NavigateWhenReady(pane, url);
    }

    // Moves each WebView2 onto the rectangle of the stage slot with the same index.
    // A drop on the left slot therefore navigates the webview covering that slot,
    // not a pane that sits further right.
    // Moves each WebView2 onto the rectangle of the stage slot with the same index.
    // A drop on the left slot navigates the webview covering that slot, not a pane further right.
    private void ApplyBounds(JsonElement root)
    {
        if (!root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
            return;

        // 2026-10-09 tabs: the bounds are for one tab ("space"). Panes and programs of other tabs are
        // hidden and kept (switching back shows them as they were); only this tab's unused slots are released.
        var space = ReadSpace(root);
        _curSpace = space;
        var spaceId = AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceId(space);
        var vw = ReadDouble(root, "vw");
        var vh = ReadDouble(root, "vh");
        var origin = ShellOrigin();
        var scaleX = ScaleFor(vw, ShellView.ActualWidth);
        var scaleY = ScaleFor(vh, ShellView.ActualHeight);
        var mentioned = new HashSet<int>();

        foreach (var item in slots.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var raw = ReadSlot(item);
            if (raw < 0 || raw >= MaxSlots) continue;
            var slot = AdvancedDirectorCockpitWV3.Windows.MainWindow.Key(space, raw);
            mentioned.Add(slot);
            var filled = item.TryGetProperty("filled", out var filledEl) && filledEl.ValueKind == JsonValueKind.True;
            var x = ReadDouble(item, "x");
            var y = ReadDouble(item, "y");
            var w = ReadDouble(item, "w");
            var h = ReadDouble(item, "h");
            var url = ReadString(item, "url");
            if (string.IsNullOrWhiteSpace(url) && _urls.TryGetValue(slot, out var pending))
                url = pending;
            var name = ReadString(item, "name");

            // A pane that holds an embedded desktop app: keep the app window on the pane rectangle.
            // Anything else in that slot now gives the window back as a normal window.
            if (_embeds.Has(slot))
            {
                if (filled && _embeds.Shows(slot, url))
                {
                    _embeds.Place(slot, new Rect(origin.X + (x * scaleX), origin.Y + (y * scaleY), w * scaleX, h * scaleY), true);
                    _urls.Remove(slot);
                    if (_panes.TryGetValue(slot, out var under))
                        HidePane(under, blank: true);
                    continue;
                }
                _embeds.Release(slot);
            }

            if (!filled || !IsDirectHttp(url))
            {
                _urls.Remove(slot);
                if (_panes.TryGetValue(slot, out var closed))
                    HidePane(closed, blank: true);
                continue;
            }

            if (w < 8 || h < 8)
            {
                if (_panes.TryGetValue(slot, out var tiny))
                    HidePane(tiny, blank: false);
                continue;
            }

            _urls[slot] = url;
            _focused = slot;
            var pane = EnsurePane(slot);
            pane.Wanted = true;
            Canvas.SetLeft(pane.View, origin.X + (x * scaleX));
            Canvas.SetTop(pane.View, origin.Y + (y * scaleY));
            pane.View.Width = Math.Max(1, w * scaleX);
            pane.View.Height = Math.Max(1, h * scaleY);
            var coveredNow = PopupCovers(pane); // adc-v156: only a popup that covers this pane hides it
            pane.View.Visibility = coveredNow ? Visibility.Hidden : Visibility.Visible;
            if (coveredNow) _popupHidden.Add(pane.Key); else _popupHidden.Remove(pane.Key);
            if (!coveredNow)
                BringPaneToFront(pane);
            AutomationProperties.SetAutomationId(pane.View, "StageSlot" + raw);
            AutomationProperties.SetName(pane.View, string.IsNullOrWhiteSpace(name) ? "Stage slot " + raw : name);
            _ = NavigateWhenReady(pane, url);
        }

        var keep = new HashSet<int>(mentioned);
        foreach (var pane in _panes.Values)
        {
            if (mentioned.Contains(pane.Key)) continue;
            if (AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceOfKey(pane.Key) == spaceId)
                HidePane(pane, blank: true);
            else
                HidePane(pane, blank: false); // another tab: hidden, page kept
        }
        foreach (var key in _embeds.Keys())
        {
            if (AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceOfKey(key) != spaceId) keep.Add(key);
        }
        _embeds.HideWhere(k => AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceOfKey(k) != spaceId);
        _embeds.ReleaseExcept(keep);
    }
    // Page popups (dialogs, dropdown menus) are HTML inside the cockpit view. Panes and embedded apps are
    // native windows that paint over that view, so a pane under a popup must be hidden for the popup to show.
    // adc-v156: only the panes a popup actually covers are hidden (menus send their rectangles; a dialog backdrop
    // or a drag still covers everything). Before a pane is hidden, a snapshot of it (CapturePreviewAsync) is sent
    // to the page, which shows it in the pane's place, so the page does not go blank while a menu is open.
    // Hidden, not closed: pages, embeds, and app state are kept.
    private List<Rect>? _popupRects; // shell coordinates; null = the whole page
    private readonly HashSet<int> _popupHidden = new();
    private int _modalGen;
    private string _modalKey = "";

    private void ApplyModal(JsonElement root)
    {
        var open = root.TryGetProperty("open", out var openEl) && openEl.ValueKind == JsonValueKind.True;
        var hasRects = root.TryGetProperty("rects", out var rectsEl) && rectsEl.ValueKind == JsonValueKind.Array;
        var full = !hasRects || (root.TryGetProperty("full", out var fullEl) && fullEl.ValueKind == JsonValueKind.True);
        List<Rect>? rects = null;
        var key = open ? "full" : "closed";
        if (open && !full)
        {
            var origin = ShellOrigin();
            var sx = ScaleFor(ReadDouble(root, "vw"), ShellView.ActualWidth);
            var sy = ScaleFor(ReadDouble(root, "vh"), ShellView.ActualHeight);
            rects = new List<Rect>();
            var sb = new StringBuilder("r");
            foreach (var r in rectsEl.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var w = ReadDouble(r, "w");
                var h = ReadDouble(r, "h");
                if (w <= 0 || h <= 0) continue;
                var rc = new Rect(origin.X + (ReadDouble(r, "x") * sx), origin.Y + (ReadDouble(r, "y") * sy), w * sx, h * sy);
                rc.Inflate(6, 6);
                rects.Add(rc);
                sb.Append(';').Append(rc.ToString());
            }
            key = sb.ToString();
        }
        if (open == _popupOpen && key == _modalKey) return;
        _popupOpen = open;
        _popupRects = rects;
        _modalKey = key;
        var gen = ++_modalGen;
        foreach (var pane in _panes.Values)
        {
            if (!pane.Wanted) continue;
            if (!open)
            {
                // Closing: restore visibility, BringPaneToFront, and a size nudge so WebView2 paints again
                // instead of staying black.
                pane.View.Visibility = Visibility.Visible;
                BringPaneToFront(pane);
                NudgePane(pane);
                continue;
            }
            if (PopupCovers(pane))
            {
                if (pane.View.Visibility == Visibility.Visible) _ = SnapThenHideAsync(pane, gen);
            }
            else if (_popupHidden.Remove(pane.Key))
            {
                pane.View.Visibility = Visibility.Visible;
                BringPaneToFront(pane);
                NudgePane(pane);
            }
        }
        _embeds.Suspend(open, open && _popupRects != null ? PopupHits : null);
        if (!open)
        {
            _popupHidden.Clear();
            PostShell("{\"type\":\"adc-pane-snap-clear\"}");
            _ = RequestSlotBoundsAsync();
        }
    }

    private static void NudgePane(SlotPane pane)
    {
        var w = pane.View.Width;
        if (w > 1)
        {
            pane.View.Width = w - 1;
            pane.View.Width = w;
        }
    }

    // adc-v156: true when an open page popup covers this rectangle (shell coordinates).
    private bool PopupHits(Rect r)
    {
        if (!_popupOpen) return false;
        if (_popupRects == null) return true;
        foreach (var rc in _popupRects)
            if (rc.IntersectsWith(r)) return true;
        return false;
    }

    private bool PopupCovers(SlotPane pane)
    {
        if (!_popupOpen) return false;
        if (_popupRects == null) return true;
        var x = Canvas.GetLeft(pane.View);
        var y = Canvas.GetTop(pane.View);
        if (double.IsNaN(x) || double.IsNaN(y)) return true;
        return PopupHits(new Rect(x, y, Math.Max(1, pane.View.Width), Math.Max(1, pane.View.Height)));
    }

    // adc-v156: snapshot first (the page shows it in the pane's place), then hide the pane.
    private async Task SnapThenHideAsync(SlotPane pane, int gen)
    {
        _popupHidden.Add(pane.Key);
        string? src = null;
        try
        {
            var core = pane.View.CoreWebView2;
            if (core != null)
            {
                var ms = new MemoryStream();
                var cap = core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, ms);
                if (await Task.WhenAny(cap, Task.Delay(500)) == cap)
                {
                    await cap;
                    src = "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                    ms.Dispose();
                }
            }
        }
        catch
        {
            src = null;
        }
        if (!_popupOpen || !PopupCovers(pane))
        {
            if (!_popupOpen) _popupHidden.Remove(pane.Key);
            return;
        }
        if (src != null)
            PostShell(JsonSerializer.Serialize(new { type = "adc-pane-snap", slot = pane.Index, gen, src }));
        pane.View.Visibility = Visibility.Hidden;
    }

    private void PostShell(string json)
    {
        try { ShellView.CoreWebView2?.PostWebMessageAsJson(json); }
        catch { }
    }

    private void RememberUrl(int slot, string url)
    {
        if (slot < 0) return;
        if (!IsDirectHttp(url)) return;
        _urls[slot] = url;
        _focused = slot;
    }

    private SlotPane EnsurePane(int slot)
    {
        if (_panes.TryGetValue(slot, out var existing))
            return existing;

        var view = new WebView2
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 1,
            Height = 1
        };
        Panel.SetZIndex(view, 20 + AdvancedDirectorCockpitWV3.Windows.MainWindow.SlotOfKey(slot));
        PaneHost.Children.Add(view);
        var pane = new SlotPane(AdvancedDirectorCockpitWV3.Windows.MainWindow.SlotOfKey(slot), view) { Key = slot };
        _panes[slot] = pane;
        pane.Ready = InitPaneAsync(pane);
        return pane;
    }

    private async Task InitPaneAsync(SlotPane pane)
    {
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (_environment == null)
                return;
            await pane.View.EnsureCoreWebView2Async(_environment);
            if (pane.View.CoreWebView2 == null)
                return;
            pane.View.CoreWebView2.Settings.IsWebMessageEnabled = true;
            AttachPopups(pane.View.CoreWebView2);
            AttachVault(pane);
            if (!pane.Wanted)
                pane.View.Visibility = Visibility.Collapsed;
            else if (PopupCovers(pane)) // adc-v156
            {
                pane.View.Visibility = Visibility.Hidden;
                _popupHidden.Add(pane.Key);
            }
            else
                BringPaneToFront(pane);
        }
        catch
        {
            pane.Wanted = false;
            pane.View.Visibility = Visibility.Collapsed;
        }
    }

    private async Task NavigateWhenReady(SlotPane pane, string url)
    {
        try
        {
            await pane.Ready;
        }
        catch
        {
            return;
        }
        if (!pane.Wanted || pane.View.CoreWebView2 == null || !IsDirectHttp(url))
            return;
        if (string.Equals(pane.Navigated, url, StringComparison.OrdinalIgnoreCase))
            return;
        pane.Navigated = url;
        // r7.1: per-card browser identity (BrowserIdentity.cs), set before the card URL loads.
        await ApplyPaneIdentityAsync(pane.View.CoreWebView2, url);
        try
        {
            pane.View.CoreWebView2.Navigate(url);
        }
        catch
        {
            pane.Navigated = null;
        }
    }

    private async Task ApplyPaneIdentityAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core, string url)
    {
        string? cards = null;
        try { cards = CfdSql.Board.GetDocumentJson("cards"); } catch { }
        await BrowserIdentity.ApplyAsync(core, BrowserIdentity.ForUrl(cards, url), _environment?.BrowserVersionString ?? "");
    }

    // A pane's WebView2 is a child HWND. A newer HWND can sit under the full-window cockpit view,
    // so the pane loaded but could not be seen or clicked. This puts the pane's HWND on top.
    private static void BringPaneToFront(SlotPane pane)
    {
        try
        {
            var hwnd = ((HwndHost)pane.View).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0013); // HWND_TOP, NOSIZE | NOMOVE | NOACTIVATE
        }
        catch
        {
            // The pane stays where it is.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private void HidePane(SlotPane pane, bool blank)
    {
        var wasWanted = pane.Wanted;
        pane.Wanted = false;
        pane.View.Visibility = Visibility.Collapsed;
        if (!blank)
            return;
        if (!wasWanted && string.IsNullOrEmpty(pane.Navigated))
            return;
        pane.Navigated = null;
        try
        {
            var core = pane.View.CoreWebView2;
            if (core == null) return;
            var src = core.Source ?? "";
            if (src.Length > 0 && !src.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
                core.Navigate("about:blank");
        }
        catch
        {
            // The pane is already hidden.
        }
    }

    private async Task RequestSlotBoundsAsync()
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        try
        {
            await core.ExecuteScriptAsync("window.__adcReportSlots && window.__adcReportSlots()");
        }
        catch
        {
            // The page also reports after its own render and resize.
        }
    }

    private Point ShellOrigin()
    {
        try
        {
            return ShellView.TranslatePoint(new Point(0, 0), PaneHost);
        }
        catch
        {
            return new Point(0, 0);
        }
    }

    private static double ScaleFor(double viewport, double actual)
    {
        if (viewport < 1 || actual < 1) return 1;
        var scale = actual / viewport;
        if (scale < 0.5 || scale > 4) return 1;
        if (Math.Abs(scale - 1) < 0.02) return 1;
        return scale;
    }

    private static int ReadSlot(JsonElement el)
    {
        if (!el.TryGetProperty("slot", out var slotEl)) return 0;
        if (slotEl.ValueKind == JsonValueKind.Number && slotEl.TryGetInt32(out var n)) return n;
        return 0;
    }

    private static string ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    private static double ReadDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) ? n : 0;
    }

    private void ShowError(string message)
    {
        foreach (var pane in _panes.Values)
            HidePane(pane, blank: false);
        ShellError.Text = message;
        ShellError.Visibility = Visibility.Visible;
        ShellView.Visibility = Visibility.Collapsed;
        PaneHost.Visibility = Visibility.Collapsed;
    }

    // Serves https://adc-wv2.local/* without SetVirtualHostNameToFolderMapping so POST /adc-data works.
    private void ServeLocal(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_environment == null) return;
        if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri)) return;
        if (!uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase)) return;
        const string noStore = "Cache-Control: no-store, max-age=0\r\nPragma: no-cache";
        var pathOnly = uri.AbsolutePath.TrimEnd('/');
        if (uri.AbsolutePath.StartsWith("/adc-vault/", StringComparison.OrdinalIgnoreCase))
        {
            ServeVault(args, uri);
            return;
        }
        if (string.Equals(args.Request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            && string.Equals(pathOnly, "/adc-data", StringComparison.OrdinalIgnoreCase))
        {
            ApplyAdcSave(args, noStore);
            return;
        }
        if (string.Equals(args.Request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            && string.Equals(pathOnly, "/adc-icon", StringComparison.OrdinalIgnoreCase))
        {
            ServeIconUpload(args, noStore); // MainWindow.Icons.cs (adc-iconpick)
            return;
        }
        if (!string.Equals(args.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            args.Response = _environment.CreateWebResourceResponse(null, 405, "Method Not Allowed", noStore);
            return;
        }
        if (string.Equals(pathOnly, "/adc-favicon", StringComparison.OrdinalIgnoreCase))
        {
            ServeFavicon(args, uri, noStore); // MainWindow.Favicons.cs
            return;
        }
        var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (rel.Length == 0) rel = "advanced-director-cockpit.html";
        if (rel.StartsWith("adc-board/", StringComparison.OrdinalIgnoreCase))
        {
            ServeBoardDocument(args, rel.Substring(10), noStore); // 2026-11-03 cards/groups/tabs/stages from SQL only
            return;
        }
        if (rel.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            ServeBoardData(args, rel.Substring(5), noStore); // card icons only (BoardIcon rows); never a file
            return;
        }
        var path = SafeChild(_www, rel);
        if (path == null || !File.Exists(path))
        {
            args.Response = _environment.CreateWebResourceResponse(null, 404, "Not Found", noStore);
            return;
        }
        byte[] bytes;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }
        catch
        {
            args.Response = _environment.CreateWebResourceResponse(null, 503, "Busy", noStore);
            return;
        }
        args.Response = _environment.CreateWebResourceResponse(
            new MemoryStream(bytes), 200, "OK", "Content-Type: " + ContentTypeFor(path) + "\r\n" + noStore);
    }

    private void ApplyAdcSave(CoreWebView2WebResourceRequestedEventArgs args, string noStore)
    {
        try
        {
            string bodyText;
            using (var stream = args.Request.Content)
            {
                if (stream == null)
                {
                    args.Response = _environment!.CreateWebResourceResponse(null, 400, "Bad Request", noStore);
                    return;
                }
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
                bodyText = reader.ReadToEnd();
            }
            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                args.Response = _environment!.CreateWebResourceResponse(null, 400, "Bad Request", noStore);
                return;
            }
            lock (_adcSaveLock)
            {
                CfdSql.Board.SavePage(root); // 2026-11-02 board in SQL (Board* tables), no JSON files
            }
            var ok = Encoding.UTF8.GetBytes("{\"ok\":true}");
            args.Response = _environment!.CreateWebResourceResponse(
                new MemoryStream(ok), 200, "OK", "Content-Type: application/json; charset=utf-8\r\n" + noStore);
        }
        catch
        {
            args.Response = _environment!.CreateWebResourceResponse(null, 500, "Save Failed", noStore);
        }
    }

    private static string? SafeChild(string root, string rel)
    {
        if (string.IsNullOrEmpty(root)) return null;
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(top, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch { return null; }
    }

    private static string ContentTypeFor(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".html": case ".htm": return "text/html; charset=utf-8";
            case ".js": return "text/javascript; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".json": return "application/json; charset=utf-8";
            case ".svg": return "image/svg+xml";
            case ".png": return "image/png";
            case ".jpg": case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".ico": return "image/x-icon";
            case ".woff2": return "font/woff2";
            default: return "application/octet-stream";
        }
    }


    private static string MissingWwwMessage()
    {
        return "Could not find the copied cockpit page." + Environment.NewLine + Environment.NewLine
            + "Expected www\\advanced-director-cockpit.html, .css, and .js next to AdvancedDirectorCockpitWV3.csproj."
            + Environment.NewLine + Environment.NewLine
            + "Looked upward from " + AppContext.BaseDirectory;
    }

    private static string? FindWww()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            // 2026-10-31 WV3 layered solution: the page lives in AdvancedDirectorCockpitWV3.UI\www next to the .sln.
            var uiWww = Path.Combine(dir.FullName, "AdvancedDirectorCockpitWV3.UI", "www");
            if (File.Exists(Path.Combine(dir.FullName, "AdvancedDirectorCockpitWV3.sln")) && IsWww(uiWww))
                return uiWww;
            var candidate = Path.Combine(dir.FullName, "www");
            var project = Path.Combine(dir.FullName, "AdvancedDirectorCockpitWV3.csproj");
            if (File.Exists(project) && IsWww(candidate))
                return candidate;
            dir = dir.Parent;
        }
        var beside = Path.Combine(AppContext.BaseDirectory, "www");
        if (IsWww(beside)) return beside;
        // 2026-10-09: a copy of the exe anywhere else (test build, moved exe) falls back to the Pub page.
        // 2026-11-04: unset PubExePath skips this. An empty path must not walk the process working directory.
        var pubFolder = Path.GetDirectoryName(PubExePath);
        if (string.IsNullOrEmpty(pubFolder)) return null;
        var pubDir = new DirectoryInfo(pubFolder);
        while (pubDir != null)
        {
            var candidate = Path.Combine(pubDir.FullName, "www");
            if (File.Exists(Path.Combine(pubDir.FullName, "AdvancedDirectorCockpitWV3.csproj")) && IsWww(candidate))
                return candidate;
            pubDir = pubDir.Parent;
        }
        return null;
    }

    private static bool IsWww(string dir)
    {
        return File.Exists(Path.Combine(dir, "advanced-director-cockpit.html"))
            && File.Exists(Path.Combine(dir, "advanced-director-cockpit.css"))
            && File.Exists(Path.Combine(dir, "advanced-director-cockpit.js"));
    }

    private static bool IsDirectHttp(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (uri.AbsolutePath.StartsWith("/frame/", StringComparison.OrdinalIgnoreCase)) return false;
        if (uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // ---- Desktop app cards (Bambu Studio) ----
    // A tray click on a card whose url is a local .exe or a desktop app link posts adc-launch.
    // The program starts in its own window. A pane is a web view and cannot show a Windows program.
    // Bambu Studio starts from bambu-studio.exe first; bambustudio://open is only the fallback.
    private const string BambuExeName = "bambu-studio.exe";
    private const string BambuScheme = "bambustudio";

    private static readonly HashSet<string> BlockedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "file", "ftp", "javascript", "vbscript", "data", "about", "blob",
        "ms-msdt", "search-ms", "search", "ms-officecmd", "mk", "res", "shell"
    };

    private void ApplyLaunch(JsonElement root)
    {
        if (!IsShellPage()) return;
        var target = ReadString(root, "url").Trim().Trim('"').Trim();
        if (target.Length == 0) return;

        var scheme = CustomScheme(target);
        var exe = LocalExe(target);
        var isBambu = string.Equals(scheme, BambuScheme, StringComparison.OrdinalIgnoreCase)
            || string.Equals(SafeFileName(target), BambuExeName, StringComparison.OrdinalIgnoreCase);
        if (exe == null && isBambu)
            exe = FindBambuExe();

        if (exe != null && TryStart(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            }))
            return;

        // Fallback: hand a registered app link to Windows.
        var link = scheme != null ? target : (isBambu ? BambuScheme + "://open" : null);
        var linkScheme = link == null ? null : CustomScheme(link);
        if (link != null && linkScheme != null && IsRegisteredScheme(linkScheme))
            TryStart(new ProcessStartInfo(link) { UseShellExecute = true });
    }

    // A pane drop of a desktop app card posts adc-embed-app. The program starts and its main
    // window is reparented into that pane. A tray click still opens it in its own window.
    private void ApplyEmbed(JsonElement root)
    {
        if (!IsShellPage()) return;
        var slot = ReadKey(root);
        if (slot < 0) return;
        var target = ReadString(root, "url").Trim().Trim('"').Trim();
        var exe = ResolveLaunchExe(target);
        if (exe == null) return;
        _embeds.Embed(slot, target, exe, AdvancedDirectorCockpitWV3.Windows.MainWindow.EmbedArgs(exe, ReadString(root, "args"), ReadString(root, "profile"))); // MainWindow.ExeArgs.cs
    }

    // The exe behind a desktop app card: a local .exe, Bambu Studio's install, or a registered link's open command.
    private static string? ResolveLaunchExe(string target)
    {
        if (target.Length == 0) return null;
        var scheme = CustomScheme(target);
        var exe = LocalExe(target);
        var isBambu = string.Equals(scheme, BambuScheme, StringComparison.OrdinalIgnoreCase)
            || string.Equals(SafeFileName(target), BambuExeName, StringComparison.OrdinalIgnoreCase);
        if (exe == null && isBambu)
            exe = FindBambuExe();
        if (exe == null && scheme != null && IsRegisteredScheme(scheme))
        {
            var command = ExeFromCommand(RegistryText(Registry.ClassesRoot, scheme + @"\shell\open\command", null));
            if (command != null && File.Exists(command)) exe = command;
        }
        return exe;
    }

    // adc-v157: a line of text in a desktop-app pane (e.g. "open it from its tray icon").
    private async Task NotifyEmbedNoteAsync(int slot, string text)
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        try
        {
            await core.ExecuteScriptAsync("window.__adcEmbedNote && window.__adcEmbedNote(" + AdvancedDirectorCockpitWV3.Windows.MainWindow.SlotOfKey(slot) + "," + JsonSerializer.Serialize(AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceName(AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceOfKey(slot))) + "," + JsonSerializer.Serialize(text) + ")");
        }
        catch
        {
            // The pane keeps its "Starting ..." line.
        }
    }

    private async Task NotifyEmbedEndedAsync(int slot)
    {
        var core = ShellView.CoreWebView2;
        if (core == null) return;
        try
        {
            await core.ExecuteScriptAsync("window.__adcEmbedEnded && window.__adcEmbedEnded(" + AdvancedDirectorCockpitWV3.Windows.MainWindow.SlotOfKey(slot) + "," + JsonSerializer.Serialize(AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceName(AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceOfKey(slot))) + ")");
        }
        catch
        {
            // The pane stays until the next render.
        }
    }

    // adc-taskbar (2026-10-11): a program in a pane was activated from outside while its pane was not on screen
    // (a link opened in the embedded Chrome, a notification): bring this cockpit window forward on the tab that holds it.
    private void RevealEmbed(int slot)
    {
        try
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            _ = ShellView.CoreWebView2?.ExecuteScriptAsync("window.__activateView && window.__activateView(" + JsonSerializer.Serialize(AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceName(AdvancedDirectorCockpitWV3.Windows.MainWindow.SpaceOfKey(slot))) + ")");
        }
        catch
        {
            // The program stays in its pane either way.
        }
    }

    private bool IsShellPage()
    {
        var src = ShellView.CoreWebView2?.Source ?? "";
        return Uri.TryCreate(src, UriKind.Absolute, out var u)
            && u.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryStart(ProcessStartInfo info)
    {
        try
        {
            using var started = Process.Start(info);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeFileName(string target)
    {
        try { return Path.GetFileName(target); }
        catch { return ""; }
    }

    // A local path to an existing .exe, or null. Accepts C:\...\app.exe and file:///C:/.../app.exe.
    private static string? LocalExe(string target)
    {
        try
        {
            var path = target;
            if (target.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(target, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
                path = fileUri.LocalPath;
            path = Environment.ExpandEnvironmentVariables(path);
            if (!Path.IsPathFullyQualified(path)) return null;
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    // The scheme of a desktop app link such as bambustudio://open, or null for web, file, and script links.
    private static string? CustomScheme(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)) return null;
        if (uri.IsFile || uri.IsUnc) return null;
        var scheme = uri.Scheme;
        if (scheme.Length < 2 || BlockedSchemes.Contains(scheme)) return null;
        return scheme;
    }

    private static bool IsRegisteredScheme(string scheme)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(scheme);
            return key?.GetValue("URL Protocol") != null;
        }
        catch
        {
            return false;
        }
    }

    // Finds bambu-studio.exe: App Paths, the uninstall entry, Program Files, then the protocol's open command.
    private static string? FindBambuExe()
    {
        var candidates = new List<string?>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            candidates.Add(RegistryText(hive, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + BambuExeName, null));
            candidates.Add(RegistryText(hive, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Bambu Studio", "DisplayIcon"));
            candidates.Add(RegistryText(hive, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Bambu Studio", "DisplayIcon"));
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bambu Studio", BambuExeName));
        candidates.Add(RegistryText(Registry.ClassesRoot, BambuScheme + @"\shell\open\command", null));

        foreach (var raw in candidates)
        {
            var exe = ExeFromCommand(raw);
            if (exe == null) continue;
            if (!string.Equals(Path.GetFileName(exe), BambuExeName, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    private static string? RegistryText(RegistryKey hive, string subKey, string? name)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    // The exe from a command line or icon value: "C:\x\app.exe" "%1", C:\x\app.exe,0, or a plain path.
    private static string? ExeFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        if (text.StartsWith("\""))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text.Substring(1, end - 1) : null;
        }
        var at = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return at > 0 ? text.Substring(0, at + 4) : null;
    }

    // ---- Pop-up windows ----
    // A site in a pane can open a pop-up (sign-in, target=_blank). Each one opens in a resizable
    // host window sized to most of the work area of the monitor the cockpit is on, centered there.
    private void AttachPopups(CoreWebView2 core)
    {
        core.NewWindowRequested += OnPaneNewWindow;
    }

    private async void OnPaneNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (_environment == null) return;
        // adc-v156: a link the user clicked that asks for a new tab/window (target=_blank, or window.open with no
        // size or position) opens in the SAME pane, so it stays in the cockpit with the same profile and cookies
        // (Back returns to the first site). Sized pop-ups (sign-in windows that talk to their opener) and pop-ups
        // the user did not ask for keep the pop-up window below. Non-web addresses (about:blank etc.) also keep it.
        try
        {
            if (sender is CoreWebView2 opener && args.IsUserInitiated && IsDirectHttp(args.Uri ?? ""))
            {
                var feat = args.WindowFeatures;
                if (feat == null || (!feat.HasSize && !feat.HasPosition))
                {
                    args.Handled = true;
                    opener.Navigate(args.Uri);
                    return;
                }
            }
        }
        catch
        {
            // Falls through to the pop-up window.
        }
        var deferral = args.GetDeferral();
        Window? popup = null;
        try
        {
            var view = new WebView2();
            popup = CreatePopupWindow(view);
            var owned = popup;
            EventHandler closeWithShell = (_, _) => owned.Close();
            Closed += closeWithShell;
            popup.Closed += (_, _) =>
            {
                Closed -= closeWithShell;
                view.Dispose();
            };
            popup.Show();
            await view.EnsureCoreWebView2Async(_environment);
            var core = view.CoreWebView2;
            if (core == null)
            {
                popup.Close();
                return;
            }
            AttachPopups(core);
            var shown = popup;
            core.DocumentTitleChanged += (_, _) =>
                shown.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Advanced Director Cockpit WV3" : core.DocumentTitle;
            core.WindowCloseRequested += (_, _) => shown.Close();
            args.NewWindow = core;
            args.Handled = true;
        }
        catch
        {
            // WebView2 falls back to its own pop-up.
            try { popup?.Close(); } catch { }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private Window CreatePopupWindow(WebView2 view)
    {
        var work = OwnerWorkArea();
        var width = Math.Min(work.Width, Math.Max(640, Math.Round(work.Width * 0.75)));
        var height = Math.Min(work.Height, Math.Max(480, Math.Round(work.Height * 0.8)));
        return new Window
        {
            Title = "Advanced Director Cockpit WV3",
            Content = view,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = true,
            MinWidth = 480,
            MinHeight = 360,
            Width = width,
            Height = height,
            Left = work.Left + ((work.Width - width) / 2),
            Top = work.Top + ((work.Height - height) / 2),
            Background = Background
        };
    }

    // Work area of the monitor this window is on, in WPF units.
    private Rect OwnerWorkArea()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var toDips = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                    ?? System.Windows.Media.Matrix.Identity;
                var topLeft = toDips.Transform(new Point(info.Work.Left, info.Work.Top));
                var bottomRight = toDips.Transform(new Point(info.Work.Right, info.Work.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }
        catch
        {
            // Primary work area below.
        }
        return SystemParameters.WorkArea;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private sealed class SlotPane    {
        public SlotPane(int index, WebView2 view)
        {
            Index = index;
            View = view;
        }

        public int Index { get; set; }
        public int Key { get; set; }
        public WebView2 View { get; }
        public Task Ready { get; set; } = Task.CompletedTask;
        public bool Wanted { get; set; }
        public string? Navigated { get; set; }
    }
}
