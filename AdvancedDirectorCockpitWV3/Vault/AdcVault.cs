using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdvancedDirectorCockpitWV3.Vault;

// 2026-11-02: in-process port of adc_vault.py (frame-proxy /adc-vault/*). No Python, no port: the WebView2
// host calls Handle() directly from ServeVault for https://<VirtualHost>/adc-vault/<action>.
// File format is identical to adc_vault.py (v1): PBKDF2-SHA256 PIN key, DPAPI(CurrentUser, entropy "ADC-Vault-v1")
// wrapping nonce|AES-256-GCM(data key), data = nonce|AES-256-GCM(entries JSON). An existing vault.dat opens as-is.
// Nothing here logs, prints, or returns a secret except copy/fill, after every check passed.
internal sealed class AdcVault
{
    public const int KdfIter = 600_000, MinPin = 6, MaxPin = 64, FreeTries = 3, MaxLockout = 300;
    public static readonly TimeSpan SessionIdle = TimeSpan.FromSeconds(120);
    static readonly byte[] AadDk = "adc-vault-dk"u8.ToArray(), AadData = "adc-vault-data"u8.ToArray();

    readonly object _lock = new();
    readonly string _dir, _path, _pending, _psl;
    readonly Func<IEnumerable<string>> _cardFiles;
    readonly Func<IEnumerable<string>> _servedRoots;
    byte[]? _key;
    JsonObject? _entries;
    readonly Dictionary<string, DateTime> _sessions = new();
    int _fails;
    DateTime _lockedUntil = DateTime.MinValue;
    readonly Timer _reaper;

    public string VaultPath => _path;

    public AdcVault(string storeDir, Func<IEnumerable<string>> cardFiles, string pslPath, Func<IEnumerable<string>>? servedRoots = null)
    {
        _dir = Path.GetFullPath(storeDir);
        _path = Path.Combine(_dir, "vault.dat");
        _pending = Path.Combine(_dir, "pending-hosts.json");
        _psl = pslPath;
        _cardFiles = cardFiles;
        _servedRoots = servedRoots ?? (() => Array.Empty<string>());
        _reaper = new Timer(_ => { try { lock (_lock) Expire(); } catch { } }, null, 15000, 15000);
    }

    // One-time copy of an existing vault (e.g. the Python proxy's) into this store. Never moves or edits the source.
    public bool ImportIfMissing(string? source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source) || File.Exists(_path) || !File.Exists(source)) return false;
            Directory.CreateDirectory(_dir);
            File.Copy(source, _path, overwrite: false);
            return true;
        }
        catch { return false; }
    }

    // ---------- DPAPI ----------
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int cb; public IntPtr pb; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CryptProtectData(ref Blob dataIn, string? desc, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob dataOut);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr desc, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob dataOut);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);
    static readonly byte[] Entropy = "ADC-Vault-v1"u8.ToArray();

    static byte[] Dpapi(byte[] data, bool protect)
    {
        var hIn = GCHandle.Alloc(data, GCHandleType.Pinned);
        var hEnt = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var bin = new Blob { cb = data.Length, pb = hIn.AddrOfPinnedObject() };
            var ent = new Blob { cb = Entropy.Length, pb = hEnt.AddrOfPinnedObject() };
            Blob outp;
            var ok = protect
                ? CryptProtectData(ref bin, "adc-vault", ref ent, IntPtr.Zero, IntPtr.Zero, 1, out outp)
                : CryptUnprotectData(ref bin, IntPtr.Zero, ref ent, IntPtr.Zero, IntPtr.Zero, 1, out outp);
            if (!ok) throw new CryptographicException("dpapi");
            try { var r = new byte[outp.cb]; Marshal.Copy(outp.pb, r, 0, outp.cb); return r; }
            finally { LocalFree(outp.pb); }
        }
        finally { hIn.Free(); hEnt.Free(); }
    }

    // ---------- crypto ----------
    static byte[] Derive(string pin, byte[] salt, int iters) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iters, HashAlgorithmName.SHA256, 32);

    static byte[] Seal(byte[] key, byte[] plain, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ct = new byte[plain.Length]; var tag = new byte[16];
        using var g = new AesGcm(key, 16);
        g.Encrypt(nonce, plain, ct, tag, aad);
        return [.. nonce, .. ct, .. tag];
    }

    static byte[] Open(byte[] key, byte[] blob, byte[] aad)
    {
        if (blob.Length < 28) throw new CryptographicException("short");
        var nonce = blob.AsSpan(0, 12); var tag = blob.AsSpan(blob.Length - 16); var ct = blob.AsSpan(12, blob.Length - 28);
        var plain = new byte[ct.Length];
        using var g = new AesGcm(key, 16);
        g.Decrypt(nonce, ct, tag, plain, aad);
        return plain;
    }

    static string HostTag(byte[] salt, string host)
    {
        using var h = new HMACSHA256(salt);
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes("host:" + host))).ToLowerInvariant()[..24];
    }

    // ---------- file ----------
    JsonObject? ReadFile()
    {
        if (!File.Exists(_path)) return null;
        var doc = JsonNode.Parse(File.ReadAllText(_path, Encoding.UTF8)) as JsonObject;
        if (doc == null || (int?)doc["v"] != 1) throw new InvalidDataException("vault format");
        return doc;
    }

    void CheckLocation()
    {
        foreach (var root in _servedRoots())
        {
            if (string.IsNullOrEmpty(root)) continue;
            var r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if ((_path + "").StartsWith(r, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("vault path is under a served root");
        }
    }

    void WriteFile(JsonObject doc)
    {
        CheckLocation();
        Directory.CreateDirectory(_dir);
        var text = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var tmp = Path.Combine(_dir, "vault.dat.tmp");
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var b = new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n"));
            fs.Write(b); fs.Flush(true);
        }
        File.Move(tmp, _path, overwrite: true);
    }

    void SaveEntries()
    {
        var doc = ReadFile();
        if (doc == null || _key == null || _entries == null) throw new InvalidOperationException("locked");
        doc["data"] = Convert.ToBase64String(Seal(_key, Encoding.UTF8.GetBytes(_entries.ToJsonString()), AadData));
        doc["index"] = new JsonArray(_entries.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).Select(k => (JsonNode)JsonValue.Create(k)!).ToArray());
        var salt = Convert.FromBase64String((string)doc["salt"]!);
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kv in _entries) { var ch = Str(kv.Value, "ch"); if (ch != "") tags.Add(HostTag(salt, ch)); }
        doc["hostIndex"] = new JsonArray(tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        var now = DateTimeOffset.Now;
        doc["updated"] = now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + now.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "");
        WriteFile(doc);
    }

    void Wipe()
    {
        if (_key != null) CryptographicOperations.ZeroMemory(_key);
        _key = null; _entries = null; _sessions.Clear();
    }

    // ---------- sessions ----------
    void Expire()
    {
        var now = DateTime.UtcNow;
        foreach (var t in _sessions.Where(kv => now - kv.Value > SessionIdle).Select(kv => kv.Key).ToList()) _sessions.Remove(t);
        if (_sessions.Count == 0 && _key != null) Wipe();
    }

    string NewSession()
    {
        var tok = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _sessions[tok] = DateTime.UtcNow;
        return tok;
    }

    bool SessionOk(string tok)
    {
        Expire();
        if (string.IsNullOrEmpty(tok) || _key == null) return false;
        foreach (var known in _sessions.Keys.ToList())
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(known), Encoding.UTF8.GetBytes(tok))) { _sessions[known] = DateTime.UtcNow; return true; }
        return false;
    }

    // ---------- PIN ----------
    static bool PinShape(string? pin) => pin != null && pin.Length >= MinPin && pin.Length <= MaxPin;
    int LockoutLeft() => Math.Max(0, (int)Math.Round((_lockedUntil - DateTime.UtcNow).TotalSeconds));
    int NoteFail()
    {
        _fails++;
        if (_fails < FreeTries) return 0;
        var wait = (int)Math.Min(MaxLockout, 5 * Math.Pow(2, _fails - FreeTries));
        _lockedUntil = DateTime.UtcNow.AddSeconds(wait);
        return wait;
    }

    sealed class VaultErr(int status, object payload) : Exception { public int Status = status; public object Payload = payload; }

    string Setup(string? pin)
    {
        if (!PinShape(pin)) throw new VaultErr(400, new { error = $"PIN must be {MinPin} to {MaxPin} characters" });
        if (ReadFile() != null) throw new VaultErr(409, new { error = "Vault is already set up" });
        var salt = RandomNumberGenerator.GetBytes(16);
        var kpin = Derive(pin!, salt, KdfIter);
        var dk = RandomNumberGenerator.GetBytes(32);
        var wrapped = Dpapi(Seal(kpin, dk, AadDk), protect: true);
        CryptographicOperations.ZeroMemory(kpin);
        WriteFile(new JsonObject { ["v"] = 1, ["kdf"] = "pbkdf2-sha256", ["iter"] = KdfIter, ["salt"] = Convert.ToBase64String(salt), ["wrap"] = Convert.ToBase64String(wrapped), ["index"] = new JsonArray(), ["data"] = "" });
        _key = dk; _entries = new JsonObject();
        SaveEntries();
        _fails = 0;
        return NewSession();
    }

    string Unlock(string? pin)
    {
        var left = LockoutLeft();
        if (left > 0) throw new VaultErr(429, new { error = $"Too many wrong PINs. Try again in {left} s.", retryAfter = left });
        var doc = ReadFile() ?? throw new VaultErr(409, new { error = "Vault is not set up" });
        byte[] dk;
        if (!PinShape(pin)) throw Wrong();
        var kpin = Derive(pin!, Convert.FromBase64String((string)doc["salt"]!), (int)doc["iter"]!);
        var inner = Dpapi(Convert.FromBase64String((string)doc["wrap"]!), protect: false);
        try { dk = Open(kpin, inner, AadDk); }
        catch (CryptographicException) { throw Wrong(); }
        finally { CryptographicOperations.ZeroMemory(kpin); }
        JsonObject entries = new();
        var data = (string?)doc["data"] ?? "";
        if (data != "") entries = JsonNode.Parse(Open(dk, Convert.FromBase64String(data), AadData)) as JsonObject ?? new JsonObject();
        if (_key == null) { _key = dk; _entries = entries; } else CryptographicOperations.ZeroMemory(dk);
        _fails = 0; _lockedUntil = DateTime.MinValue;
        return NewSession();
    }

    VaultErr Wrong()
    {
        var wait = NoteFail();
        return new VaultErr(403, new { error = "Wrong PIN." + (wait > 0 ? $" Wait {wait} s before the next try." : ""), retryAfter = wait });
    }

    // ---------- cards / hosts ----------
    static string Str(JsonNode? n, string k) { try { return n is JsonObject o && o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : ""; } catch { return ""; } }
    static bool Bool(JsonNode? n, string k) { try { return n is JsonObject o && o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b; } catch { return false; } }
    static List<string> Hosts(JsonNode? n) => n is JsonObject o && o["hosts"] is JsonArray a ? a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Where(s => s != "").ToList() : new();

    List<JsonObject> AllCards()
    {
        var outp = new List<JsonObject>();
        foreach (var path in _cardFiles())
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                var rows = node is JsonObject o ? o["cards"] as JsonArray : node as JsonArray;
                if (rows != null) outp.AddRange(rows.OfType<JsonObject>());
            }
            catch { }
        }
        return outp;
    }

    string? CardUrl(string id)
    {
        foreach (var r in AllCards())
            if (Str(r, "id") == id && Str(r, "url") != "") return Str(r, "url");
        return null;
    }

    static string HostOf(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.Host)) return "";
        return u.Host.ToLowerInvariant().TrimEnd('.');
    }

    static (string host, int port)? HostPort(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (!string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(u.Host)) return null;
        return (u.Host.ToLowerInvariant().TrimEnd('.'), u.Port);
    }

    static List<string> CleanHosts(JsonNode? raw)
    {
        var items = new List<string>();
        if (raw is JsonValue sv && sv.TryGetValue<string>(out var s)) items.AddRange(s.Replace(",", "\n").Split('\n'));
        else if (raw is JsonArray a) items.AddRange(a.Select(x => x?.ToString() ?? ""));
        else return new();
        var outp = new List<string>();
        foreach (var item in items)
        {
            var h = item.Trim().ToLowerInvariant();
            if (h == "") continue;
            if (h.Contains("://")) h = Uri.TryCreate(h, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
            h = h.Trim('/').TrimEnd('.');
            var wild = h.StartsWith("*.");
            var core = wild ? h[2..] : h;
            if (core == "" || !core.Contains('.') || core.Any(c => !("abcdefghijklmnopqrstuvwxyz0123456789-.".Contains(c)))) continue;
            h = wild ? "*." + core : core;
            if (!outp.Contains(h)) outp.Add(h);
        }
        return outp.Take(20).ToList();
    }

    // Public suffix list (same file as the Python proxy, copied into Vault\public_suffix_list.dat).
    static readonly string[] NotPublic = [".local", ".localhost", ".localdomain", ".internal", ".home.arpa", ".lan", ".home", ".corp", ".intranet"];
    static readonly HashSet<string> PslFallback2 = ["co.uk","org.uk","ac.uk","gov.uk","me.uk","ltd.uk","plc.uk","net.uk","sch.uk","com.au","net.au","org.au","edu.au","gov.au","co.nz","org.nz","net.nz","co.jp","ne.jp","or.jp","co.in","net.in","org.in","com.br","com.mx","com.cn","com.hk","com.sg","com.tw","co.za","co.kr","com.tr","com.ar","co.il","com.my","com.ph","com.vn","co.id"];
    (HashSet<string> exact, HashSet<string> wild, HashSet<string> exc)? _pslData;
    bool _pslLoaded;
    static readonly IdnMapping Idn = new();
    static string IdnA(string s) { try { return Idn.GetAscii(s).ToLowerInvariant(); } catch { return s.ToLowerInvariant(); } }

    (HashSet<string>, HashSet<string>, HashSet<string>)? LoadPsl()
    {
        if (_pslLoaded) return _pslData;
        _pslLoaded = true;
        HashSet<string> exact = new(), wild = new(), exc = new();
        try
        {
            foreach (var line in File.ReadLines(_psl, Encoding.UTF8))
            {
                var rule = line.Trim().Split(' ')[0];
                if (rule == "" || rule.StartsWith("//")) continue;
                if (rule.StartsWith('!')) exc.Add(IdnA(rule[1..]));
                else if (rule.StartsWith("*.")) wild.Add(IdnA(rule[2..]));
                else exact.Add(IdnA(rule));
            }
        }
        catch { exact.Clear(); }
        _pslData = exact.Count > 0 ? (exact, wild, exc) : null;
        return _pslData;
    }

    internal string? RegistrableDomain(string host)
    {
        var h = IdnA((host ?? "").Trim().TrimEnd('.').ToLowerInvariant());
        if (h == "" || !h.Contains('.') || NotPublic.Any(h.EndsWith)) return null;
        if (IPAddress.TryParse(h.Trim('[', ']'), out _)) return null;
        var labels = h.Split('.');
        if (labels.Any(l => l == "")) return null;
        int suffixLen = 0;
        var psl = LoadPsl();
        if (psl is { } p)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                var cand = string.Join('.', labels[i..]);
                if (p.Item3.Contains(cand)) { suffixLen = labels.Length - i - 1; break; }
                if (p.Item1.Contains(cand)) suffixLen = Math.Max(suffixLen, labels.Length - i);
                if (i + 1 < labels.Length && p.Item2.Contains(string.Join('.', labels[(i + 1)..]))) suffixLen = Math.Max(suffixLen, labels.Length - i);
            }
            if (suffixLen == 0) return null;
        }
        else
        {
            suffixLen = PslFallback2.Contains(string.Join('.', labels[^2..])) ? 2 : 1;
            var last = labels[^1];
            if (!last.All(char.IsLetter) || last.Length < 2) return null;
        }
        if (labels.Length <= suffixLen) return null;
        return string.Join('.', labels[^(suffixLen + 1)..]);
    }

    List<string> EffectiveHosts(string cardUrl, IEnumerable<string> explicitHosts)
    {
        var outp = new List<string>();
        var hp = HostPort(cardUrl);
        if (hp is { } c && c.port == 443 && RegistrableDomain(c.host) is { } reg) outp.Add("*." + reg);
        foreach (var h in explicitHosts) if (!outp.Contains(h)) outp.Add(h);
        return outp;
    }

    internal bool HostAllowed(string pageUrl, string cardUrl, List<string> allowed)
    {
        var page = HostPort(pageUrl);
        if (page is not { } pg) return false;
        if (HostPort(cardUrl) is { } card && card == pg) return true;
        if (pg.port != 443) return false;
        foreach (var e in EffectiveHosts(cardUrl, allowed))
        {
            if (e.StartsWith("*.")) { var core = e[2..]; if (pg.host == core || pg.host.EndsWith("." + core)) return true; }
            else if (pg.host == e) return true;
        }
        return false;
    }

    (string? key, JsonObject? entry) Resolve(string cardId)
    {
        var ents = _entries ?? new JsonObject();
        if (ents[cardId] is JsonObject direct) return (cardId, direct);
        var host = HostOf(CardUrl(cardId));
        if (host == "") return (null, null);
        var live = AllCards().Select(r => Str(r, "id")).ToHashSet();
        foreach (var kv in ents)
            if (kv.Value is JsonObject e && Str(e, "ch") == host && !live.Contains(kv.Key)) return (kv.Key, e);
        return (null, null);
    }

    void ApplyPending()
    {
        try
        {
            var changed = false;
            foreach (var kv in _entries!.ToList())
            {
                if (kv.Value is JsonObject e && Str(e, "ch") == "") { var h = HostOf(CardUrl(kv.Key)); if (h != "") { e["ch"] = h; changed = true; } }
            }
            var hasPending = File.Exists(_pending);
            if (hasPending)
            {
                JsonObject? pend = null;
                try { pend = JsonNode.Parse(File.ReadAllText(_pending)) as JsonObject; } catch { }
                foreach (var kv in pend ?? new JsonObject())
                {
                    var (_, ent) = Resolve(kv.Key);
                    if (ent == null) continue;
                    var cur = Hosts(ent);
                    var merged = CleanHosts(new JsonArray(cur.Concat(CleanHosts(kv.Value?.DeepClone())).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()));
                    if (!merged.SequenceEqual(cur)) { ent["hosts"] = ToArr(merged); changed = true; }
                }
            }
            if (!changed && !hasPending) return;
            SaveEntries();
            if (hasPending) File.Delete(_pending);
        }
        catch { }
    }

    static JsonArray ToArr(IEnumerable<string> xs) => new(xs.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());

    List<string> StatusIds(JsonObject doc)
    {
        var ids = new SortedSet<string>(((doc["index"] as JsonArray) ?? new JsonArray()).Select(x => x?.ToString() ?? "").Where(x => x != ""), StringComparer.Ordinal);
        var tags = ((doc["hostIndex"] as JsonArray) ?? new JsonArray()).Select(x => x?.ToString() ?? "").ToHashSet();
        if (tags.Count > 0)
        {
            try
            {
                var salt = Convert.FromBase64String((string)doc["salt"]!);
                var rows = AllCards();
                var owned = rows.Where(r => ids.Contains(Str(r, "id"))).Select(r => HostTag(salt, HostOf(Str(r, "url")))).ToHashSet();
                foreach (var r in rows)
                {
                    var cid = Str(r, "id"); var h = HostOf(Str(r, "url"));
                    if (cid != "" && h != "") { var t = HostTag(salt, h); if (tags.Contains(t) && !owned.Contains(t)) ids.Add(cid); }
                }
            }
            catch { }
        }
        return ids.ToList();
    }

    // ---------- request ----------
    public (int status, byte[] body) Handle(string action, byte[] rawBody, string headerToken)
    {
        lock (_lock)
        {
            try { return (200, J(HandleCore(action, rawBody, headerToken))); }
            catch (VaultErr e) { return (e.Status, J(e.Payload)); }
            catch { return (500, J(new { error = "vault error" })); }
        }
    }

    static byte[] J(object o) => JsonSerializer.SerializeToUtf8Bytes(o);

    object HandleCore(string action, byte[] rawBody, string token)
    {
        JsonObject body;
        try { body = (rawBody.Length == 0 ? new JsonObject() : JsonNode.Parse(rawBody) as JsonObject) ?? throw new VaultErr(400, new { error = "json" }); }
        catch (VaultErr) { throw; }
        catch { throw new VaultErr(400, new { error = "json" }); }
        token ??= "";
        string bodyTok() => token != "" ? token : Str(body, "token");

        switch (action)
        {
            case "status":
                var doc = ReadFile();
                return new { initialized = doc != null, unlocked = SessionOk(token), ids = doc != null ? StatusIds(doc) : new List<string>(), lockout = LockoutLeft(), minPin = MinPin };
            case "setup": return new { token = Setup(Str(body, "pin")) };
            case "unlock": return new { token = Unlock(body["pin"] is JsonValue pv && pv.TryGetValue<string>(out var pin) ? pin : null) };
            case "lock":
                if (!SessionOk(bodyTok())) throw new VaultErr(401, new { error = "locked" });
                Wipe(); return new { ok = true };
            case "end":
                var t = bodyTok();
                if (t != "") foreach (var k in _sessions.Keys.ToList()) if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(t))) _sessions.Remove(k);
                Expire(); return new { ok = true };
        }
        if (!SessionOk(token)) throw new VaultErr(401, new { error = "locked" });
        ApplyPending();
        if (action == "ping") return new { ok = true };
        var cardId = Str(body, "cardId");
        if (cardId == "" || cardId.Length > 200) throw new VaultErr(400, new { error = "cardId" });
        var (entryKey, entry) = Resolve(cardId);
        switch (action)
        {
            case "get":
                if (entry == null) return new { exists = false };
                return new { exists = true, username = Str(entry, "u"), hasPassword = Str(entry, "p") != "", extraLabel = Str(entry, "xl"), hasExtra = Str(entry, "x") != "",
                    allowedHosts = Hosts(entry), autoSubmit = Bool(entry, "auto"), autoAdvance = Bool(entry, "adv"), savedFor = Str(entry, "ch"), sharedFromOtherCard = entryKey != cardId };
            case "save":
            {
                var cardUrlNow = CardUrl(cardId) ?? throw new VaultErr(404, new { error = "Card not found in cards.json" });
                var old = entry ?? new JsonObject();
                if (entryKey != null && entryKey != cardId) _entries!.Remove(entryKey);
                string Cap(string s, int n) => s.Length > n ? s[..n] : s;
                var pw = Str(body, "password");
                var xv = Str(body, "extra");
                var nw = new JsonObject
                {
                    ["u"] = Cap(Str(body, "username"), 512),
                    ["p"] = pw != "" ? Cap(pw, 1024) : Str(old, "p"),
                    ["xl"] = Cap(Str(body, "extraLabel"), 100),
                    ["hosts"] = ToArr(CleanHosts(body["allowedHosts"]?.DeepClone())),
                    ["auto"] = Bool(body, "autoSubmit"),
                    ["adv"] = Bool(body, "autoAdvance"),
                    ["ch"] = HostOf(cardUrlNow),
                    ["x"] = xv != "" ? Cap(xv, 1024) : (Bool(body, "clearExtra") ? "" : Str(old, "x")),
                };
                if (Str(nw, "u") == "" && Str(nw, "p") == "") throw new VaultErr(400, new { error = "Enter a username or password" });
                _entries![cardId] = nw;
                SaveEntries();
                return new { ok = true };
            }
            case "delete":
                if (entry != null) { _entries!.Remove(entryKey!); SaveEntries(); }
                return new { ok = true };
            case "copy":
            {
                var f = Str(body, "field");
                if (entry == null || f is not ("u" or "p" or "x")) throw new VaultErr(404, new { error = "Nothing saved for this card" });
                var v = Str(entry, f);
                if (v == "") throw new VaultErr(404, new { error = "That field is empty for this card" });
                return new { value = v };
            }
            case "origins":
            {
                if (entry == null) return new { origins = Array.Empty<string>() };
                var cardUrl = CardUrl(cardId) ?? "";
                var outp = new List<string>();
                if (HostPort(cardUrl) is { } hp) outp.Add("https://" + hp.host + (hp.port == 443 ? "" : ":" + hp.port));
                foreach (var h in Hosts(entry)) if (!h.StartsWith("*.")) outp.Add("https://" + h);
                return new { origins = outp, wild = EffectiveHosts(cardUrl, Hosts(entry)).Where(h => h.StartsWith("*.")).Select(h => h[2..]).ToList() };
            }
            case "fill":
            {
                if (entry == null) throw new VaultErr(404, new { error = "none" });
                var cardUrl = CardUrl(cardId);
                var pageUrl = Str(body, "url");
                if (cardUrl == null || !HostAllowed(pageUrl, cardUrl, Hosts(entry))) throw new VaultErr(403, new { error = "origin mismatch", host = HostOf(pageUrl) });
                return new { u = Str(entry, "u"), p = Str(entry, "p"), x = Str(entry, "x"), xl = Str(entry, "xl"), autoSubmit = Bool(entry, "auto"), autoAdvance = Bool(entry, "adv") };
            }
        }
        throw new VaultErr(404, new { error = "unknown" });
    }
}
