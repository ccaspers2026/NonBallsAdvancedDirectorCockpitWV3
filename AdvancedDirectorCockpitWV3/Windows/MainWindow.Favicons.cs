using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-10-09: GET /adc-favicon?url=<card url> finds the site's best icon (apple-touch-icon, largest
// <link rel=icon>, manifest icon, then /favicon.ico, then Google s2 at 64 px), saves it once as PNG
// (max 64x64) under <data>\icons\<host>.png, and answers {"iconImage":"data/icons/<host>.png"}.
// The page writes that into the card (only when the card has no iconImage). 204 when no icon.
public partial class MainWindow
{
    private static readonly HttpClient FavHttp = CreateFavHttp();
    private static readonly ConcurrentDictionary<string, bool> FavFailed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim FavGate = new(2);

    private static HttpClient CreateFavHttp()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36");
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return c;
    }

    private void ServeFavicon(CoreWebView2WebResourceRequestedEventArgs args, Uri uri, string noStore)
    {
        var env = _environment;
        if (env == null) return;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var target = query["url"] ?? "";
        if (!Uri.TryCreate(target, UriKind.Absolute, out var site) || (site.Scheme != "https" && site.Scheme != "http") || string.IsNullOrEmpty(site.Host))
        {
            args.Response = env.CreateWebResourceResponse(null, 400, "Bad Request", noStore);
            return;
        }
        var deferral = args.GetDeferral();
        var dispatcher = Dispatcher;
        _ = Task.Run(async () =>
        {
            string? rel = null;
            try { rel = await FindOrFetchFaviconAsync(site); } catch { rel = null; }
            await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (rel == null)
                        args.Response = env.CreateWebResourceResponse(null, 204, "No Icon", noStore);
                    else
                    {
                        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { iconImage = rel }));
                        args.Response = env.CreateWebResourceResponse(new MemoryStream(body), 200, "OK",
                            "Content-Type: application/json; charset=utf-8\r\n" + noStore);
                    }
                }
                finally { deferral.Complete(); }
            });
        });
    }

    private static string FavSlug(string host) => Regex.Replace(host.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static async Task<string?> FindOrFetchFaviconAsync(Uri site)
    {
        var host = site.Host;
        var name = FavSlug(host) + ".png";
        var rel = "data/icons/" + name;
        if (CfdSql.Board.IconExists(name)) return rel;
        if (FavFailed.ContainsKey(host)) return null;
        await FavGate.WaitAsync();
        try
        {
            if (CfdSql.Board.IconExists(name)) return rel;
            var png = await FetchIconPngAsync(site);
            if (png == null)
            {
                FavFailed[host] = true;
                return null;
            }
            CfdSql.Board.TryAddIcon(name, png); // BoardIcon row, served as data/icons/<name>
            return rel;
        }
        finally { FavGate.Release(); }
    }

    private static string BaseDomain(string host)
    {
        var p = host.Split('.');
        if (p.Length >= 3 && p[^1].Length == 2 && new[] { "co", "com", "org", "net", "gov", "ac" }.Contains(p[^2]))
            return string.Join(".", p[^3..]);
        return p.Length >= 2 ? string.Join(".", p[^2..]) : host;
    }

    private static readonly Regex LinkTag = new("<link\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Attr = new("([a-zA-Z_:-]+)\\s*=\\s*(\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.Compiled);

    private static int SizeOf(string? sizes)
    {
        var best = 0;
        foreach (var tok in (sizes ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(tok, "^(\\d+)x(\\d+)");
            if (m.Success) best = Math.Max(best, int.Parse(m.Groups[1].Value));
            else if (tok == "any") best = Math.Max(best, 512);
        }
        return best;
    }

    private static async Task<List<(int size, Uri url)>> PageCandidatesAsync(Uri page, List<Uri> finals)
    {
        var list = new List<(int, Uri)>();
        try
        {
            using var res = await FavHttp.GetAsync(page);
            if (!res.IsSuccessStatusCode) return list;
            var final = res.RequestMessage?.RequestUri ?? page;
            finals.Add(final);
            var text = await res.Content.ReadAsStringAsync();
            if (text.Length > 3_000_000) text = text.Substring(0, 3_000_000);
            foreach (Match tag in LinkTag.Matches(text))
            {
                var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in Attr.Matches(tag.Value))
                    a[m.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value);
                if (!a.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href)) continue;
                if (!Uri.TryCreate(final, href, out var abs)) continue;
                var rel = (a.TryGetValue("rel", out var r) ? r : "").ToLowerInvariant();
                a.TryGetValue("sizes", out var sizes);
                if (rel.Contains("apple-touch-icon")) list.Add((SizeOf(sizes) is var s1 && s1 > 0 ? s1 : 180, abs));
                else if (rel.Contains("mask-icon")) continue;
                else if (rel.Split(' ').Contains("icon")) list.Add((SizeOf(sizes) is var s2 && s2 > 0 ? s2 : 32, abs));
                else if (rel == "manifest")
                {
                    try
                    {
                        using var mr = await FavHttp.GetAsync(abs);
                        if (!mr.IsSuccessStatusCode) continue;
                        var mfinal = mr.RequestMessage?.RequestUri ?? abs;
                        using var doc = JsonDocument.Parse(await mr.Content.ReadAsStringAsync());
                        if (doc.RootElement.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Array)
                            foreach (var ic in icons.EnumerateArray())
                                if (ic.TryGetProperty("src", out var src) && src.ValueKind == JsonValueKind.String && Uri.TryCreate(mfinal, src.GetString(), out var iu))
                                    list.Add((SizeOf(ic.TryGetProperty("sizes", out var sz) && sz.ValueKind == JsonValueKind.String ? sz.GetString() : null) is var s3 && s3 > 0 ? s3 : 64, iu));
                    }
                    catch { }
                }
            }
        }
        catch { }
        return list;
    }

    private static async Task<byte[]?> TryImageAsync(Uri url)
    {
        try
        {
            if (url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
            using var res = await FavHttp.GetAsync(url);
            if (!res.IsSuccessStatusCode) return null;
            var bytes = await res.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0 || bytes.Length > 3_000_000) return null;
            return ToPng(bytes);
        }
        catch { return null; }
    }

    // Decodes PNG/ICO/JPEG/GIF/BMP, keeps the largest frame, scales to at most 64 px, returns PNG bytes.
    private static byte[]? ToPng(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource? best = null;
            foreach (var f in decoder.Frames)
                if (best == null || f.PixelWidth * f.PixelHeight > best.PixelWidth * best.PixelHeight) best = f;
            if (best == null || Math.Max(best.PixelWidth, best.PixelHeight) < 16) return null;
            BitmapSource outSrc = best;
            var max = Math.Max(best.PixelWidth, best.PixelHeight);
            if (max > 64)
            {
                var k = 64.0 / max;
                outSrc = new TransformedBitmap(best, new ScaleTransform(k, k));
            }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(outSrc));
            using var outMs = new MemoryStream();
            enc.Save(outMs);
            return outMs.ToArray();
        }
        catch { return null; }
    }

    private static async Task<byte[]?> FetchIconPngAsync(Uri site)
    {
        var host = site.Host;
        var bd = BaseDomain(host);
        var roots = new List<Uri> { site, new Uri("https://" + host + "/") };
        if (!string.Equals(bd, host, StringComparison.OrdinalIgnoreCase))
        {
            roots.Add(new Uri("https://www." + bd + "/"));
            roots.Add(new Uri("https://" + bd + "/"));
        }
        var finals = new List<Uri>();
        var cands = new List<(int size, Uri url)>();
        foreach (var r in roots.Distinct()) cands.AddRange(await PageCandidatesAsync(r, finals));
        foreach (var c in cands.OrderByDescending(c => c.size).Select(c => c.url).Distinct())
        {
            var png = await TryImageAsync(c);
            if (png != null) return png;
        }
        var origins = new List<string> { "https://" + host };
        origins.AddRange(finals.Select(f => f.Scheme + "://" + f.Host));
        if (!string.Equals(bd, host, StringComparison.OrdinalIgnoreCase)) origins.Add("https://www." + bd);
        foreach (var o in origins.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var png = await TryImageAsync(new Uri(o + "/favicon.ico"));
            if (png != null) return png;
        }
        foreach (var d in new[] { host, bd }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var png = await TryImageAsync(new Uri("https://www.google.com/s2/favicons?domain=" + Uri.EscapeDataString(d) + "&sz=64"));
            if (png != null) return png;
        }
        return null;
    }
}
