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
    private const string VaultFillJs = @"function adcVaultFill(win, doc, d){
  if(!d || !doc || !doc.documentElement) return;
  /* One watcher per document. A new call (manual Fill login, or a re-run) replaces the old one. */
  try { if(win.__adcVaultFillCtl && win.__adcVaultFillCtl.stop) win.__adcVaultFillCtl.stop(); } catch(e){}
  var filled = new win.WeakSet(), advanced = new win.WeakSet(), submitted = new win.WeakSet(), mine = new win.WeakSet();
  var start = Date.now(), LIFE = 15 * 60 * 1000, stopped = false, timer = 0, obs = null, count = 0;
  function blob(el){ return ((el.getAttribute('autocomplete') || '') + ' ' + (el.name || '') + ' ' + (el.id || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('placeholder') || '') + ' ' + (el.getAttribute('data-testid') || '')).toLowerCase(); }
  function typ(el){ return String(el.type || 'text').toLowerCase(); }
  function vis(el){
    if(!el || el.disabled || el.readOnly || typ(el) === 'hidden') return false;
    try {
      var r = el.getBoundingClientRect();
      if(!(r.width > 0 && r.height > 0)) return false;
      var cs = win.getComputedStyle(el);
      return cs.visibility !== 'hidden' && cs.display !== 'none' && Number(cs.opacity || 1) > 0.05;
    } catch(e){ return false; }
  }
  function isOtp(el){ return /one-time-code|one-time|\botp\b|totp|mfa|2fa|verification|security.?code|passcode|\bpin\b|captcha|search/.test(blob(el)); }
  function isPass(el){ return typ(el) === 'password' && !isOtp(el); }
  function isIdent(el){
    var t = typ(el);
    if(t !== 'text' && t !== 'email' && t !== 'tel' && t !== '' && t !== 'number') return false;
    if(isOtp(el)) return false;
    if(t === 'email' || t === 'tel') return true;
    var b = blob(el);
    return /\b(username|email|tel)\b/.test(el.getAttribute('autocomplete') || '') ||
      /user|e-?mail|login|logon|userid|account|phone|mobile|cell|identifier|member/.test(b);
  }
  function setVal(el, v){
    try {
      var proto = win.HTMLInputElement && el instanceof win.HTMLInputElement ? win.HTMLInputElement.prototype : null;
      var desc = proto && Object.getOwnPropertyDescriptor(proto, 'value');
      if(el.focus) el.focus();
      /* Native setter so React/MUI/Vue see a real change, then the events a typing user would fire. */
      if(desc && desc.set) desc.set.call(el, v); else el.value = v;
      var ie;
      try { ie = new win.InputEvent('input', { bubbles: true, cancelable: false, inputType: 'insertText', data: String(v) }); }
      catch(e2){ ie = new win.Event('input', { bubbles: true }); }
      el.dispatchEvent(ie);
      el.dispatchEvent(new win.Event('change', { bubbles: true }));
      try { el.dispatchEvent(new win.KeyboardEvent('keyup', { bubbles: true, key: 'Unidentified' })); } catch(e3){}
      count++;
    } catch(e){}
  }
  /* Phone fields often cap length or reject punctuation: fall back to digits when the stored text does not fit. */
  function identValue(el, v){
    var max = parseInt(el.getAttribute('maxlength') || '0', 10);
    var phoneish = typ(el) === 'tel' || /phone|mobile|cell|\btel\b/.test(blob(el));
    var digits = String(v).replace(/[^0-9]/g, '');
    if(phoneish && digits.length >= 7 && /^[\s0-9()+.\-]+$/.test(String(v))){
      if(digits.length === 11 && digits.charAt(0) === '1' && (max === 10 || max === 14)) digits = digits.slice(1);
      if(max > 0 && String(v).length > max) return digits.slice(0, max);
      if(typ(el) === 'number') return digits;
    }
    return v;
  }
  /* Small notice inside the login page itself (the cockpit page cannot see into the pane). Never shows values. */
  function note(msg){
    try {
      var n = doc.createElement('div');
      n.textContent = msg;
      n.setAttribute('style', 'position:fixed;z-index:2147483647;left:50%;bottom:24px;transform:translateX(-50%);background:#1f2937;color:#fff;font:14px/1.4 system-ui,sans-serif;padding:8px 14px;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.35);pointer-events:none');
      (doc.body || doc.documentElement).appendChild(n);
      setTimeout(function(){ try { n.remove(); } catch(e){} }, 3500);
    } catch(e){}
  }
  function enabled(el){ return !!el && !el.disabled && String(el.getAttribute('aria-disabled') || '').toLowerCase() !== 'true' && vis(el); }
  function label(el){ return String((el.innerText || el.textContent || el.value || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('title') || '')).replace(/\s+/g, ' ').trim(); }
  var GO = /^(next|continue|send( me a)? code|send|get code|text me( a code)?|verify|sign ?in|log ?in|login|submit|proceed|go)\b/i;
  var NOGO = /google|apple|facebook|microsoft|sso|create|sign ?up|register|forgot|reset|help|back|cancel|privacy|terms|password instead|different/i;
  /* Returns {el, how} for the Next-style control, preferring the form's own submit button. */
  function advanceTarget(field){
    var f = field && field.form;
    if(f){
      var subs = Array.prototype.slice.call(f.querySelectorAll('button[type=submit], input[type=submit], button:not([type])')).filter(function(b){ return vis(b) && !NOGO.test(label(b)); });
      if(subs.length) return { el: subs[0], how: 'button' };
    }
    var all = Array.prototype.slice.call(doc.querySelectorAll('button, input[type=submit], input[type=button], [role=button], a[href]')).filter(vis);
    for(var i = 0; i < all.length; i++){
      var t = label(all[i]);
      if(t && t.length < 40 && GO.test(t) && !NOGO.test(t)) return { el: all[i], how: 'text' };
    }
    if(f && f.requestSubmit) return { el: null, how: 'form' };
    return null;
  }
  /* Waits up to 3 s for the button to enable (React often enables it only after the input event), then presses once. */
  function press(field, set, what){
    if(set.has(field)) return;
    set.add(field);
    var t0 = Date.now();
    (function attempt(){
      try {
        if(stopped) return;
        var tg = advanceTarget(field);
        if(tg && tg.el && enabled(tg.el)){
          tg.el.click();
          note(what + ': clicked ' + (label(tg.el).slice(0, 24) || 'Next'));
          return;
        }
        if(Date.now() - t0 < 3000){ setTimeout(attempt, 150); return; }
        var f = field.form;
        if(tg && tg.how === 'form' && f && f.requestSubmit){ f.requestSubmit(); note(what + ': submitted the form'); return; }
        note(what + (tg && tg.el ? ': the ' + (label(tg.el).slice(0, 24) || 'Next') + ' button stayed disabled' : ': no button found') + ' - press it yourself');
      } catch(e){}
    })();
  }
  function pass1(){
    if(stopped) return;
    if(Date.now() - start > LIFE){ stop(); return; }
    var list = Array.prototype.slice.call(doc.querySelectorAll('input')).filter(vis);
    var passes = list.filter(isPass);
    var idents = list.filter(isIdent);
    if(passes.length){
      var pass = passes[0];
      /* Same-screen username: the identifier just before the password, only if empty. */
      if(d.u){
        var best = null, idx = list.indexOf(pass);
        for(var i = 0; i < idx; i++){ if(isIdent(list[i]) || (typ(list[i]) === 'text' && !isOtp(list[i]))) best = list[i]; }
        if(best && !filled.has(best) && !best.value){ setVal(best, identValue(best, d.u)); filled.add(best); }
      }
      /* r7.2: an automatic (re)start never overwrites a password the user already typed; only a manual Fill (d.force) does. */
    if(d.p && !filled.has(pass) && isPass(pass)){ if(!pass.value || d.force){ setVal(pass, d.p); mine.add(pass); } filled.add(pass); }
      if(d.autoSubmit && mine.has(pass) && pass.value === d.p) press(pass, submitted, 'Auto-submit');
    } else if(d.u && idents.length){
      /* Identifier-only screen (phone, email, or user id). */
      var id = idents[0];
      if(!filled.has(id)){
        if(!id.value || id.value === d.u){ setVal(id, identValue(id, d.u)); }
        filled.add(id);
        if(d.autoAdvance && id.value) press(id, advanced, 'Auto-advance');
      }
    }
    if(d.x && d.xl){
      var key = String(d.xl).toLowerCase();
      var xf = list.filter(function(el){ return !isPass(el) && typ(el) !== 'password' && blob(el).indexOf(key) >= 0; })[0] || null;
      if(xf && !filled.has(xf)){ setVal(xf, d.x); filled.add(xf); }
    }
  }
  function queue(){ if(timer || stopped) return; timer = setTimeout(function(){ timer = 0; pass1(); }, 150); }
  function stop(){
    stopped = true;
    try { if(obs) obs.disconnect(); } catch(e){}
    clearTimeout(timer);
    d = null;
  }
  win.__adcVaultFillCtl = { stop: stop };
  pass1();
  try {
    obs = new win.MutationObserver(queue);
    obs.observe(doc.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'hidden', 'type', 'disabled'] });
  } catch(e){}
  try { win.addEventListener('pagehide', stop); } catch(e){}
  /* SPA screens that swap without DOM mutations we can see (rare): re-check now and then. */
  (function tick(){ if(stopped) return; pass1(); setTimeout(tick, 1500); })();
  return count;
}
";

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
