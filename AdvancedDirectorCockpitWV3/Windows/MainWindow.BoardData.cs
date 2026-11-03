using System.IO;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-11-03 board is SQL/LocalDB only. The page reads https://<VirtualHost>/adc-board/<doc>
// (doc = cards | groups | tabs | stages) and saves with POST /adc-data; both go to the CFD Board* tables
// (CfdSql.Board). Card images stay at data/icons/<name> (the URL stored in iconImage) and are BoardIcon rows.
// Nothing here reads or writes a file: there is no data folder and no board JSON file at runtime.
// The old data/<doc>.json URLs answer 410 Gone so a stale page or stale build can never fall back to files.
public partial class MainWindow
{
    internal static readonly string[] BoardDocuments = { "cards", "groups", "tabs", "stages" };

    private void ServeBoardDocument(CoreWebView2WebResourceRequestedEventArgs args, string doc, string noStore)
    {
        var env = _environment!;
        doc = doc.Trim('/').ToLowerInvariant();
        if (Array.IndexOf(BoardDocuments, doc) < 0)
        {
            args.Response = env.CreateWebResourceResponse(null, 404, "Not Found", noStore);
            return;
        }
        try
        {
            var json = CfdSql.Board.GetDocumentJson(doc);
            args.Response = json == null
                ? env.CreateWebResourceResponse(null, 404, "Not Found", noStore)
                : env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(json)), 200, "OK", "Content-Type: application/json; charset=utf-8\r\n" + noStore);
        }
        catch
        {
            args.Response = env.CreateWebResourceResponse(null, 503, "Board Unavailable", noStore);
        }
    }

    private void ServeBoardData(CoreWebView2WebResourceRequestedEventArgs args, string rel, string noStore)
    {
        var env = _environment!;
        if (!rel.StartsWith("icons/", StringComparison.OrdinalIgnoreCase))
        {
            // Retired data/*.json board URLs: the board is SQL-only (adc-board/*).
            args.Response = env.CreateWebResourceResponse(null, 410, "Gone", noStore);
            return;
        }
        try
        {
            var name = rel.Substring(6);
            var png = name.Contains('/') || name.Contains('\\') ? null : CfdSql.Board.GetIcon(name);
            args.Response = png == null
                ? env.CreateWebResourceResponse(null, 404, "Not Found", noStore)
                : env.CreateWebResourceResponse(new MemoryStream(png), 200, "OK", "Content-Type: " + ContentTypeFor(name) + "\r\n" + noStore);
        }
        catch
        {
            args.Response = env.CreateWebResourceResponse(null, 503, "Board Unavailable", noStore);
        }
    }
}