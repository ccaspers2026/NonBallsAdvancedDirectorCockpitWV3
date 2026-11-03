using AdvancedDirectorCockpitWV3.Data.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdvancedDirectorCockpitWV3.Data.Tests;

// 2026-10-31 appsettings refactor: ConnectionStrings:Cfd from the Shell appsettings files; no hardcoded default,
// ADC_CFD_SQL_CS no longer read.
public class CfdConnectionTests
{
    [Fact] public void Development_appsettings_targets_LocalDB_mirror_CFD_trusted()
    {
        var cs = CfdConnection.Require(TestConfig.Load("Development"));
        var b = new SqlConnectionStringBuilder(cs);
        Assert.Equal(@"(localdb)\OmegaAlien_Local", b.DataSource);
        Assert.Equal("CFD", b.InitialCatalog);
        Assert.True(b.IntegratedSecurity);
        Assert.DoesNotContain("Password", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Production_appsettings_targets_OmegaAlien_Prod_LocalDB_not_live_server()
    {
        var cs = CfdConnection.Require(TestConfig.Load("Production"));
        var b = new SqlConnectionStringBuilder(cs);
        Assert.Equal(@"(localdb)\OmegaAlien_Prod", b.DataSource);
        Assert.Equal("CFD", b.InitialCatalog);
        Assert.True(b.IntegratedSecurity);
        Assert.DoesNotContain("Password", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Base_appsettings_has_no_connection_string()
        => Assert.Null(TestConfig.Load(null).GetConnectionString(CfdConnection.Name));

    [Fact] public void Key_names_are_ConnectionStrings_Cfd()
    {
        Assert.Equal("ConnectionStrings:Cfd", CfdConnection.ConfigKey);
        Assert.Equal("ConnectionStrings__Cfd", CfdConnection.EnvVarOverride);
    }

    [Fact] public void Require_throws_clear_error_when_missing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CfdConnection.Require(new ConfigurationBuilder().Build()));
        Assert.Contains("ConnectionStrings:Cfd", ex.Message);
    }

    [Fact] public void Env_ConnectionStrings__Cfd_overrides_appsettings()
    {
        var old = Environment.GetEnvironmentVariable(CfdConnection.EnvVarOverride);
        try
        {
            Environment.SetEnvironmentVariable(CfdConnection.EnvVarOverride, "Server=x;Database=y;Trusted_Connection=True");
            Assert.Equal("Server=x;Database=y;Trusted_Connection=True", CfdConnection.Require(TestConfig.Load("Development")));
        }
        finally { Environment.SetEnvironmentVariable(CfdConnection.EnvVarOverride, old); }
    }

    [Fact] public void Deprecated_ADC_CFD_SQL_CS_is_ignored()
    {
        var old = Environment.GetEnvironmentVariable("ADC_CFD_SQL_CS");
        try
        {
            Environment.SetEnvironmentVariable("ADC_CFD_SQL_CS", "Server=legacy;Database=z");
            var cfg = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            Assert.Throws<InvalidOperationException>(() => CfdConnection.Require(cfg));
        }
        finally { Environment.SetEnvironmentVariable("ADC_CFD_SQL_CS", old); }
    }

    [Fact] public void AddCfdData_binds_options_and_resolves_context_and_repositories()
    {
        var cs = TempDb.Cs("WV3_di_check");
        using var sp = new ServiceCollection().AddCfdData(TestConfig.InMemory(cs)).BuildServiceProvider();
        Assert.Equal(cs, sp.GetRequiredService<IOptions<CfdOptions>>().Value.ConnectionString);
        using var scope = sp.CreateScope();
        Assert.Contains("WV3_di_check", scope.ServiceProvider.GetRequiredService<CfdDbContext>().Database.GetConnectionString());
        Assert.NotNull(sp.GetRequiredService<CfdProbeRepository>());
        Assert.NotNull(sp.GetRequiredService<LoanRepository>());
        Assert.NotNull(sp.GetRequiredService<ConfigKeyRepository>());
    }

    [Fact] public void AddCfdData_missing_connection_string_fails_validation()
    {
        using var sp = new ServiceCollection().AddCfdData(new ConfigurationBuilder().Build()).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<CfdOptions>>().Value);
    }

    [Fact] public void Design_time_factory_reads_Development_appsettings()
    {
        using var db = new CfdDbContextFactory().CreateDbContext([]);
        Assert.Contains("OmegaAlien_Local", db.Database.GetConnectionString());
    }

    [Fact] public void DbContext_uses_given_connection_string()
    {
        using var db = CfdDbContext.Create(TempDb.Cs("WV3_ctx_check"));
        Assert.Contains("WV3_ctx_check", db.Database.GetConnectionString());
    }

    [Fact] public void Empty_connection_string_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => CfdDbContext.Create(""));
        Assert.ThrowsAny<ArgumentException>(() => new CfdProbeRepository(" "));
        Assert.ThrowsAny<ArgumentException>(() => CfdSeeder.Run(""));
    }

    [Fact] public void Seeds_are_embedded()
    {
        var names = typeof(CfdSeeder).Assembly.GetManifestResourceNames();
        Assert.Contains(names, n => n.Contains(".Seeds.") && n.EndsWith("0001_SchemaMeta.sql"));
    }
}

// Read-only against the populated mirror (Development ConnectionStrings:Cfd): SELECTs only, nothing written.
public class LiveMirrorReadOnlyTests
{
    [Fact] public void Probe_reads_CFD_mirror()
    {
        var repo = new CfdProbeRepository(TestConfig.DevCfd);
        Assert.True(repo.UserTableCount() > 0);
        var p = repo.Probe();
        Assert.Equal("CFD", p.Database);
        Assert.True(p.LoanCount >= 0);
    }
}

// Loads the Shell project's appsettings.json + appsettings.{env}.json (same files the app uses).
public static class TestConfig
{
    public static IConfiguration Load(string? env)
    {
        var b = new ConfigurationBuilder().SetBasePath(CfdDbContextFactory.FindShellDir()).AddJsonFile("appsettings.json", optional: false);
        if (env != null) b.AddJsonFile($"appsettings.{env}.json", optional: false);
        return b.AddEnvironmentVariables().Build();
    }

    public static IConfiguration InMemory(string cs) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [CfdConnection.ConfigKey] = cs }).Build();

    public static string DevCfd => CfdConnection.Require(Load("Development"));
}

// Disposable database per test on the Development LocalDB server; never CFD.
public sealed class TempDb : IDisposable
{
    public static string Cs(string db) =>
        new SqlConnectionStringBuilder(TestConfig.DevCfd) { InitialCatalog = db, ConnectTimeout = 30 }.ConnectionString;
    public string Name { get; } = "WV3_Test_" + Guid.NewGuid().ToString("N")[..12];
    public string ConnectionString => Cs(Name);
    public TempDb() => Exec($"CREATE DATABASE [{Name}]");
    public void Dispose() => Exec($"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]");
    static void Exec(string sql)
    {
        SqlConnection.ClearAllPools();
        using var c = new SqlConnection(Cs("master"));
        c.Open();
        using var k = new SqlCommand(sql, c);
        k.ExecuteNonQuery();
    }
    public object? Scalar(string sql)
    {
        using var c = new SqlConnection(ConnectionString);
        c.Open();
        using var k = new SqlCommand(sql, c);
        return k.ExecuteScalar();
    }
}