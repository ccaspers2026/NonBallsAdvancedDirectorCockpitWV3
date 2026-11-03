using AdvancedDirectorCockpitWV3.Data;
using AdvancedDirectorCockpitWV3.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal(2, Q(db, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory"));
        Assert.Equal(3, Q(db, "SELECT COUNT(*) FROM dbo.SchemaVersions_Seed"));
    });

    [Fact] public void Second_start_on_populated_db_is_probe_only() => WithTempDb(db =>
    {
        var svc = new CfdStartupService(Cs(db));
        Assert.True(svc.Verify());
        Assert.True(svc.Verify(), svc.Status);
        Assert.DoesNotContain("migrated", svc.Status);
        Assert.Equal(3, Q(db, "SELECT COUNT(*) FROM dbo.SchemaVersions_Seed"));
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

    // 2026-11-02 board in SQL: a CFD created before EF (five tables, no __EFMigrationsHistory) is baselined at
    // InitialCreate, then AddBoard + seeds run. CFD tables and their rows are untouched.
    [Fact] public void Existing_pre_ef_cfd_is_baselined_then_board_migrated_and_seeded() => WithTempDb(db =>
    {
        using (var ctx = CfdDbContext.Create(Cs(db))) ctx.Database.Migrate(); // full schema...
        Q(db, "DROP TABLE dbo.BoardCard; DROP TABLE dbo.BoardGroup; DROP TABLE dbo.BoardTab; DROP TABLE dbo.BoardStageLayout; DROP TABLE dbo.BoardSetting; DROP TABLE dbo.BoardIcon; DROP TABLE dbo.__EFMigrationsHistory");
        Q(db, "INSERT INTO dbo.SchemaMeta (Version, Notes) VALUES (7, N'pre-existing')"); // ...now looks like the old CFD
        var svc = new CfdStartupService(Cs(db));
        Assert.True(svc.Verify(), svc.Status);
        Assert.Contains("baselined", svc.Status);
        Assert.Equal(2, Q(db, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory"));
        Assert.True((int)Q(db, "SELECT COUNT(*) FROM dbo.BoardCard")! > 0);
        Assert.Equal(1, Q(db, "SELECT COUNT(*) FROM dbo.SchemaMeta"));
        Assert.True(svc.Verify(), svc.Status); // second start: nothing pending
        Assert.DoesNotContain("migrations applied", svc.Status);
    });

    [Fact] public void Empty_connection_string_is_rejected()
        => Assert.ThrowsAny<ArgumentException>(() => new CfdStartupService(""));
}