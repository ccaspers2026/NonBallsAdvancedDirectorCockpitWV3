# Advanced Director Cockpit WV3 (v3.0.0)
Copied from WV2 on 2026-10-31. Open AdvancedDirectorCockpitWV3.sln, startup = AdvancedDirectorCockpitWV3 (Shell), F5.

## Data (2026-11-02: self-contained)
- Board (cards, groups, tabs, stages, card icons): CFD database, tables BoardCard, BoardGroup, BoardTab, BoardStageLayout, BoardSetting, BoardIcon (EF migration AddBoard; seed Seeds\0002_BoardSeed.sql + 0003_BoardIcons.sql, generated from the last canonical data\*.json, deduplicated). The page still requests data/cards.json, data/groups.json, data/tabs.json, data/stages.json and data/icons/<name>; the Shell answers them from SQL (MainWindow.BoardData.cs -> BoardRepository). POST /adc-data saves to SQL in one transaction (rows deduplicated by id). There is no data folder and no Cockpit:DataDir.
- Vault: in-process AdcVault; store %LOCALAPPDATA%\AdvancedDirectorCockpitWV3\vault (Vault:StoreDir to override). Vault:ImportFrom = optional one-time copy of an older vault.dat.
- LocalAppData profile: %LOCALAPPDATA%\AdvancedDirectorCockpitWV3

## Companions (external, not VS projects)
- CFD: https://cfd.local:8080/ (Launch Pad card)
- None required to run the cockpit. CFD (and its own settings server) is just a card the cockpit opens. The old Python vault proxy (127.0.0.1:8791, adc_vault.py) is retired: the vault is in-process since 2026-11-02. Virtual host stays adc-wv2.local so saved vault origins still match.

## Solution layout (2026-10-31 layered reorg, no behavior change)
- AdvancedDirectorCockpitWV3\            Shell (WPF WinExe + WebView2). STARTUP. App, MainWindow*.cs, EmbeddedApps, Vault, CfdSql facade. Refs UI + Services.
- AdvancedDirectorCockpitWV3.UI\         UI: www\ (HTML/JS/CSS/assets). Content copied to Shell output www\. In Source Debug the Shell serves UI\www live from source (found next to the .sln).
- AdvancedDirectorCockpitWV3.Services\   Services: CfdStartupService (startup verify). Refs Data.
- AdvancedDirectorCockpitWV3.Data\       Data: EF Core SQL Server CfdDbContext (5 CFD + 6 Board entities), Migrations\ (InitialCreate = existing CFD schema, AddBoard = board tables), Repositories\, CfdSeeder (dbUp), Seeds\*.sql.
- dotnet-tools.json                      local dotnet-ef 10.0.14 (`dotnet tool restore`).

## CFD SQL
- Connection string: appsettings `ConnectionStrings:Cfd` (see Configuration below). No hardcoded default. `ADC_CFD_SQL_CS` is DEPRECATED and no longer read (machine/user env vars were not removed; remove them by hand if set).
- Startup: App.OnStartup -> generic host (DI) -> CfdSql.VerifyOnStartup -> CfdStartupService (DI singleton).Verify -> CfdProbeRepository (SELECT DB_NAME(), @@SERVERNAME, loan count). Failure = warning dialog; title [CFD local]/[CFD offline].
- EF = schema only. dbUp = seed data only (journal dbo.SchemaVersions_Seed). Order is always EF Migrate THEN dbUp.
- Empty database (zero user tables): Database.Migrate() (InitialCreate + AddBoard), then dbUp runs Seeds\*.sql (idempotent). Never drops or wipes.
- Existing CFD (has dbo.Loan; 2026-11-02 board in SQL): if it has no __EFMigrationsHistory (built outside EF, e.g. OmegaAlien_Local / OmegaAlien_Prod) the history table is created with InitialCreate recorded as applied (baseline; the 5 CFD tables are not touched). Then pending migrations (AddBoard = Board* tables only) and pending seeds run. The board seed fills a Board table only when it is empty; after that the page's saves own the rows.
- Any other non-empty database (no dbo.Loan): nothing written; the probe reports the failure.
- New migration: `dotnet ef migrations add <Name> --project AdvancedDirectorCockpitWV3.Data --output-dir Migrations`
## Configuration (appsettings, 2026-10-31)
Standard .NET generic host (`Host.CreateApplicationBuilder`, ContentRoot = exe folder). Sources, last wins:
`appsettings.json` -> `appsettings.{DOTNET_ENVIRONMENT}.json` -> environment variables. Command-line args are not a config source.
Files live in the Shell project (`AdvancedDirectorCockpitWV3\`) and are copied to bin\ and publish\ (PreserveNewest).

| File | Environment | Keys |
|---|---|---|
| appsettings.json | all (shared, no secrets) | Cockpit:VirtualHost, Cockpit:MaxSlots, Cockpit:PubExePath, Vault:StoreDir, Vault:ImportFrom, Vault:MaxBodyBytes, Logging |
| appsettings.Development.json | Development (F5) | ConnectionStrings:Cfd = (localdb)\OmegaAlien_Local / CFD |
| appsettings.Production.json | Production (Pub exe; DOTNET_ENVIRONMENT unset) | ConnectionStrings:Cfd = (localdb)\OmegaAlien_Prod / CFD (LocalDB mirror, NOT the live MSSQLSERVER CFD) |
| Properties\launchSettings.json | VS profiles | "(Development)" sets DOTNET_ENVIRONMENT=Development (default F5); "(Production config)" for testing Prod settings |

- Options: CockpitOptions / VaultOptions (Shell, AdcSettings.cs), CfdOptions (Data, CfdConnection.cs); all `ValidateDataAnnotations().ValidateOnStart()`. A bad/missing value = "Configuration error" dialog at start.
- Env override examples: `ConnectionStrings__Cfd`, `Vault__StoreDir`.
- DI: `AddAdcCockpit(config)` (Shell), `AddCfdServices(config)` (Services) -> `AddCfdData(config)` (Data: CfdOptions, CfdDbContext, repositories).
- Production note: the Pub exe runs as Production -> OmegaAlien_Prod LocalDB. On first Pub start the existing OmegaAlien_Prod CFD is baselined and gets the Board* tables + board seed (see CFD SQL above); an empty database is fully migrated + seeded.
- Design time (`dotnet ef`): CfdDbContextFactory reads the Shell appsettings.json + appsettings.{DOTNET_ENVIRONMENT or Development}.json + env vars. No hardcoded fallback.
- Tests read the same Shell appsettings files (Development) or in-memory config; temp databases go on the Development LocalDB server.
- Follow-ups (not in this pass): UI JS still hardcodes its own values (no /adc-config endpoint yet); favicon service URL and Bambu exe/scheme names are still constants in the Shell.