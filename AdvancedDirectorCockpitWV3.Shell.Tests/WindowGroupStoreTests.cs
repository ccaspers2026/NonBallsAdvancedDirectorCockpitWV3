using System.IO;
using AdvancedDirectorCockpitWV3.Windows;

namespace AdvancedDirectorCockpitWV3.Shell.Tests;

public class WindowGroupStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "adc-wg-" + Guid.NewGuid().ToString("N"), "window-groups.json");

    [Fact]
    public void Upsert_RoundTrips_MonitorAndPosition()
    {
        var p = TempFile();
        WindowGroupStore.Upsert(new WindowGroup { Name = "Trading", Windows = { new WindowGroupWindow { Key = "main", Monitor = @"\\.\DISPLAY2", L = 10, T = 20, R = 1610, B = 970, Max = true }, new WindowGroupWindow { Key = "w2", Monitor = @"\\.\DISPLAY1", L = -1900, T = 0, R = -100, B = 1000 } } }, p);
        var g = WindowGroupStore.Find("trading", p);
        Assert.NotNull(g);
        Assert.Equal(2, g!.Windows.Count);
        Assert.Equal(@"\\.\DISPLAY2", g.Windows[0].Monitor);
        Assert.True(g.Windows[0].Max);
        Assert.Equal(-1900, g.Windows[1].L);
    }

    [Fact]
    public void Upsert_SameName_Replaces_AndSorts()
    {
        var p = TempFile();
        WindowGroupStore.Upsert(new WindowGroup { Name = "Zeta" }, p);
        WindowGroupStore.Upsert(new WindowGroup { Name = "Alpha", Windows = { new WindowGroupWindow { Key = "main" } } }, p);
        WindowGroupStore.Upsert(new WindowGroup { Name = " alpha " }, p);
        var all = WindowGroupStore.Load(p);
        Assert.Equal(new[] { "alpha", "Zeta" }, all.Select(x => x.Name).ToArray());
        Assert.Empty(all[0].Windows);
    }

    [Fact]
    public void Load_MissingOrBadFile_IsEmpty()
    {
        var p = TempFile();
        Assert.Empty(WindowGroupStore.Load(p));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "{not json");
        Assert.Empty(WindowGroupStore.Load(p));
        Assert.Throws<ArgumentException>(() => WindowGroupStore.Upsert(new WindowGroup { Name = "  " }, p));
    }
}
