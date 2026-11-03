using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvancedDirectorCockpitWV3.Configuration;

// 2026-10-31 appsettings refactor. Bound from appsettings.json + appsettings.{Environment}.json (+ env vars).
// Section "Cockpit".
public sealed class CockpitOptions
{
    public const string Section = "Cockpit";
    [Required] public string VirtualHost { get; set; } = "adc-wv2.local";
    [Range(1, 64)] public int MaxSlots { get; set; } = 16;
    // Single data-folder setting. Every instance reads and saves cards/groups/tabs/stages here.
    [Required] public string DataDir { get; set; } = "";
    [Required] public string PubExePath { get; set; } = "";
}

// Section "Vault": the local frame proxy (adc_vault.py).
public sealed class VaultOptions
{
    public const string Section = "Vault";
    [Required, Url] public string ProxyBaseUrl { get; set; } = "http://127.0.0.1:8791";
    [Range(1, 300)] public int TimeoutSeconds { get; set; } = 20;
    [Range(1024, 1048576)] public int MaxBodyBytes { get; set; } = 16384;
}

// Values are copied once from the host in App.OnStartup (least-invasive bridge to the static MainWindow code).
internal static class AdcSettings
{
    public static CockpitOptions Cockpit { get; set; } = new();
    public static VaultOptions Vault { get; set; } = new();

    public static IServiceCollection AddAdcCockpit(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CockpitOptions>().Bind(config.GetSection(CockpitOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<VaultOptions>().Bind(config.GetSection(VaultOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        return services;
    }
}