using AdvancedDirectorCockpitWV3.Data;
using AdvancedDirectorCockpitWV3.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvancedDirectorCockpitWV3.Services.Tests;

// Gate: zero user tables -> EF Migrate (schema) THEN dbUp seeds; otherwise probe-only (nothing written).
// Runs against disposable databases on the Development LocalDB server (Shell appsettings.Development.json); never CFD.
public class CfdStartupServiceTests
{
    static readonly string DevCfd = CfdConnection.Require(new ConfigurationBuilder()
        .SetBasePath(CfdDbContextFactory.FindShellDir())
        .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json").AddEnvironmentVariables().Build());

    static string Cs(string db) => new SqlConnectionStringBuilder(DevCfd) { InitialCatalog = db, ConnectTimeout = 30 }.ConnectionString;
    static object? Q(string db, string sql) { using var c = new SqlConnection(Cs(db)); c.Open(); using var k = new SqlCommand(sql, c); return k.ExecuteScalar(); }

    static void WithTempDb(Action<string> body)
    {
        var name = "WV3_SvcTest_" + Guid.NewGuid().ToString("N")[..12];
        Q("master", $"CREATE DATABASE [{name}]");
        try { body(name); }
        finally
        {
            SqlConnection.ClearAllPools();
            Q("master", $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }

    [Fact] public void Empty_db_migrates_then_seeds_then_connects() => WithTempDb(db =>
    {
        var svc = new CfdStartupService(Cs(db));
        Assert.True(svc.Verify(), svc.Status);
        Assert.Contains("schema migrated; seeds applied", svc.Status);
        Assert.Contains($"db={db}", svc.Status);
        Assert.Equal(1, Q(db, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory"));
        Assert.Equal(1, Q(db, "SELECT COUNT(*) FROM dbo.SchemaVersions_Seed"));
    });

    [Fact] public void Second_start_on_populated_db_is_probe_only() => WithTempDb(db =>
    {
        var svc = new CfdStartupService(Cs(db));
        Assert.True(svc.Verify());
        Assert.True(svc.Verify(), svc.Status);
        Assert.DoesNotContain("migrated", svc.Status);
        Assert.Equal(1, Q(db, "SELECT COUNT(*) FROM dbo.SchemaVersions_Seed"));
    });

    [Fact] public void Non_empty_db_never_migrates_or_seeds() => WithTempDb(db =>
    {
        Q(db, "CREATE TABLE dbo.Unrelated(Id int)");
        var svc = new CfdStartupService(Cs(db));
        Assert.False(svc.Verify()); // probe needs dbo.Loan -> fails, but nothing is written
        Assert.StartsWith("CFD SQL: connection FAILED", svc.Status);
        Assert.Equal(1, Q(db, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0"));
    });

    [Fact] public void Unreachable_server_reports_failed()
    {
        var svc = new CfdStartupService(@"Server=(localdb)\NoSuchInstance_WV3;Database=x;Trusted_Connection=True;Connect Timeout=2");
        Assert.False(svc.Verify());
        Assert.False(svc.Connected);
        Assert.StartsWith("CFD SQL: connection FAILED", svc.Status);
    }

    [Fact] public void AddCfdServices_resolves_singleton_with_configured_connection_string()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [CfdConnection.ConfigKey] = Cs("WV3_never_opened") }).Build();
        using var sp = new ServiceCollection().AddCfdServices(cfg).BuildServiceProvider();
        var a = sp.GetRequiredService<CfdStartupService>();
        Assert.Same(a, sp.GetRequiredService<CfdStartupService>());
        Assert.Equal("CFD SQL: not checked", a.Status);
    }

    [Fact] public void Empty_connection_string_is_rejected()
        => Assert.ThrowsAny<ArgumentException>(() => new CfdStartupService(""));
}