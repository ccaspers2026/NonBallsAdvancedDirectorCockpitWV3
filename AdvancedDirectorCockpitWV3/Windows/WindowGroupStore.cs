using System.IO;
using System.Text.Json;

namespace AdvancedDirectorCockpitWV3.Windows;

// 2026-11-04 window groups (slice 1). A named group is a snapshot of the cockpit windows that were open
// when it was saved: each window's key, monitor device name and Win32 normal rect (L/T/R/B) + maximized.
// Stored in %LOCALAPPDATA%\AdvancedDirectorCockpitWV3\window-groups.json (separate from windows.json).
internal sealed class WindowGroupWindow
{
    public string Key { get; set; } = "";
    public string Monitor { get; set; } = "";
    public int L { get; set; }
    public int T { get; set; }
    public int R { get; set; }
    public int B { get; set; }
    public bool Max { get; set; }
    public string? ConfigId { get; set; }
}

internal sealed class WindowGroup
{
    public string Name { get; set; } = "";
    public DateTime SavedUtc { get; set; }
    public List<WindowGroupWindow> Windows { get; set; } = new();
}

internal static class WindowGroupStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdvancedDirectorCockpitWV3", "window-groups.json");

    internal static List<WindowGroup> Load(string? path = null)
    {
        path ??= DefaultPath;
        lock (Gate)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<List<WindowGroup>>(File.ReadAllText(path)) ?? new();
            }
            catch { }
            return new();
        }
    }

    internal static WindowGroup? Find(string name, string? path = null) =>
        Load(path).FirstOrDefault(g => string.Equals(g.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    // Same name (case-insensitive) replaces the old group; list stays sorted by name.
    internal static void Upsert(WindowGroup group, string? path = null)
    {
        path ??= DefaultPath;
        group.Name = (group.Name ?? "").Trim();
        if (group.Name.Length == 0) throw new ArgumentException("Group name is required.");
        lock (Gate)
        {
            var all = Load(path);
            all.RemoveAll(g => string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase));
            all.Add(group);
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(all, Opts));
        }
    }
}
