using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdvancedDirectorCockpitWV3.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdvancedDirectorCockpitWV3.Data.Repositories;

// 2026-11-02 board in SQL. The page still speaks the old document shapes (data/cards.json etc.); the host
// builds those documents from the Board* tables and saves them back here. No board JSON file is read or written.
//   cards  -> { ...doc.cards.*,  "cards":  [BoardCard.Json] }
//   groups -> { ...doc.groups.*, "groups": [BoardGroup.Json] }
//   tabs   -> { ...doc.tabs.*,   "tabs":   [BoardTab.Json] }
//   stages -> { ...doc.stages.*, "layouts":[BoardStageLayout.Json] }   (nativeSlot, defaults live in BoardSetting)
public sealed class BoardRepository
{
    public static readonly string[] Documents = { "cards", "groups", "tabs", "stages" };
    private static readonly object SaveGate = new();
    private readonly string _cs;

    public BoardRepository(string connectionString) { ArgumentException.ThrowIfNullOrWhiteSpace(connectionString); _cs = connectionString; }

    public static string ArrayKey(string doc) => doc == "stages" ? "layouts" : doc;

    // Returns null for an unknown document name.
    public string? GetDocumentJson(string doc)
    {
        if (!Documents.Contains(doc)) return null;
        using var db = CfdDbContext.Create(_cs);
        var prefix = "doc." + doc + ".";
        var settings = db.BoardSetting.AsNoTracking().Where(s => s.Key.StartsWith(prefix)).OrderBy(s => s.Seq).ThenBy(s => s.Key).ToList();
        var rows = doc switch
        {
            "cards" => db.BoardCard.AsNoTracking().OrderBy(r => r.Seq).Select(r => r.Json).ToList(),
            "groups" => db.BoardGroup.AsNoTracking().OrderBy(r => r.Seq).Select(r => r.Json).ToList(),
            "tabs" => db.BoardTab.AsNoTracking().OrderBy(r => r.Seq).Select(r => r.Json).ToList(),
            _ => db.BoardStageLayout.AsNoTracking().OrderBy(r => r.Seq).Select(r => r.Json).ToList(),
        };
        var o = new JsonObject();
        foreach (var s in settings.Where(s => s.Key == prefix + "_readme")) o["_readme"] = Parse(s.ValueJson);
        var arr = new JsonArray();
        foreach (var j in rows) arr.Add(Parse(j));
        o[ArrayKey(doc)] = arr;
        foreach (var s in settings.Where(s => s.Key != prefix + "_readme")) o[s.Key.Substring(prefix.Length)] = Parse(s.ValueJson);
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public List<JsonObject> GetCards()
    {
        using var db = CfdDbContext.Create(_cs);
        return db.BoardCard.AsNoTracking().OrderBy(r => r.Seq).Select(r => r.Json).ToList()
            .Select(j => Parse(j) as JsonObject).Where(o => o != null).Select(o => o!).ToList();
    }

    // Saves the page's POST /adc-data body {cards:[], groups:[], tabs:[], stages:{nativeSlot, layouts:[], defaults:{}}}.
    // All four in one transaction; rows are deduplicated by id (first occurrence wins, same as the seed).
    public void SavePage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("body");
        var cards = Rows(root, "cards"); var groups = Rows(root, "groups"); var tabs = Rows(root, "tabs");
        if (!root.TryGetProperty("stages", out var st) || st.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("stages");
        if (!st.TryGetProperty("nativeSlot", out var native) || native.ValueKind != JsonValueKind.String) throw new InvalidOperationException("stages");
        if (!st.TryGetProperty("defaults", out var defaults) || defaults.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("stages");
        var layouts = Rows(st, "layouts");
        lock (SaveGate)
        {
            using var db = CfdDbContext.Create(_cs);
            using var tx = db.Database.BeginTransaction();
            db.BoardCard.ExecuteDelete(); db.BoardGroup.ExecuteDelete(); db.BoardTab.ExecuteDelete(); db.BoardStageLayout.ExecuteDelete();
            var i = 0;
            foreach (var r in Dedupe(cards)) db.BoardCard.Add(new BoardCard { Id = r.id, Seq = i++, GroupId = S(r.el, "groupId"), Name = S(r.el, "name"), App = S(r.el, "app"), Url = Cap(S(r.el, "url"), 2000), Json = r.el.GetRawText() });
            i = 0;
            foreach (var r in Dedupe(groups)) db.BoardGroup.Add(new BoardGroup { Id = r.id, Seq = i++, ParentId = S(r.el, "parentId"), Title = S(r.el, "title"), Json = r.el.GetRawText() });
            i = 0;
            foreach (var r in Dedupe(tabs)) db.BoardTab.Add(new BoardTab { Id = r.id, Seq = i++, Label = S(r.el, "label"), Json = r.el.GetRawText() });
            i = 0;
            foreach (var r in Dedupe(layouts)) db.BoardStageLayout.Add(new BoardStageLayout { Id = r.id, Seq = i++, Json = r.el.GetRawText() });
            db.SaveChanges();
            UpsertSetting(db, "doc.stages.nativeSlot", native.GetRawText());
            UpsertSetting(db, "doc.stages.defaults", defaults.GetRawText());
            db.SaveChanges();
            tx.Commit();
        }
    }

    // ---------- icons (served as data/icons/<name>) ----------
    public byte[]? GetIcon(string name)
    {
        using var db = CfdDbContext.Create(_cs);
        return db.BoardIcon.AsNoTracking().Where(x => x.Name == name).Select(x => x.Content).FirstOrDefault();
    }

    public bool IconExists(string name) { using var db = CfdDbContext.Create(_cs); return db.BoardIcon.Any(x => x.Name == name); }

    public List<(string Name, byte[] Content)> IconsWithPrefix(string prefix)
    {
        using var db = CfdDbContext.Create(_cs);
        return db.BoardIcon.AsNoTracking().Where(x => x.Name.StartsWith(prefix)).Select(x => new { x.Name, x.Content }).ToList()
            .Select(x => (x.Name, x.Content)).ToList();
    }

    // false = name already taken (nothing written).
    public bool TryAddIcon(string name, byte[] content)
    {
        using var db = CfdDbContext.Create(_cs);
        if (db.BoardIcon.Any(x => x.Name == name)) return false;
        db.BoardIcon.Add(new BoardIcon { Name = name, Content = content });
        try { db.SaveChanges(); return true; } catch (DbUpdateException) { return false; }
    }

    // ---------- helpers ----------
    private static void UpsertSetting(CfdDbContext db, string key, string json)
    {
        var s = db.BoardSetting.FirstOrDefault(x => x.Key == key);
        if (s == null) db.BoardSetting.Add(new BoardSetting { Key = key, Seq = 10, ValueJson = json });
        else { s.ValueJson = json; s.UpdatedAt = DateTimeOffset.Now; }
    }

    private static List<JsonElement> Rows(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) throw new InvalidOperationException(key);
        var list = new List<JsonElement>();
        foreach (var r in arr.EnumerateArray()) { if (r.ValueKind != JsonValueKind.Object) throw new InvalidOperationException(key); list.Add(r.Clone()); }
        return list;
    }

    internal static IEnumerable<(string id, JsonElement el)> Dedupe(IEnumerable<JsonElement> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var id = S(r, "id");
            if (string.IsNullOrEmpty(id)) id = "noid-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(r.GetRawText())))[..16];
            if (id.Length > 200 || !seen.Add(id)) continue;
            yield return (id, r);
        }
    }

    private static string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? Cap(string? s, int n) => s == null || s.Length <= n ? s : s[..n];
    private static JsonNode? Parse(string? json) { if (string.IsNullOrWhiteSpace(json)) return null; try { return JsonNode.Parse(json); } catch { return JsonValue.Create(json); } }
}
