using AdvancedDirectorCockpitWV3.Windows;

namespace AdvancedDirectorCockpitWV3.Shell.Tests;

public class WindowGroupApplyTests
{
    [Fact]
    public void PlanPlacement_AlreadyMaximized_PlacesNormalThenMaximizes()
    {
        var moved = WindowGroupApply.PlanPlacement(maximize: true, placeThenMaximize: true);
        Assert.Equal(new[] { WindowGroupApply.ShowNormal, WindowGroupApply.ShowMaximized }, moved.ShowCmds);
        Assert.Equal(1, moved.ShowCmds[0]);
        Assert.NotEqual(WindowGroupApply.ShowMaximized, moved.ShowCmds[0]);

        Assert.Equal(new[] { WindowGroupApply.ShowMaximized }, WindowGroupApply.PlanPlacement(maximize: true, placeThenMaximize: false).ShowCmds);
        Assert.Equal(new[] { WindowGroupApply.ShowNormal }, WindowGroupApply.PlanPlacement(maximize: false, placeThenMaximize: true).ShowCmds);
        Assert.Equal(new[] { WindowGroupApply.ShowNormal }, WindowGroupApply.PlanPlacement(maximize: false, placeThenMaximize: false).ShowCmds);
    }

    [Fact]
    public void PlanDismiss_Caddy_HidesFinancesExtra_KeepsDetached()
    {
        var caddy = new[] { "main", "w2", "w3", "w4" };
        Assert.Equal(WindowGroupApply.Dismiss.Keep, WindowGroupApply.PlanDismiss("main", detached: false, caddy));
        Assert.Equal(WindowGroupApply.Dismiss.Keep, WindowGroupApply.PlanDismiss("w4", detached: false, caddy));
        Assert.Equal(WindowGroupApply.Dismiss.Hide, WindowGroupApply.PlanDismiss("w5", detached: false, caddy));
        Assert.Equal(WindowGroupApply.Dismiss.Keep, WindowGroupApply.PlanDismiss("solo:news", detached: true, caddy));
        Assert.Equal(WindowGroupApply.Dismiss.Keep, WindowGroupApply.PlanDismiss("", detached: false, caddy));
        Assert.Equal(WindowGroupApply.Dismiss.Hide, WindowGroupApply.PlanDismiss("main", detached: false, new[] { "w2", "w3" }));
    }

    [Fact]
    public void ShouldMove_OnlyWhenCurrentDesktopIsKnownAndDifferent()
    {
        var here = Guid.NewGuid();
        var there = Guid.NewGuid();
        var hwnd = new IntPtr(0x100);
        Assert.True(WindowGroupApply.ShouldMove(here, there, hwnd));
        Assert.True(WindowGroupApply.ShouldMove(here, Guid.Empty, hwnd));
        Assert.False(WindowGroupApply.ShouldMove(here, here, hwnd));
        Assert.False(WindowGroupApply.ShouldMove(Guid.Empty, there, hwnd));
        Assert.False(WindowGroupApply.ShouldMove(here, there, IntPtr.Zero));
    }
}
