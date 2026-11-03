# Advanced Director Cockpit WV3 (v3.0.0)
Copied from WV2 on 2026-10-31. Open AdvancedDirectorCockpitWV3.sln, startup = AdvancedDirectorCockpitWV3 (Shell), F5.

## Data (WV3-owned copies; WV2 live data untouched)
- Data dir (appsettings Cockpit:DataDir): V:\_Data\CaspersCreations\Applications\Data\Advanced Director Cockpit WV3\data
- Vault folder copy: V:\_Data\CaspersCreations\Applications\Data\Advanced Director Cockpit WV3\vault
- LocalAppData profile: %LOCALAPPDATA%\AdvancedDirectorCockpitWV3

## Companions (external, not VS projects)
- CFD: https://cfd.local:8080/ (Launch Pad card)
- CFD settings server: :8792 (Python, CFD\v2\settings-server)
- Vault frame proxy: http://127.0.0.1:8791 (adc_vault.py) - shared with WV2; virtual host kept as adc-wv2.local so vault origin checks still pass
- Optional NonBalls Local API :8793 (ADC DB fork only)

## Solution layout (2026-10-31 layered reorg, no behavior change)
- AdvancedDirectorCockpitWV3\            Shell (WPF WinExe + WebView2). STARTUP. App, MainWindow*.cs, EmbeddedApps, Vault, CfdSql facade. Refs UI + Services.
- AdvancedDirectorCockpitWV3.UI\         UI: www\ (HTML/JS/CSS/assets). Content copied to Shell output www\. In Source Debug the Shell serves UI\www live from source (found next to the .sln).
- AdvancedDirectorCockpitWV3.Services\   Services: CfdStartupService (startup verify). Refs Data.
- AdvancedDirectorCockpitWV3.Data\       Data: EF Core SQL Server CfdDbContext (5 entities), Migrations\ (InitialCreate = existing CFD schema), Repositories\, CfdSeeder (dbUp), Seeds\*.sql.
- dotnet-tools.json                      local dotnet-ef 10.0.14 (`dotnet tool restore`).

## CFD SQL
- Connection string: appsettings `ConnectionStrings:Cfd` (see Configuration below). No hardcoded default. `ADC_CFD_SQL_CS` is DEPRECATED and no longer read (machine/user env vars were not removed; remove them by hand if set).
- Startup: App.OnStartup -> generic host (DI) -> CfdSql.VerifyOnStartup -> CfdStartupService (DI singleton).Verify -> CfdProbeRepository (SELECT DB_NAME(), @@SERVERNAME, loan count). Failure = warning dialog; title [CFD local]/[CFD offline].
- EF = schema only. dbUp = seed data only (journal dbo.SchemaVersions_Seed).
- Existing database (any user tables): NOTHING is migrated or seeded; read-only probe only. The current OmegaAlien_Local CFD was built outside EF, so it has no __EFMigrationsHistory; never call Migrate() against it unless baselined first.
- Empty database (zero user tables): Database.Migrate() creates the 5 tables, then dbUp runs Seeds\*.sql (idempotent). Never drops or wipes.
- New migration: `dotnet ef migrations add <Name> --project AdvancedDirectorCockpitWV3.Data --output-dir Migrations`
## Configuration (appsettings, 2026-10-31)
Standard .NET generic host (`Host.CreateApplicationBuilder`, ContentRoot = exe folder). Sources, last wins:
`appsettings.json` -> `appsettings.{DOTNET_ENVIRONMENT}.json` -> environment variables. Command-line args are not a config source.
Files live in the Shell project (`AdvancedDirectorCockpitWV3\`) and are copied to bin\ and publish\ (PreserveNewest).

| File | Environment | Keys |
|---|---|---|
| appsettings.json | all (shared, no secrets) | Cockpit:VirtualHost, Cockpit:MaxSlots, Cockpit:PubExePath, Vault:ProxyBaseUrl, Vault:TimeoutSeconds, Vault:MaxBodyBytes, Logging |
| appsettings.Development.json | Development (F5) | ConnectionStrings:Cfd = (localdb)\OmegaAlien_Local / CFD, Cockpit:DataDir |
| appsettings.Production.json | Production (Pub exe; DOTNET_ENVIRONMENT unset) | ConnectionStrings:Cfd = (localdb)\OmegaAlien_Prod / CFD (LocalDB mirror, NOT the live MSSQLSERVER CFD), Cockpit:DataDir |
| Properties\launchSettings.json | VS profiles | "(Development)" sets DOTNET_ENVIRONMENT=Development (default F5); "(Production config)" for testing Prod settings |

- Options: CockpitOptions / VaultOptions (Shell, AdcSettings.cs), CfdOptions (Data, CfdConnection.cs); all `ValidateDataAnnotations().ValidateOnStart()`. A bad/missing value = "Configuration error" dialog at start.
- Env override examples: `ConnectionStrings__Cfd`, `Cockpit__DataDir`, `Vault__ProxyBaseUrl`.
- DI: `AddAdcCockpit(config)` (Shell), `AddCfdServices(config)` (Services) -> `AddCfdData(config)` (Data: CfdOptions, CfdDbContext, repositories).
- Production note: the Pub exe runs as Production -> OmegaAlien_Prod LocalDB. If that CFD database has ZERO user tables the empty-db gate will migrate + seed it on first start (same rule as before). Confirm OmegaAlien_Prod CFD is populated (or intended to be initialised) before first Pub run.
- Design time (`dotnet ef`): CfdDbContextFactory reads the Shell appsettings.json + appsettings.{DOTNET_ENVIRONMENT or Development}.json + env vars. No hardcoded fallback.
- Tests read the same Shell appsettings files (Development) or in-memory config; temp databases go on the Development LocalDB server.
- Follow-ups (not in this pass): UI JS still hardcodes its own values (no /adc-config endpoint yet); favicon service URL and Bambu exe/scheme names are still constants in the Shell.