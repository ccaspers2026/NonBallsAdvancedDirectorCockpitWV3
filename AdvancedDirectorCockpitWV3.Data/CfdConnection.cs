using System.ComponentModel.DataAnnotations;
using AdvancedDirectorCockpitWV3.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdvancedDirectorCockpitWV3.Data;

// 2026-10-31 appsettings refactor: the CFD connection string comes ONLY from configuration
// ConnectionStrings:Cfd (Shell appsettings.{Environment}.json, or env var ConnectionStrings__Cfd).
// There is no hardcoded default any more. ADC_CFD_SQL_CS is DEPRECATED and is no longer read.
public static class CfdConnection
{
    public const string Name = "Cfd";
    public const string ConfigKey = "ConnectionStrings:" + Name;
    public const string EnvVarOverride = "ConnectionStrings__" + Name;

    [Obsolete("Deprecated 2026-10-31; not read. Use ConnectionStrings:Cfd (appsettings) or env ConnectionStrings__Cfd.")]
    public const string LegacyEnvVar = "ADC_CFD_SQL_CS";

    public static string Require(IConfiguration config) =>
        config.GetConnectionString(Name) is { Length: > 0 } cs
            ? cs
            : throw new InvalidOperationException(
                $"Missing {ConfigKey}. Set it in appsettings.{{Environment}}.json or env var {EnvVarOverride}.");
}

// Bound from ConnectionStrings:Cfd.
public sealed class CfdOptions
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "ConnectionStrings:Cfd is required (appsettings.Development.json / appsettings.Production.json or env ConnectionStrings__Cfd).")]
    public string ConnectionString { get; set; } = "";
}

public static class CfdDataServiceCollectionExtensions
{
    // Registers CfdOptions (validated at host start), CfdDbContext and the repositories.
    public static IServiceCollection AddCfdData(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CfdOptions>()
            .Configure(o => o.ConnectionString = config.GetConnectionString(CfdConnection.Name) ?? "")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddDbContext<CfdDbContext>((sp, b) => b.UseSqlServer(Cs(sp)));
        services.AddTransient(sp => new CfdProbeRepository(Cs(sp)));
        services.AddTransient(sp => new LoanRepository(Cs(sp)));
        services.AddTransient(sp => new ConfigKeyRepository(Cs(sp)));
        return services;
    }

    private static string Cs(IServiceProvider sp) => sp.GetRequiredService<IOptions<CfdOptions>>().Value.ConnectionString;
}