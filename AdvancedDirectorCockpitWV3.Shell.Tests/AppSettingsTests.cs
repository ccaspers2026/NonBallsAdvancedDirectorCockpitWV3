using System.IO;
using AdvancedDirectorCockpitWV3.Data;
using AdvancedDirectorCockpitWV3.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdvancedDirectorCockpitWV3.Shell.Tests;

// 2026-10-31 appsettings refactor: the Shell's own appsettings files bind and validate for both environments.
public class AppSettingsTests
{
    static IConfiguration Load(string env) => new ConfigurationBuilder()
        .SetBasePath(CfdDbContextFactory.FindShellDir())
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{env}.json", optional: false)
        .Build();

    static ServiceProvider Build(IConfiguration cfg)
    {
        var s = new ServiceCollection();
        s.AddAdcCockpit(cfg);
        s.AddCfdServices(cfg);
        return s.BuildServiceProvider();
    }

    [Theory, InlineData("Development"), InlineData("Production")]
    public void Environment_binds_and_validates(string env)
    {
        using var sp = Build(Load(env));
        var c = sp.GetRequiredService<IOptions<CockpitOptions>>().Value;
        Assert.Equal("adc-wv2.local", c.VirtualHost);
        Assert.Equal(16, c.MaxSlots);
        Assert.EndsWith("AdvancedDirectorCockpitWV3.exe", c.PubExePath);
        var v = sp.GetRequiredService<IOptions<VaultOptions>>().Value;
        Assert.Equal("", v.StoreDir);
        Assert.Equal(16384, v.MaxBodyBytes);
        Assert.NotNull(sp.GetRequiredService<CfdStartupService>());
    }

    [Fact] public void Base_appsettings_alone_fails_validation_without_ConnectionStrings_Cfd()
    {
        var cfg = new ConfigurationBuilder().SetBasePath(CfdDbContextFactory.FindShellDir()).AddJsonFile("appsettings.json").Build();
        using var sp = Build(cfg);
        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<CfdOptions>>().Value);
    }

    [Fact] public void Shell_output_contains_appsettings_files()
    {
        var shellBin = Path.GetDirectoryName(typeof(CockpitOptions).Assembly.Location)!;
        Assert.True(File.Exists(Path.Combine(shellBin, "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(shellBin, "appsettings.Development.json")));
        Assert.True(File.Exists(Path.Combine(shellBin, "appsettings.Production.json")));
    }
}