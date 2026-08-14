using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MotorPumpSuite.App.Branding;
using MotorPumpSuite.App.ViewModels;
using MotorPumpSuite.App.Views;
using MotorPumpSuite.Application.DependencyInjection;
using MotorPumpSuite.Infrastructure.DependencyInjection;
using MotorPumpSuite.Infrastructure.Persistence;
using Serilog;

namespace MotorPumpSuite.App;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
/// <remarks>
/// The base type is spelled out in full: inside the <c>MotorPumpSuite.App</c> namespace an
/// unqualified <c>Application</c> binds to the <c>MotorPumpSuite.Application</c> namespace, not to
/// the WPF type, and the build fails with CS0118.
/// </remarks>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// How long the startup screen stays up even when there is nothing left to wait for.
    /// </summary>
    /// <remarks>
    /// Without a floor, a warm start finishes before the intro animation does and the splash
    /// flickers past. This is a presentation choice, not a delay imposed on real work — the
    /// startup tasks run concurrently with it.
    /// </remarks>
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromSeconds(2.2);

    private IHost? _host;
    private IServiceScope? _shellScope;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF would otherwise close the app when the splash — the only open window — is closed.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _host = BuildHost();
            await _host.StartAsync().ConfigureAwait(true);

            var splash = _host.Services.GetRequiredService<SplashWindow>();
            splash.Show();

            var startedAt = DateTimeOffset.UtcNow;

            splash.ReportStatus("Preparing the company database…");
            _shellScope = _host.Services.CreateScope();

            var applied = await _shellScope.ServiceProvider
                .GetRequiredService<IDatabaseInitialiser>()
                .InitialiseAsync()
                .ConfigureAwait(true);

            splash.ReportStatus(applied.Count == 0
                ? "Schema is up to date."
                : $"Applied {applied.Count} migration(s).");

            var shell = _shellScope.ServiceProvider.GetRequiredService<ShellWindow>();
            var viewModel = (ShellViewModel)shell.DataContext;
            await viewModel.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            if (elapsed < MinimumSplashDuration)
            {
                await Task.Delay(MinimumSplashDuration - elapsed).ConfigureAwait(true);
            }

            MainWindow = shell;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            shell.Show();
            splash.Close();

            Log.Information("Shell window opened.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The application failed to start.");
            MessageBox.Show(
                $"The application could not start.\n\n{ex.Message}\n\nSee the log for details.",
                "Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _shellScope?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(true);
            _host.Dispose();
        }

        Log.Information("Application exited with code {ExitCode}.", e.ApplicationExitCode);
        await Log.CloseAndFlushAsync().ConfigureAwait(true);

        base.OnExit(e);
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("MOTORPUMP_");

        ConfigureLogging(builder.Configuration);
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.Services
            .AddOptions<BrandingOptions>()
            .Bind(builder.Configuration.GetSection(BrandingOptions.SectionName));

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddTransient<SplashWindow>();
        builder.Services.AddScoped<ShellViewModel>();
        builder.Services.AddScoped<ShellWindow>();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        return builder.Build();
    }

    /// <summary>
    /// Configures Serilog: console plus a rolling daily file, 31 days retained.
    /// </summary>
    /// <remarks>
    /// The sink path is built here rather than in <c>appsettings.json</c> because it has to resolve
    /// <c>%LOCALAPPDATA%</c> at run time, and a log file that silently lands somewhere else is a
    /// diagnostic tool that cannot be found when it is needed.
    /// </remarks>
    private static void ConfigureLogging(IConfiguration configuration)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MotorPumpSuite",
            "logs");

        Directory.CreateDirectory(logDirectory);

        var retainedFiles = configuration.GetValue("Logging:RetainedFileCountLimit", 31);

        // Invariant culture throughout: a log read months later during an audit must not depend on
        // the regional settings of the machine that wrote it.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(logDirectory, "log-.txt"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainedFiles,
                shared: true)
            .CreateLogger();
    }

    /// <summary>
    /// Logs an unhandled UI-thread exception before the process dies.
    /// </summary>
    /// <remarks>
    /// The exception is deliberately not swallowed. Continuing after an unhandled fault risks
    /// writing a half-posted voucher from Phase 1 onward, and a crash with a logged cause is a far
    /// better outcome than corrupt books.
    /// </remarks>
    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception on the UI thread.");
        Log.CloseAndFlush();
    }
}
