using System.IO;
using System.Text;
using System.Text.Json;

namespace AdvancedDirectorCockpitWV3.Shell.Tests;

// 2026-11-02: in-process vault (port of adc_vault.py). Temp store + temp cards.json; test PIN only.
public class AdcVaultTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "adcvault-test-" + Guid.NewGuid().ToString("N"));
    readonly AdcVault _v;

    public AdcVaultTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "cards.json"), "{\"cards\":[{\"id\":\"c1\",\"url\":\"https://www.example.com/login\"}]}");
        var psl = Path.Combine(AppContext.BaseDirectory, "Vault", "public_suffix_list.dat");
        _v = new AdcVault(Path.Combine(_dir, "store"), () => new[] { Path.Combine(_dir, "cards.json") }, psl);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    (int s, JsonElement j) Call(string a, object? body = null, string tok = "")
    {
        var (s, b) = _v.Handle(a, body == null ? Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(body), tok);
        return (s, JsonDocument.Parse(b).RootElement.Clone());
    }

    [Fact] public void Status_ping_unlock_save_fill_roundtrip()
    {
        var (s, j) = Call("status");
        Assert.Equal(200, s); Assert.False(j.GetProperty("initialized").GetBoolean());
        Assert.Equal(401, Call("ping").s);
        Assert.Equal(400, Call("setup", new { pin = "123" }).s);
        var tok = Call("setup", new { pin = "test-pin-1" }).j.GetProperty("token").GetString()!;
        Assert.Equal(200, Call("ping", null, tok).s);
        Assert.Equal(200, Call("save", new { cardId = "c1", username = "u1", password = "p1" }, tok).s);
        Assert.Equal(200, Call("lock", null, tok).s);
        Assert.Equal(403, Call("unlock", new { pin = "wrong-pin" }).s);
        tok = Call("unlock", new { pin = "test-pin-1" }).j.GetProperty("token").GetString()!;
        var g = Call("get", new { cardId = "c1" }, tok).j;
        Assert.True(g.GetProperty("exists").GetBoolean()); Assert.Equal("u1", g.GetProperty("username").GetString());
        Assert.Equal("p1", Call("fill", new { cardId = "c1", url = "https://secure.example.com/x" }, tok).j.GetProperty("p").GetString());
        Assert.Equal(403, Call("fill", new { cardId = "c1", url = "https://evil.test/" }, tok).s);
        var st = Call("status", null, tok).j;
        Assert.True(st.GetProperty("unlocked").GetBoolean());
        Assert.Contains("c1", st.GetProperty("ids").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("p1", File.ReadAllText(Path.Combine(_dir, "store", "vault.dat")));
    }
}
