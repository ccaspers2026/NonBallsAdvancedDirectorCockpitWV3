using System.Text.Json;
using AdvancedDirectorCockpitWV3.Data;
using AdvancedDirectorCockpitWV3.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AdvancedDirectorCockpitWV3.Data.Tests;

// 2026-11-02 board in SQL: EF Migrate then dbUp seed fills the Board* tables; the repository builds the page's
// documents from them and saves them back (deduplicated). Disposable databases only.
public class BoardRepositoryTests
{
    static BoardRepository Seeded(TempDb t)
    {
        using (var db = CfdDbContext.Create(t.ConnectionString)) db.Database.Migrate();
        var (ok, msg) = CfdSeeder.Run(t.ConnectionString);
        Assert.True(ok, msg);
        return new BoardRepository(t.ConnectionString);
    }

    [Fact] public void Seed_fills_board_and_documents_have_page_shape()
    {
        using var t = new TempDb();
        var repo = Seeded(t);
        Assert.True((int)t.Scalar("SELECT COUNT(*) FROM dbo.BoardCard")! > 0);
        Assert.True((int)t.Scalar("SELECT COUNT(*) FROM dbo.BoardIcon")! > 0);
        foreach (var name in BoardRepository.Documents)
        {
            using var doc = JsonDocument.Parse(repo.GetDocumentJson(name)!);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty(BoardRepository.ArrayKey(name)).ValueKind);
        }
        using var st = JsonDocument.Parse(repo.GetDocumentJson("stages")!);
        Assert.Equal(JsonValueKind.String, st.RootElement.GetProperty("nativeSlot").ValueKind);
        Assert.Equal(JsonValueKind.Object, st.RootElement.GetProperty("defaults").ValueKind);
        Assert.Null(repo.GetDocumentJson("nope"));
    }

    [Fact] public void Seed_has_no_duplicate_ids()
    {
        using var t = new TempDb();
        Seeded(t);
        using var doc = JsonDocument.Parse(new BoardRepository(t.ConnectionString).GetDocumentJson("cards")!);
        var ids = doc.RootElement.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact] public void SavePage_round_trips_and_dedupes()
    {
        using var t = new TempDb();
        var repo = Seeded(t);
        var body = """
        {"cards":[{"id":"a","groupId":"g","name":"A","url":"https://a.example/"},{"id":"a","name":"dup"},{"id":"b","name":"B"}],
         "groups":[{"id":"g","title":"G"}],"tabs":[{"id":"launch","label":"Main"}],
         "stages":{"nativeSlot":"@native","layouts":[{"id":"single","count":1}],"defaults":{"launch":{"layout":"single","slots":[]}}}}
        """;
        using (var d = JsonDocument.Parse(body)) repo.SavePage(d.RootElement);
        using var cards = JsonDocument.Parse(repo.GetDocumentJson("cards")!);
        var arr = cards.RootElement.GetProperty("cards").EnumerateArray().ToList();
        Assert.Equal(new[] { "a", "b" }, arr.Select(c => c.GetProperty("id").GetString()));
        Assert.Equal("A", arr[0].GetProperty("name").GetString());
        Assert.True(cards.RootElement.TryGetProperty("_readme", out _)); // doc settings kept
        Assert.Equal("https://a.example/", repo.GetCards()[0]["url"]!.GetValue<string>());
        using var st = JsonDocument.Parse(repo.GetDocumentJson("stages")!);
        Assert.Single(st.RootElement.GetProperty("layouts").EnumerateArray());
        Assert.Equal("@native", st.RootElement.GetProperty("nativeSlot").GetString());
    }

    [Fact] public void SavePage_rejects_bad_body_and_writes_nothing()
    {
        using var t = new TempDb();
        var repo = Seeded(t);
        var before = (int)t.Scalar("SELECT COUNT(*) FROM dbo.BoardCard")!;
        using var d = JsonDocument.Parse("""{"cards":[],"groups":[],"tabs":[]}""");
        Assert.Throws<InvalidOperationException>(() => repo.SavePage(d.RootElement));
        Assert.Equal(before, (int)t.Scalar("SELECT COUNT(*) FROM dbo.BoardCard")!);
    }

    [Fact] public void Icons_add_get_and_never_overwrite()
    {
        using var t = new TempDb();
        var repo = Seeded(t);
        Assert.True(repo.TryAddIcon("upload_x.png", new byte[] { 1, 2, 3 }));
        Assert.False(repo.TryAddIcon("upload_x.png", new byte[] { 9 }));
        Assert.Equal(new byte[] { 1, 2, 3 }, repo.GetIcon("upload_x.png"));
        Assert.True(repo.IconExists("upload_x.png"));
        Assert.Contains(repo.IconsWithPrefix("upload_"), i => i.Name == "upload_x.png");
        Assert.Null(repo.GetIcon("missing.png"));
    }
}