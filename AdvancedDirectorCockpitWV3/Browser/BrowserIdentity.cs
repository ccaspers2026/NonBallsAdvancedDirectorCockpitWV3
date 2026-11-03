using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Browser;

// r7.1 (2026-10-10): per-card browser identity. A card with "browserIdentity": "edge" in cards.json gets, in its pane,
// the Edge stable user agent for this WebView2 runtime's version and User-Agent client hints without the
// "Microsoft Edge WebView2" brand (DigitalOcean refused sign-in from the WebView2-branded client). Every other card
// keeps WebView2's default identity. Applied with CDP Emulation.setUserAgentOverride right before the host navigates
// a pane to a card URL; cleared again when that pane is sent to a card without the flag.
internal static class BrowserIdentity
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2, string> Applied = new();

    /// <summary>"edge" when the card for <paramref name="url"/> asks for it, else "". Exact URL match first, then same host.</summary>
    public static string ForUrl(string? cardsJson, string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cardsJson) || !Uri.TryCreate(url, UriKind.Absolute, out var target)) return "";
            using var doc = JsonDocument.Parse(cardsJson);
            var rows = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("cards", out var c) ? c : doc.RootElement;
            if (rows.ValueKind != JsonValueKind.Array) return "";
            string? hostMatch = null;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("url", out var u) || u.ValueKind != JsonValueKind.String) continue;
                if (!Uri.TryCreate(u.GetString(), UriKind.Absolute, out var cardUri)) continue;
                var id = row.TryGetProperty("browserIdentity", out var bi) && bi.ValueKind == JsonValueKind.String ? (bi.GetString() ?? "").Trim().ToLowerInvariant() : "";
                if (id != "edge") id = "";
                if (string.Equals(cardUri.AbsoluteUri.TrimEnd('/'), target.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return id;
                if (hostMatch == null && string.Equals(cardUri.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase)) hostMatch = id;
            }
            return hostMatch ?? "";
        }
        catch { return ""; }
    }

    /// <summary>CDP parameters for an Edge stable identity matching the runtime version (e.g. 154.0.4258.62).</summary>
    public static string EdgeOverrideJson(string browserVersion)
    {
        var full = (browserVersion ?? "").Split(' ')[0];
        var major = full.Split('.')[0];
        if (!int.TryParse(major, out _)) { major = "154"; full = "154.0.0.0"; }
        var build = Environment.OSVersion.Version.Build;
        var platformVersion = build >= 26100 ? "19.0.0" : build >= 22000 ? "15.0.0" : "10.0.0";
        var ua = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36 Edg/{major}.0.0.0";
        var payload = new
        {
            userAgent = ua,
            userAgentMetadata = new
            {
                brands = new[] { new { brand = "Microsoft Edge", version = major }, new { brand = "Not A(Brand", version = "99" }, new { brand = "Chromium", version = major } },
                fullVersionList = new[] { new { brand = "Microsoft Edge", version = full }, new { brand = "Not A(Brand", version = "99.0.0.0" }, new { brand = "Chromium", version = full } },
                fullVersion = full, platform = "Windows", platformVersion, architecture = "x86", model = "", mobile = false, bitness = "64", wow64 = false
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Applies (or clears) the identity on this pane's CoreWebView2. Never throws.</summary>
    public static async Task ApplyAsync(CoreWebView2 core, string identity, string browserVersion)
    {
        try
        {
            Applied.TryGetValue(core, out var have);
            have ??= "";
            if (identity == have) return;
            var json = identity == "edge" ? EdgeOverrideJson(browserVersion) : "{\"userAgent\":\"\"}";
            await core.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride", json);
            Applied.AddOrUpdate(core, identity);
        }
        catch
        {
            // Identity is best effort: a failure leaves the default WebView2 identity.
        }
    }
}