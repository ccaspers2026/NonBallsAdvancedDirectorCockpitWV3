using AdvancedDirectorCockpitWV3.Data;
using AdvancedDirectorCockpitWV3.Data.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdvancedDirectorCockpitWV3.Services;

// Startup CFD check. Order is always EF Migrate (schema) THEN dbUp seeds (data).
//   * zero user tables            -> full schema (InitialCreate + AddBoard) then all seeds.
//   * existing CFD (dbo.Loan)     -> 2026-11-02 board in SQL: if the CFD was created before EF (no
//     __EFMigrationsHistory) InitialCreate is recorded as already applied (baseline, CFD tables untouched),
//     then pending migrations (AddBoard = Board* tables only) and pending seeds (board seed only fills empty tables).
//   * anything else (not a CFD)   -> nothing written; the probe reports the failure.
public sealed class CfdStartupService
{
    public const string BaselineMigration = "20261031182843_InitialCreate";
    private readonly string _cs;

    public CfdStartupService(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _cs = connectionString;
    }

    public bool Connected { get; private set; }
    public string Status { get; private set; } = "CFD SQL: not checked";
    public string ConnectionString => _cs;

    public bool Verify()
    {
        try
        {
            var repo = new CfdProbeRepository(_cs);
            var init = "";
            if (repo.UserTableCount() == 0)
            {
                using (var db = CfdDbContext.Create(_cs)) db.Database.Migrate();
                var (ok, msg) = CfdSeeder.Run(_cs);
                init = " (empty db: schema migrated; seeds " + (ok ? msg : "FAILED - " + msg) + ")";
            }
            else if (Scalar("SELECT OBJECT_ID('dbo.Loan')") is not DBNull and not null)
            {
                var baselined = false;
                if (Scalar("SELECT OBJECT_ID('dbo.__EFMigrationsHistory')") is DBNull or null)
                {
                    Exec("CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL); "
                       + "INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'" + BaselineMigration + "', N'10.0.14');");
                    baselined = true;
                }
                int pending;
                using (var db = CfdDbContext.Create(_cs))
                {
                    pending = db.Database.GetPendingMigrations().Count();
                    if (pending > 0) db.Database.Migrate();
                }
                var (ok, msg) = CfdSeeder.Run(_cs);
                if (baselined || pending > 0 || msg != "no seeds pending")
                    init = " (" + (baselined ? "baselined; " : "") + $"migrations applied={pending}; seeds " + (ok ? msg : "FAILED - " + msg) + ")";
                if (!ok) throw new InvalidOperationException("seed failed - " + msg);
            }
            var p = repo.Probe();
            Status = $"CFD SQL: connected db={p.Database} server={p.Server} loans={p.LoanCount}" + init;
            Connected = true;
        }
        catch (Exception ex)
        {
            Status = "CFD SQL: connection FAILED - " + ex.Message;
            Connected = false;
        }
        System.Diagnostics.Debug.WriteLine(Status);
        Console.WriteLine(Status);
        return Connected;
    }

    private object? Scalar(string sql) { using var c = new SqlConnection(_cs); c.Open(); using var k = new SqlCommand(sql, c); return k.ExecuteScalar(); }
    private void Exec(string sql) { using var c = new SqlConnection(_cs); c.Open(); using var k = new SqlCommand(sql, c); k.ExecuteNonQuery(); }
}

public static class CfdServicesServiceCollectionExtensions
{
    // Data (options, DbContext, repositories) + the startup service + the board repository.
    public static IServiceCollection AddCfdServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddCfdData(config);
        services.AddSingleton(sp => new CfdStartupService(sp.GetRequiredService<IOptions<CfdOptions>>().Value.ConnectionString));
        services.AddSingleton(sp => new BoardRepository(sp.GetRequiredService<IOptions<CfdOptions>>().Value.ConnectionString));
        return services;
    }
}
