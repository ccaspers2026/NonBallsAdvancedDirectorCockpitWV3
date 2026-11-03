using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-10-09: per-card password vault, WV3 side.
// The vault (PIN check, AES-256-GCM, DPAPI) is the local frame proxy on 127.0.0.1:8791 (adc_vault.py).
// When a pane finishes loading, the host tells the cockpit page the pane's real URL. The page asks the
// vault for that card's login; the vault refuses unless the URL host matches the card (or its allowed hosts).
// The page hands the result back here and the host injects it only if the pane is still on that origin.
// Nothing here logs or stores a secret.
public partial class MainWindow
{
    // 2026-11-02: adcVaultFill lives in www\adc-vault-fill.js (moved verbatim from the old VaultFillJs const).
    private static string? _vaultFillJs;
    private static string VaultFillJs => _vaultFillJs ??= File.ReadAllText(Path.Combine(FindWww() ?? Path.Combine(AppContext.BaseDirectory, "www"), "adc-vault-fill.js"));

    private void AttachVault(SlotPane pane)
    {
        var core = pane.View.CoreWebView2;
        if (core == null) return;
        // v1.5.1: frames inside the pane (e.g. Chase's secure.chase.com sign-in box inside www.chase.com).
        var frames = new VaultFrames();
        frames.Attach(core);
        frames.FrameLoaded += url => _ = NotifyVaultFrameLoadedAsync(pane, url);
        _vaultFrames.AddOrUpdate(core, frames);
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
                _ = NotifyVaultPaneLoadedAsync(pane, false);
        };
        // Ctrl+Shift+L inside a pane: fill this pane's saved login now (same vault and origin checks).
        pane.View.PreviewKeyDown += (_, e) =>
        {
            var mods = System.Windows.Input.Keyboard.Modifiers;
            if (e.Key == System.Windows.Input.Key.L
                && mods == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
            {
                e.Handled = true;
                _ = NotifyVaultPaneLoadedAsync(pane, true);
            }
        };
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2, VaultFrames> _vaultFrames = new();

    private VaultFrames? FramesOf(SlotPane pane)
    {
        var core = pane.View.CoreWebView2;
        return core != null && _vaultFrames.TryGetValue(core, out var f) ? f : null;
    }

    // A frame inside a pane finished loading: the page asks the vault whether that frame's origin is approved
    // for the pane's card and, if so, hands the login back for that frame only.
    private async Task NotifyVaultFrameLoadedAsync(SlotPane pane, string url)
    {
        try
        {
            var shell = ShellView.CoreWebView2;
            if (shell == null || !pane.Wanted || VaultFrames.Origin(url) == null) return;
            if (SpaceOfKey(pane.Key) != SpaceId(_curSpace)) return;
            if (VaultFrames.Origin(url) == HttpsOrigin(pane.View.CoreWebView2?.Source)) return; // top page handles its own origin
            await shell.ExecuteScriptAsync(
                "window.__adcVaultFrameLoaded && window.__adcVaultFrameLoaded(" + pane.Index + "," + JsonSerializer.Serialize(url) + ")");
        }
        catch
        {
            // A vault problem must never break a pane.
        }
    }

    // Tells the cockpit page which real URL a pane is on. The page maps the slot to its card using the
    // tab that is showing, so panes of a hidden tab are skipped (their slot numbers belong to that tab).
    private async Task NotifyVaultPaneLoadedAsync(SlotPane pane, bool manual)
    {
        try
        {
            var core = pane.View.CoreWebView2;
            var shell = ShellView.CoreWebView2;
            if (core == null || shell == null || !pane.Wanted) return;
            if (SpaceOfKey(pane.Key) != SpaceId(_curSpace)) return;
            var src = core.Source ?? "";
            if (!Uri.TryCreate(src, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            {
                if (manual)
                    await shell.ExecuteScriptAsync("window.__adcVaultFillResult && window.__adcVaultFillResult(" + pane.Index + ",-1," + JsonSerializer.Serialize(u?.Host ?? "") + ")");
                return;
            }
            var frameUrls = FramesOf(pane)?.OtherOriginUrls(src) ?? new List<string>();
            await shell.ExecuteScriptAsync(
                "window.__adcVaultPaneLoaded && window.__adcVaultPaneLoaded(" + pane.Index + "," + JsonSerializer.Serialize(src) + "," + (manual ? "true" : "false") + "," + JsonSerializer.Serialize(frameUrls) + ")");
        }
        catch
        {
            // A vault problem must never break a pane.
        }
    }

    private static string? HttpsOrigin(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps) return null;
        return "https://" + u.IdnHost.ToLowerInvariant() + (u.IsDefaultPort ? "" : ":" + u.Port);
    }

    private void ApplyVaultFill(JsonElement root)
    {
        try
        {
            var key = ReadKey(root); // tabs: (tab, slot) key
            if (key < 0) return;
            // Manual request (Fill login button / Ctrl+Shift+L in the cockpit page): no data, answer with the pane URL.
            if (root.TryGetProperty("request", out var reqEl) && reqEl.ValueKind == JsonValueKind.True)
            {
                if (_panes.TryGetValue(key, out var reqPane) && reqPane.Wanted)
                    _ = NotifyVaultPaneLoadedAsync(reqPane, true);
                return;
            }
            var url = ReadString(root, "url");
            if (!_panes.TryGetValue(key, out var pane) || !pane.Wanted) return;
            var core = pane.View.CoreWebView2;
            if (core == null) return;
            var want = HttpsOrigin(url);
            var have = HttpsOrigin(core.Source);
            var inFrame = root.TryGetProperty("frame", out var fr) && fr.ValueKind == JsonValueKind.True;
            if (inFrame && want != null && !string.Equals(want, have, StringComparison.Ordinal))
            {
                var fscript = BuildVaultFillScript(root, want);
                if (fscript == null) return;
                var report = root.TryGetProperty("report", out var frep) && frep.ValueKind == JsonValueKind.True;
                _ = InjectFrameFillAsync(pane, fscript, want, report);
                return;
            }
            if (want == null || have == null || !string.Equals(want, have, StringComparison.Ordinal))
            {
                if (root.TryGetProperty("report", out var rep) && rep.ValueKind == JsonValueKind.True)
                    _ = ReportFillAsync(pane.Index, "-1", have ?? "");
                return;
            }
            var script = BuildVaultFillScript(root, want);
            if (script == null) return;
            _ = InjectFillAsync(core, script, pane.Index, want);
        }
        catch
        {
            // Never surface or log fill data.
        }
    }

    private static string? BuildVaultFillScript(JsonElement root, string want)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
        string Str(string name) => data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
        var auto = data.TryGetProperty("autoSubmit", out var a) && a.ValueKind == JsonValueKind.True;
        var adv = data.TryGetProperty("autoAdvance", out var av) && av.ValueKind == JsonValueKind.True;
        var payload = new Dictionary<string, object>
        {
            ["u"] = Str("u"), ["p"] = Str("p"), ["x"] = Str("x"), ["xl"] = Str("xl"), ["autoSubmit"] = auto, ["autoAdvance"] = adv,
            // r7.2: manual fills (Fill login / Ctrl+Shift+L) report back; only those may overwrite a typed password.
            ["force"] = root.TryGetProperty("report", out var rf) && rf.ValueKind == JsonValueKind.True
        };
        return "(function(){if(location.origin!==" + JsonSerializer.Serialize(want) + ")return -1;\n"
            + VaultFillJs + "\nreturn adcVaultFill(window,document," + JsonSerializer.Serialize(payload) + ");})();";
    }

    private async Task InjectFrameFillAsync(SlotPane pane, string script, string origin, bool report)
    {
        int? n = null;
        try
        {
            var frames = FramesOf(pane);
            if (frames != null) n = await frames.ExecuteInOriginAsync(origin, script);
        }
        catch { n = null; }
        script = "";
        if (report && n != null) await ReportFillAsync(pane.Index, n.Value.ToString(), origin.Replace("https://", ""));
    }

    private async Task InjectFillAsync(CoreWebView2 core, string script, int slot, string origin)
    {
        string result = "0";
        try { result = await core.ExecuteScriptAsync(script); }
        catch { result = "0"; }
        script = "";
        await ReportFillAsync(slot, result is "null" or "" ? "0" : result, origin.Replace("https://", ""));
    }

    private async Task ReportFillAsync(int slot, string count, string where)
    {
        try
        {
            if (!int.TryParse(count, out var n)) n = 0;
            var shell = ShellView.CoreWebView2;
            if (shell != null)
                await shell.ExecuteScriptAsync("window.__adcVaultFillResult && window.__adcVaultFillResult(" + slot + "," + n + "," + JsonSerializer.Serialize(where) + ")");
        }
        catch { }
    }

    // Same-origin vault API for the WV3 page: https://adc-wv2.local/adc-vault/<action> is forwarded to the
    // frame proxy on 127.0.0.1:8791 with Origin https://adc-wv2.local. Bodies are passed through, never logged.
    private static readonly HttpClient VaultHttp = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(AdcSettings.Vault.TimeoutSeconds) };
    private static int VaultMaxBody => AdcSettings.Vault.MaxBodyBytes; // appsettings Vault:*

    private async void ServeVault(CoreWebView2WebResourceRequestedEventArgs args, Uri uri)
    {
        var deferral = args.GetDeferral();
        const string headers = "Content-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff";
        try
        {
            if (_environment == null) return;
            var action = uri.AbsolutePath.Substring("/adc-vault/".Length);
            if (!string.Equals(args.Request.Method, "POST", StringComparison.OrdinalIgnoreCase)
                || action.Length == 0 || action.Length > 20 || !action.All(char.IsAsciiLetterLower))
            {
                args.Response = _environment.CreateWebResourceResponse(null, 405, "Method Not Allowed", headers);
                return;
            }
            byte[] body;
            using (var stream = args.Request.Content)
            using (var ms = new MemoryStream())
            {
                if (stream != null)
                {
                    var buf = new byte[4096];
                    int n;
                    while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > VaultMaxBody) break;
                    }
                }
                body = ms.ToArray();
            }
            if (body.Length > VaultMaxBody)
            {
                args.Response = _environment.CreateWebResourceResponse(null, 413, "Too Large", headers);
                return;
            }
            using var req = new HttpRequestMessage(HttpMethod.Post, AdcSettings.Vault.ProxyBaseUrl.TrimEnd('/') + "/adc-vault/" + action);
            req.Content = new ByteArrayContent(body.Length == 0 ? "{}"u8.ToArray() : body);
            req.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            req.Headers.TryAddWithoutValidation("Origin", "https://" + VirtualHost);
            if (args.Request.Headers.Contains("X-ADC-Vault-Token"))
                req.Headers.TryAddWithoutValidation("X-ADC-Vault-Token", args.Request.Headers.GetHeader("X-ADC-Vault-Token"));
            using var resp = await VaultHttp.SendAsync(req);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            args.Response = _environment.CreateWebResourceResponse(
                new MemoryStream(bytes), (int)resp.StatusCode, string.IsNullOrEmpty(resp.ReasonPhrase) ? "OK" : resp.ReasonPhrase, headers);
        }
        catch
        {
            try
            {
                if (_environment != null)
                    args.Response = _environment.CreateWebResourceResponse(
                        new MemoryStream("{\"error\":\"The vault service is not reachable (frame proxy on 127.0.0.1:8791).\"}"u8.ToArray()), 502, "Bad Gateway", headers);
            }
            catch { }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
