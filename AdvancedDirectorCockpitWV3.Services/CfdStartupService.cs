using AdvancedDirectorCockpitWV3.Data;
using AdvancedDirectorCockpitWV3.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdvancedDirectorCockpitWV3.Services;

// Startup CFD check. Behavior on an existing CFD (today's OmegaAlien_Local) = read-only probe, nothing written.
// Only when the database has zero user tables does it apply EF migrations (schema) and then dbUp seeds (data).
// 2026-10-31: instance service; connection string from DI (CfdOptions <- ConnectionStrings:Cfd).
public sealed class CfdStartupService
{
    private readonly string _cs;

    public CfdStartupService(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _cs = connectionString;
    }

    public bool Connected { get; private set; }
    public string Status { get; private set; } = "CFD SQL: not checked";

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
}

public static class CfdServicesServiceCollectionExtensions
{
    // Data (options, DbContext, repositories) + the startup service.
    public static IServiceCollection AddCfdServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddCfdData(config);
        services.AddSingleton(sp => new CfdStartupService(sp.GetRequiredService<IOptions<CfdOptions>>().Value.ConnectionString));
        return services;
    }
}