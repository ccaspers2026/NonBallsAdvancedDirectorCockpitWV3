using System.IO;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-11-02 board in SQL. The page keeps its URLs (data/cards.json, data/groups.json, data/tabs.json,
// data/stages.json, data/icons/<name>) but every answer is built from the CFD Board* tables (CfdSql.Board).
// There is no data folder and no board JSON file at runtime.
public partial class MainWindow
{
    private void ServeBoardData(CoreWebView2WebResourceRequestedEventArgs args, string rel, string noStore)
    {
        var env = _environment!;
        try
        {
            if (rel.StartsWith("icons/", StringComparison.OrdinalIgnoreCase))
            {
                var name = rel.Substring(6);
                var png = name.Contains('/') || name.Contains('\\') ? null : CfdSql.Board.GetIcon(name);
                args.Response = png == null
                    ? env.CreateWebResourceResponse(null, 404, "Not Found", noStore)
                    : env.CreateWebResourceResponse(new MemoryStream(png), 200, "OK", "Content-Type: " + ContentTypeFor(name) + "\r\n" + noStore);
                return;
            }
            var doc = rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? rel[..^5] : rel;
            var json = CfdSql.Board.GetDocumentJson(doc.ToLowerInvariant());
            args.Response = json == null
                ? env.CreateWebResourceResponse(null, 404, "Not Found", noStore)
                : env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(json)), 200, "OK", "Content-Type: application/json; charset=utf-8\r\n" + noStore);
        }
        catch
        {
            args.Response = env.CreateWebResourceResponse(null, 503, "Board Unavailable", noStore);
        }
    }
}
