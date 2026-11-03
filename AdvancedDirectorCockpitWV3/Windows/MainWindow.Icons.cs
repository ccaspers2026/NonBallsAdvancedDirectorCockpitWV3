using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-10-11 (adc-iconpick): POST /adc-icon from the Add card / Edit card form's "Image file..." button.
// Body: {"name":"logo.png","data":"<base64 of the file>"}. Accepts PNG, JPG/JPEG, ICO, WebP, GIF up to 1 MB
// (SVG is refused: it can carry script and would be served from the cockpit's own origin). The bytes must
// decode as an image (ToPng in MainWindow.Favicons.cs: largest frame, at most 64 px, re-encoded as PNG, so
// nothing but pixels is kept). Saved as a BoardIcon row in the CFD database (2026-11-02; was <data>\icons, the store
// MainWindow.Favicons.cs uses for site icons; served as data/icons/... by ServeLocal) as upload_<safe-name>.png.
// Site icons there are named <host-slug>.png (FavSlug: a-z, 0-9, dashes only, never '_'), so an upload name can
// never equal a site-icon name. Never overwrites: an identical earlier UPLOAD (same SHA-256) is reused (site icons
// and hand-placed files are never reused or touched); a different file with a taken name gets -2, -3, ...
// Answers {"iconImage":"data/icons/upload_<name>.png","reused":false} or {"message":"..."} with 400/413/415/500.
// The page writes iconImage into the card and saves it the usual way (POST /adc-data).
public partial class MainWindow
{
    private void ServeIconUpload(CoreWebView2WebResourceRequestedEventArgs args, string noStore)
    {
        var env = _environment;
        if (env == null) return;
        IconStore.Result r;
        try
        {
            using var body = args.Request.Content;
            r = IconStore.Save(body, CfdSql.Board, ToPng);
        }
        catch
        {
            r = IconStore.Fail(500, "The icon was not saved.");
        }
        args.Response = env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(r.Json)), r.Status,
            r.Status == 200 ? "OK" : "Icon Not Saved", "Content-Type: application/json; charset=utf-8\r\n" + noStore);
    }
}

// No WebView2 or WPF types below, so it can be tested on its own.
internal static class IconStore
{
    public const int MaxBytes = 1024 * 1024;
    public const string Folder = "data/icons";   // URL prefix the page stores in iconImage
    public const string DiskFolder = "icons";    // under the Data folder (same as MainWindow.Favicons.cs)
    public const string Prefix = "upload_";      // '_' never appears in a FavSlug site-icon name
    private static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".ico", ".webp", ".gif" };
    private static readonly object Gate = new();

    public readonly record struct Result(int Status, string Json);

    public static Result Fail(int status, string message) =>
        new(status, JsonSerializer.Serialize(new { message }));

    public static Result Save(Stream? body, AdvancedDirectorCockpitWV3.Data.Repositories.BoardRepository store, Func<byte[], byte[]?> toPng)
    {
        if (body == null) return Fail(400, "No file was sent.");
        string text;
        using (var reader = new StreamReader(body, Encoding.UTF8))
        {
            var buf = new char[MaxBytes / 3 * 4 + 8192];
            var n = reader.ReadBlock(buf, 0, buf.Length);
            if (reader.Peek() >= 0) return Fail(413, "That image is over 1 MB.");
            text = new string(buf, 0, n);
        }
        string name, data;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            name = root.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : "";
            data = root.TryGetProperty("data", out var dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString() ?? "" : "";
        }
        catch (JsonException) { return Fail(400, "The upload was not readable."); }
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (!Exts.Contains(ext))
            return Fail(415, "Use a PNG, JPG, ICO, WebP or GIF file" + (ext == ".svg" ? " (SVG is not accepted)." : "."));
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch (FormatException) { return Fail(400, "The upload was not readable."); }
        if (bytes.Length == 0) return Fail(400, "That file is empty.");
        if (bytes.Length > MaxBytes) return Fail(413, "That image is over 1 MB.");
        if (!LooksLikeImage(bytes)) return Fail(415, "That file is not a PNG, JPG, ICO, WebP or GIF image.");
        var png = toPng(bytes);
        if (png == null || png.Length == 0) return Fail(415, "That image could not be read (at least 16 px is needed).");

        // 2026-11-02 board in SQL: icons are BoardIcon rows (served as data/icons/<name>), not files.
        lock (Gate)
        {
            var hash = Convert.ToHexString(SHA256.HashData(png));
            foreach (var (existing, content) in store.IconsWithPrefix(Prefix))
            {
                if (!existing.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                if (content.Length == png.Length && Convert.ToHexString(SHA256.HashData(content)) == hash)
                    return Ok(existing, reused: true);
            }
            var slug = Prefix + Slug(Path.GetFileNameWithoutExtension(name));
            for (var i = 1; i < 1000; i++)
            {
                var file = slug + (i == 1 ? "" : "-" + i) + ".png";
                if (store.TryAddIcon(file, png)) return Ok(file, reused: false);
            }
        }
        return Fail(500, "No free file name for that icon.");
    }

    private static Result Ok(string file, bool reused) =>
        new(200, JsonSerializer.Serialize(new { iconImage = Folder + "/" + file, reused }));

    // Lower-case a-z, 0-9 and single dashes, at most 48 characters; "icon" when nothing is left.
    public static string Slug(string? raw)
    {
        var s = Regex.Replace((raw ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (s.Length > 48) s = s.Substring(0, 48).Trim('-');
        return s.Length == 0 ? "icon" : s;
    }

    public static bool LooksLikeImage(byte[] b)
    {
        bool At(int i, params byte[] sig) { if (b.Length < i + sig.Length) return false; for (var k = 0; k < sig.Length; k++) if (b[i + k] != sig[k]) return false; return true; }
        return At(0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)           // PNG
            || At(0, 0xFF, 0xD8, 0xFF)                                       // JPEG
            || At(0, 0x00, 0x00, 0x01, 0x00)                                 // ICO
            || At(0, 0x47, 0x49, 0x46, 0x38)                                 // GIF8
            || (At(0, 0x52, 0x49, 0x46, 0x46) && At(8, 0x57, 0x45, 0x42, 0x50)); // RIFF....WEBP
    }

    // Full path of rel under root, or null if it would leave root.
    public static string? SafeUnder(string root, string rel)
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
}
