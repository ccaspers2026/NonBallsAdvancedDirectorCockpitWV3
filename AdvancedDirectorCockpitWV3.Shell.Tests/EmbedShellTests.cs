using AdvancedDirectorCockpitWV3;

namespace AdvancedDirectorCockpitWV3.Shell.Tests;

public class EmbedShellTests
{
    [Fact] public void EmbeddedExStyle_adds_toolwindow_strips_appwindow_and_edges()
    {
        var ex = EmbedShell.EmbeddedExStyle(EmbedShell.WsExAppWindow | EmbedShell.WsExTopmost | EmbedShell.WsExClientEdge | 0x10);
        Assert.Equal(EmbedShell.WsExToolWindow | 0x10, ex);
    }

    [Theory]
    [InlineData(0L, 0L, false, true)]
    [InlineData(EmbedShell.WsChild, 0L, false, false)]
    [InlineData(0L, EmbedShell.WsExToolWindow, false, false)]
    [InlineData(0L, 0L, true, false)]
    [InlineData(0L, EmbedShell.WsExAppWindow, true, true)]
    public void ShouldHaveTab(long style, long ex, bool owner, bool expected) => Assert.Equal(expected, EmbedShell.ShouldHaveTab(style, ex, owner));

    [Theory]
    [InlineData("chrome", true)] [InlineData("MSEDGE", true)] [InlineData("1Password", false)] [InlineData("", false)]
    public void NeverAdoptRunning(string image, bool expected) => Assert.Equal(expected, EmbedShell.NeverAdoptRunning(image));

    [Fact] public void Format_Parse_roundtrip_and_cleans_tabs()
    {
        var r = new OrphanRecord(0x1234, 42, 0x14CF0000, 0x100, true, "chrome", "a\tb");
        var back = EmbedShell.Parse(EmbedShell.Format(r))!;
        Assert.Equal(r with { Target = "a b" }, back);
    }

    [Theory]
    [InlineData("")] [InlineData("v2\t1\t1\t0\t0\t1\tx\ty")] [InlineData("v1\t0\t1\t0\t0\t1\tx\ty")] [InlineData("v1\t1\t0\t0\t0\t1\tx\ty")] [InlineData("v1\t1\t1\tZZ\t0\t1\tx\ty")]
    public void Parse_rejects_bad_lines(string line) => Assert.Null(EmbedShell.Parse(line));

    [Fact] public void ParseAll_dedupes_Upsert_replaces_Remove_drops()
    {
        var a = new OrphanRecord(1, 1, 0, 0, false, "a", "");
        var b = new OrphanRecord(2, 2, 0, 0, false, "b", "");
        var list = EmbedShell.ParseAll(new[] { EmbedShell.Format(a), EmbedShell.Format(a), "junk", EmbedShell.Format(b) });
        Assert.Equal(2, list.Count);
        list = EmbedShell.Upsert(list, a with { Image = "a2" });
        Assert.Equal(2, list.Count);
        Assert.Equal("a2", list.Single(x => x.Hwnd == 1).Image);
        Assert.Equal(new long[] { 2 }, EmbedShell.Remove(list, 1).Select(x => x.Hwnd));
    }

    [Fact] public void VisibleFraction_and_Rescue()
    {
        var work = new Box(0, 0, 1920, 1040);
        Assert.Equal(1.0, EmbedShell.VisibleFraction(new Box(10, 10, 110, 110), new[] { work }));
        Assert.Equal(0.5, EmbedShell.VisibleFraction(new Box(-50, 0, 50, 100), new[] { work }), 3);
        Assert.Equal(0, EmbedShell.VisibleFraction(new Box(5000, 0, 5100, 100), new[] { work }));
        var r = EmbedShell.Rescue(new Box(5000, 0, 5100, 100), work);
        Assert.Equal(640, r.Width); Assert.Equal(480, r.Height);
        Assert.Equal((1920 - 640) / 2, r.Left);
        Assert.True(EmbedShell.VisibleFraction(r, new[] { work }) == 1.0);
    }
}
