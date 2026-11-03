using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using System.Windows.Shell;
using AdvancedDirectorCockpitWV3.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AdvancedDirectorCockpitWV3.App;

// 2026-10-09 multi-window: ONE cockpit process per Windows user. Starting the exe again does not start a
// second process (that would restore the same panes and fight over the same embedded app windows):
//   AdvancedDirectorCockpitWV3.exe               -> the running cockpit comes to the front
//   AdvancedDirectorCockpitWV3.exe --new-window  -> the running cockpit opens another window
//   AdvancedDirectorCockpitWV3.exe --multi-instance -> old behaviour: a separate process (testing only)
// The running cockpit listens on a per-user named pipe. All windows live in this one process and share the
// WebView2 profile, the proxy session and the embedded-app ownership.
public partial class App : Application
{
    private static Mutex? _instanceMutex;
    private static string Suffix => Environment.UserName.Replace('\\', '_');
    private static string MutexName => @"Local\AdvancedDirectorCockpitWV3-" + Suffix;
    internal static string PipeName => "AdvancedDirectorCockpitWV3-" + Suffix;

    public App()
    {
        // Closing one Open-group child window must not shut down the whole cockpit.
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var newWindow = e.Args.Any(a => string.Equals(a, "--new-window", StringComparison.OrdinalIgnoreCase));
        var multi = e.Args.Any(a => string.Equals(a, "--multi-instance", StringComparison.OrdinalIgnoreCase));
        if (!multi)
        {
            _instanceMutex = new Mutex(true, MutexName, out var first);
            if (!first)
            {
                if (SendToRunning(newWindow ? "new-window" : "focus"))
                {
                    Shutdown();
                    return;
                }
                // The running cockpit did not answer within 3 s: start anyway rather than show nothing.
            }
            else
            {
                StartListening();
            }
        }
        if (!StartHost()) return;
        SetJumpList();
        CfdSql.VerifyOnStartup();
        global::AdvancedDirectorCockpitWV3.Windows.MainWindow.StartWindows();
    }

    // 2026-10-31 appsettings: generic host = configuration (appsettings.json + appsettings.{DOTNET_ENVIRONMENT}.json
    // + env vars), IOptions validation, DI. Command-line args are NOT added (--new-window has no value).
    internal static IHost? AppHost { get; private set; }

    private bool StartHost()
    {
        try
        {
            var b = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
            b.Services.AddAdcCockpit(b.Configuration);
            b.Services.AddCfdServices(b.Configuration);
            AppHost = b.Build();
            AppHost.Start();
            AdcSettings.Cockpit = AppHost.Services.GetRequiredService<IOptions<CockpitOptions>>().Value;
            AdcSettings.Vault = AppHost.Services.GetRequiredService<IOptions<VaultOptions>>().Value;
            CfdSql.Service = AppHost.Services.GetRequiredService<CfdStartupService>();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Configuration error (appsettings):\n\n" + ex.Message,
                "Advanced Director Cockpit WV3 - configuration", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppHost?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            AppHost?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }

    private static bool SendToRunning(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var w = new StreamWriter(client);
            w.WriteLine(command);
            w.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartListening()
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    server.WaitForConnection();
                    using var r = new StreamReader(server);
                    var cmd = (r.ReadLine() ?? "").Trim();
                    Dispatcher.BeginInvoke(() => global::AdvancedDirectorCockpitWV3.Windows.MainWindow.HandleInstanceCommand(cmd));
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        }) { IsBackground = true, Name = "ADC single-instance pipe" };
        t.Start();
    }

    // Taskbar / Start jump list: "Advanced Director Cockpit - New Window".
    private void SetJumpList()
    {
        try
        {
            var exe = Environment.ProcessPath ?? "";
            var list = new JumpList();
            list.JumpItems.Add(new JumpTask
            {
                Title = "Advanced Director Cockpit - New Window",
                Description = "Open another cockpit window (for another monitor)",
                ApplicationPath = exe,
                Arguments = "--new-window",
                IconResourcePath = exe,
                IconResourceIndex = 0
            });
            list.ShowFrequentCategory = false;
            list.ShowRecentCategory = false;
            JumpList.SetJumpList(this, list);
            list.Apply();
        }
        catch
        {
            // Jump list is optional.
        }
    }
}
