# Advanced Director Cockpit WV3

Self-contained WPF + WebView2 cockpit (.NET 10). No Python, no local proxy ports, no data folder:

- Board (cards, groups, tabs, stages, card icons) lives in SQL Server LocalDB, CFD database, `Board*` tables.
- Vault (`/adc-vault/*`) runs in-process (`AdvancedDirectorCockpitWV3\Vault\AdcVault.cs`). Store: `%LOCALAPPDATA%\AdvancedDirectorCockpitWV3\vault` unless `Vault:StoreDir` is set.
- Page (HTML/JS/CSS/assets) is the `AdvancedDirectorCockpitWV3.UI\www` project, copied to the Shell output.

## Clone and run (another developer)

### Dependencies: NuGet only

Standing rule: the solution carries everything it needs. Every library comes from NuGet via `PackageReference` and is restored by `dotnet restore` / the build. There are no checked-in DLLs, no `HintPath` or COM references, no Python, no npm, and no local proxy or helper services.

NuGet packages in use: Microsoft.Web.WebView2 (WebView2 control), Microsoft.EntityFrameworkCore.SqlServer + .Design, Microsoft.Data.SqlClient, dbup-sqlserver, Microsoft.Extensions.Hosting / Configuration.Json / Configuration.EnvironmentVariables / Options.DataAnnotations, System.Security.Cryptography.Pkcs; tests: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector.

### Host requirements (not libraries, unavoidable)

These are the operating system and runtimes the app runs on. They cannot come from NuGet:

- Windows 10/11 x64 (WPF is Windows-only).
- .NET 10 SDK, to build and run (Visual Studio 2026 includes it).
- Microsoft Edge WebView2 Runtime, which is built into Windows 11. The NuGet package supplies the control, and the runtime supplies the browser engine.
- SQL Server Express LocalDB, the database engine (Visual Studio's "Data storage and processing" workload installs it). Alternatively, set `ConnectionStrings__Cfd` to point at any SQL Server.

1. `git clone https://github.com/ccaspers2026/NonBallsAdvancedDirectorCockpitWV3.git`
2. Create a LocalDB instance and database, or point the connection string at one you have:
   `sqllocaldb create OmegaAlien_Local -s` then `sqlcmd -S "(localdb)\OmegaAlien_Local" -Q "CREATE DATABASE CFD"`
   (or set env var `ConnectionStrings__Cfd` to any SQL Server connection string).
3. `dotnet tool restore` (dotnet-ef, only needed to add migrations).
4. `dotnet build AdvancedDirectorCockpitWV3.sln` and `dotnet test AdvancedDirectorCockpitWV3.sln` (tests create and drop temp databases on the Development LocalDB server).
5. F5 `AdvancedDirectorCockpitWV3` (launch profile "(Development)").

On first start against an empty database the app runs EF `Migrate()` (schema) and then dbUp seeds (`Seeds\*.sql`: SchemaMeta, the board, the card icons). Nothing else is needed.

See `README-WV3.md` for layout, configuration and the database rules.