using System.IO;

namespace AdvancedDirectorCockpitWV3.Shell.Tests;

public class MachineLocalPathsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "adc-paths-" + Guid.NewGuid().ToString("N"));

    public MachineLocalPathsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_configured_path_stays_empty(string? configured)
    {
        Assert.Equal("", MachineLocalPaths.ResolveConfigured(configured, _root));
    }

    [Fact]
    public void Relative_path_resolves_next_to_the_app_and_does_not_invent_a_drive()
    {
        var relative = Path.Combine("publish", MachineLocalPaths.PubExeFileName);
        var got = MachineLocalPaths.ResolveConfigured(relative, _root);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, relative)), got);
        Assert.DoesNotContain("V:" + Path.DirectorySeparatorChar + "_Data", got, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(_root), got, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Absolute_configured_path_is_normalized_not_rebased()
    {
        var abs = Path.Combine(_root, "elsewhere", "vault.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        var got = MachineLocalPaths.ResolveConfigured(abs, Path.Combine(_root, "app"));
        Assert.Equal(Path.GetFullPath(abs), got);
    }

    [Fact]
    public void Percent_env_token_expands_before_relative_resolution()
    {
        var name = "ADC_TEST_IMPORT_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, Path.Combine("legacy", "vault.dat"));
        try
        {
            var got = MachineLocalPaths.ResolveConfigured("%" + name + "%", _root);
            Assert.Equal(Path.GetFullPath(Path.Combine(_root, "legacy", "vault.dat")), got);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Unset_pub_exe_noops_when_no_publish_output_exists()
    {
        var app = Path.Combine(_root, "proj", "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(_root, "proj", "AdvancedDirectorCockpitWV3.csproj"), "<Project />");
        var process = Path.Combine(app, MachineLocalPaths.PubExeFileName);
        Assert.Equal("", MachineLocalPaths.ResolvePubExe("", app, process));
        Assert.False(MachineLocalPaths.IsPublishOutput(process));
    }

    [Fact]
    public void Unset_pub_exe_uses_this_process_when_it_is_the_publish_output()
    {
        var pub = Path.Combine(_root, "publish", MachineLocalPaths.PubExeFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(pub)!);
        Assert.Equal(Path.GetFullPath(pub), MachineLocalPaths.ResolvePubExe(null, _root, pub));
        Assert.True(MachineLocalPaths.IsPublishOutput(pub));
    }

    [Fact]
    public void Unset_pub_exe_does_not_adopt_a_sibling_publish_folder()
    {
        var project = Path.Combine(_root, "AdvancedDirectorCockpitWV3");
        var app = Path.Combine(project, "bin", "Debug", "net10.0-windows");
        var pub = Path.Combine(project, "bin", "Release", "net10.0-windows", "publish", MachineLocalPaths.PubExeFileName);
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.GetDirectoryName(pub)!);
        File.WriteAllBytes(pub, new byte[] { 1 });
        var process = Path.Combine(app, MachineLocalPaths.PubExeFileName);
        Assert.Equal("", MachineLocalPaths.ResolvePubExe("", app, process));
    }

    [Fact]
    public void Configured_relative_pub_exe_resolves_next_to_the_app()
    {
        var app = Path.Combine(_root, "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(app);
        var relative = Path.Combine("..", "..", "..", "publish", MachineLocalPaths.PubExeFileName);
        var got = MachineLocalPaths.ResolvePubExe(relative, app, Path.Combine(app, MachineLocalPaths.PubExeFileName));
        Assert.Equal(Path.GetFullPath(Path.Combine(app, relative)), got);
        Assert.DoesNotContain("V:" + Path.DirectorySeparatorChar, got, StringComparison.OrdinalIgnoreCase);
    }
}
