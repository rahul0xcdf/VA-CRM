using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VinayakaAgencies.App.Branding;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.App.Views;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Maintenance;
using VinayakaAgencies.Application.Security;
using VinayakaAgencies.Application.DependencyInjection;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Infrastructure.Configuration;
using VinayakaAgencies.Infrastructure.DependencyInjection;
using VinayakaAgencies.Infrastructure.Persistence;
using Serilog;

namespace VinayakaAgencies.App;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
/// <remarks>
/// The base type is spelled out in full: inside the <c>VinayakaAgencies.App</c> namespace an
/// unqualified <c>Application</c> binds to the <c>VinayakaAgencies.Application</c> namespace, not to
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

            // Phase 10 §4a D7, and it has to happen here — before anything opens the database.
            // Phases 0–9 kept the books under %LOCALAPPDATA%, which is per Windows user; they now
            // live machine-wide. Without this move an upgraded install would find no database at
            // the new location, and a missing database is indistinguishable from a fresh install:
            // the owner would be walked through first-run company creation with their real books
            // sitting untouched in a folder nothing reads any more.
            splash.ReportStatus("Checking where the company data lives…");
            RelocateCompanyDataIfNeeded();

            splash.ReportStatus("Preparing the company database…");
            _shellScope = _host.Services.CreateScope();

            var initialiser = _shellScope.ServiceProvider.GetRequiredService<IDatabaseInitialiser>();

            // Honours Database:ApplyMigrationsOnStartup, which defaults to off from Phase 9. On a
            // single-user install that is switched on it migrates here exactly as before.
            var applied = await initialiser.InitialiseAsync().ConfigureAwait(true);

            // Phase 9 §2: with two or three clients, a stale schema is not something to fix quietly
            // while they are all launching. If anything is still pending the upgrade becomes one
            // operator's deliberate act, and the books stay shut until it has been done — running
            // against a schema this build does not match is how a missing column becomes a crash
            // halfway through billing.
            var pending = await initialiser.GetPendingMigrationsAsync().ConfigureAwait(true);

            if (pending.Count > 0)
            {
                splash.ReportStatus("A database upgrade is required…");

                var upgradeViewModel = new SchemaUpgradeViewModel(initialiser, pending);
                var upgradeWindow = new SchemaUpgradeWindow { DataContext = upgradeViewModel };

                splash.Hide();
                upgradeWindow.ShowDialog();
                splash.Show();

                if (!upgradeViewModel.IsUpToDate)
                {
                    splash.Close();
                    Shutdown();
                    return;
                }

                applied = pending;
            }

            splash.ReportStatus(applied.Count == 0
                ? "Schema is up to date."
                : $"Applied {applied.Count} migration(s).");

            // Phase 2: fill any missing inventory seed rows (Stock Journal type + series, the main
            // godown, the base unit, settings). Row-granular and idempotent, so this is safe on
            // every start and writes nothing once present.
            splash.ReportStatus("Preparing inventory masters…");
            var provisioning = _shellScope.ServiceProvider.GetRequiredService<ICompanyProvisioningService>();

            // A company is never defaulted into existence (spec §10): its legal name and state code
            // end up on filed returns. On a brand-new install the only honest move is to ask.
            if (await _shellScope.ServiceProvider
                    .GetRequiredService<ICompanyService>()
                    .CountAsync()
                    .ConfigureAwait(true) == 0)
            {
                splash.ReportStatus("Waiting for company details…");

                var firstRunViewModel = new FirstRunCompanyViewModel(
                    _shellScope.ServiceProvider.GetRequiredService<ICompanyService>(),
                    provisioning,
                    _shellScope.ServiceProvider.GetRequiredService<ISignInService>());
                var firstRunWindow = new FirstRunCompanyWindow { DataContext = firstRunViewModel };
                firstRunWindow.ShowDialog();

                if (!firstRunViewModel.CreatedCompanyId.HasValue)
                {
                    Log.Information("First-run company creation was dismissed; exiting cleanly.");
                    Shutdown(0);
                    return;
                }
            }

            var companyId = await _shellScope.ServiceProvider
                .GetRequiredService<ICompanyContext>()
                .EnsureCompanyAsync()
                .ConfigureAwait(true);
            await provisioning.EnsureInventorySeedAsync(companyId).ConfigureAwait(true);

            // Phase 3: voucher types/series per document kind, the COGS ledger and the default
            // print templates. Idempotent like its inventory sibling, so it runs on every start
            // and fills only what earlier installs are missing.
            splash.ReportStatus("Preparing sales & purchase masters…");
            await provisioning.EnsurePipelineSeedAsync(companyId).ConfigureAwait(true);

            // Phase 4: the four customer categories and the sales settings row. Idempotent like
            // its siblings, so it runs on every start and fills only what earlier installs miss.
            splash.ReportStatus("Preparing customer masters…");
            await provisioning.EnsureCustomerSeedAsync(companyId).ConfigureAwait(true);

            // Phase 5: the output/input GST ledgers and the settings row naming the round-off
            // ledger. Phase 5 delivered this seed but never called it from startup, so an install
            // that had not opened the GST settings screen had no ledgers for a tax line to post
            // to — fixed here rather than left for the first invoice to discover.
            splash.ReportStatus("Preparing GST masters…");
            await provisioning.EnsureGstSeedAsync(companyId).ConfigureAwait(true);

            // Phase 7: the TDS and TCS payable ledgers and the settings row naming them. No
            // section, rate or threshold is seeded — those are the owner's master data
            // (phase-7-tds-payroll-banking.md §2, checkpoints C7.2 and C7.5).
            splash.ReportStatus("Preparing TDS masters…");
            await provisioning.EnsureTdsSeedAsync(companyId).ConfigureAwait(true);

            // Phase 7b: the salary and statutory ledgers, and the payroll settings row. All three
            // statutory flags start off, so this costs a row and no behaviour until the owner
            // declares a scheme applies (checkpoint C7.7).
            splash.ReportStatus("Preparing payroll masters…");
            await provisioning.EnsurePayrollSeedAsync(companyId).ConfigureAwait(true);

            // Phase 9: the Owner and Operator roles (§4a D4). Roles only — no account is seeded,
            // because a shipped user name with a default password is a hole, not a convenience.
            // Until the first account exists every permission is granted, so this changes nothing
            // for an install that has not been secured yet.
            splash.ReportStatus("Preparing user roles…");
            await provisioning.EnsureSecuritySeedAsync(companyId).ConfigureAwait(true);

            // Phase 9 §4a D2: the books open under a name. Non-negotiable #3 requires every change
            // to be attributable, and a workgroup shop shares one Windows login per machine — so
            // the identity has to come from an application account.
            //
            // Skipped when first-run has just created the owner account, which signs them in as it
            // creates them: asking again immediately would read as the account not having been
            // made. On an existing install this is where the account is asked for, and declining
            // closes the product rather than opening the books unattributed.
            if (!_shellScope.ServiceProvider.GetRequiredService<IUserSession>().IsSignedIn)
            {
                splash.ReportStatus("Waiting for sign-in…");

                var loginViewModel = new LoginViewModel(
                    _shellScope.ServiceProvider.GetRequiredService<ISignInService>(),
                    companyId);

                await loginViewModel.LoadCommand.ExecuteAsync(null).ConfigureAwait(true);

                var loginWindow = new LoginWindow { DataContext = loginViewModel };

                splash.Hide();
                loginWindow.ShowDialog();
                splash.Show();

                if (!loginViewModel.IsSignedIn)
                {
                    splash.Close();
                    Shutdown();
                    return;
                }
            }

            var shell = _shellScope.ServiceProvider.GetRequiredService<ShellWindow>();

            // Exercise both database paths at startup exactly as the Phase 0 shell did (C0.7): the
            // diagnostics view model reads the company count through EF Core and through Dapper,
            // and both must agree before the user sees a screen.
            await new DiagnosticsViewModel(
                _shellScope.ServiceProvider.GetRequiredService<ICompanyService>(),
                _shellScope.ServiceProvider.GetRequiredService<ISqlQueryExecutor>())
                .RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);

            var elapsed = DateTimeOffset.UtcNow - startedAt;

            // ADR-0005 header: company legal name, GSTIN and active financial-year period.
            splash.ReportStatus("Loading company header…");
            await _shellScope.ServiceProvider.GetRequiredService<ShellViewModel>()
                .LoadCompanyHeaderAsync()
                .ConfigureAwait(true);
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

    /// <summary>
    /// Moves a pre-Phase-10 install's company data to the machine-wide location, once.
    /// </summary>
    /// <remarks>
    /// Resolved from the host rather than constructed, so the relocator and the SQLite resolver are
    /// looking at the same <see cref="IAppDataPaths"/> and the same configured file name. An
    /// ambiguous outcome — a database at both locations — is logged as a warning and the startup
    /// continues against the machine-wide copy, which is what <c>LegacyDataRelocator</c> leaves in
    /// place; it is not this method's business to choose between two sets of books.
    /// </remarks>
    private void RelocateCompanyDataIfNeeded()
    {
        if (_host is null)
        {
            return;
        }

        var options = _host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>()
            .Value;

        // A deployment that pinned an explicit connection string has chosen its own location, and
        // moving files out from under it would be the opposite of helpful.
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Log.Information(
                "Database:ConnectionString is set explicitly, so the Phase 10 data relocation is skipped.");
            return;
        }

        var relocation = _host.Services
            .GetRequiredService<LegacyDataRelocator>()
            .Relocate(string.IsNullOrWhiteSpace(options.DatabaseFileName)
                ? "motorpump.db"
                : options.DatabaseFileName);

        switch (relocation.Outcome)
        {
            case LegacyDataRelocationOutcome.Relocated:
                Log.Information(
                    "Company data relocated: {Explanation} Entries: {Entries}",
                    relocation.Explanation,
                    string.Join(", ", relocation.MovedEntries));
                break;

            case LegacyDataRelocationOutcome.AmbiguousLeftAlone:
                Log.Warning("Company data was not relocated. {Explanation}", relocation.Explanation);
                break;

            default:
                Log.Debug("No company data relocation needed. {Explanation}", relocation.Explanation);
                break;
        }
    }

    /// <summary>
    /// Writes a crash report beside the logs, for the next diagnostics bundle to carry.
    /// </summary>
    /// <remarks>
    /// Synchronous by necessity: every caller is a last-chance handler on a process that is about
    /// to die, and an awaited continuation would not be reached. Failures are swallowed and logged
    /// — a fault while recording a fault must not replace the original in the log.
    /// </remarks>
    private void RecordCrash(string source, Exception exception)
    {
        try
        {
            if (_host is null)
            {
                return;
            }

            using var scope = _host.Services.CreateScope();

            var path = scope.ServiceProvider
                .GetRequiredService<IDiagnosticsBundleService>()
                .RecordCrashAsync(source, exception)
                .GetAwaiter()
                .GetResult();

            Log.Information("Crash report written to {Path}.", path);
        }
        catch (Exception recordingFailure) when (recordingFailure is not OutOfMemoryException)
        {
            Log.Error(recordingFailure, "Could not write a crash report for the {Source} fault.", source);
        }
    }

    /// <summary>
    /// Takes the shutdown backup of §4a D1, if it is switched on.
    /// </summary>
    /// <remarks>
    /// Clean shutdown is the one moment the books are known to be quiet and the operator known to
    /// be finished, which is why it is the default. It is best-effort: a failure here is logged and
    /// the application still closes, because refusing to exit would be a worse bug than a missed
    /// backup, and the next launch will say how old the newest archive is.
    /// </remarks>
    private void BackupOnShutdownIfConfigured()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            using var scope = _host.Services.CreateScope();

            if (!scope.ServiceProvider.GetRequiredService<BackupOptions>().BackupOnShutdown)
            {
                return;
            }

            var summary = scope.ServiceProvider
                .GetRequiredService<IBackupService>()
                .CreateAsync("on shutdown")
                .GetAwaiter()
                .GetResult();

            Log.Information(
                "Shutdown backup written to {Path} (verified: {Verified}).",
                summary.FilePath,
                summary.IsVerified);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ObjectDisposedException)
        {
            Log.Warning(ex, "The shutdown backup did not complete. The application is still closing.");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Before the scope goes: the backup needs a working service provider.
        BackupOnShutdownIfConfigured();

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

        // Three handlers, because there are three ways an unhandled fault reaches the top and only
        // one of them was covered before Phase 10. The UI thread is the common case; a background
        // thread bypasses the dispatcher entirely; and a faulted Task nobody awaited surfaces later
        // still, at a garbage collection, with no stack that points anywhere useful unless it is
        // captured here.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
        // Through IAppDataPaths from Phase 10, rather than building the path here a second time.
        // Phases 0–9 derived it independently in two places — here and in the SQLite resolver — and
        // §4a D7 then moved one of them. Two derivations of the same location is how a diagnostics
        // bundle ends up collecting logs from a directory nothing writes to.
        var logDirectory = new AppDataPaths().LogDirectory;

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
    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception on the UI thread.");
        RecordCrash("UI thread", e.Exception);
        Log.CloseAndFlush();
    }

    /// <summary>
    /// A fault on a thread the dispatcher does not own. The process is already going down.
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            Log.Fatal("A non-exception object was thrown and left unhandled: {Object}", e.ExceptionObject);
            Log.CloseAndFlush();
            return;
        }

        Log.Fatal(exception, "Unhandled exception on a background thread. Terminating: {IsTerminating}", e.IsTerminating);
        RecordCrash("background thread", exception);
        Log.CloseAndFlush();
    }

    /// <summary>
    /// A faulted <see cref="Task"/> nobody awaited.
    /// </summary>
    /// <remarks>
    /// Observed and logged, then marked observed so it does not also take the process down. This is
    /// the one unhandled fault that is <i>not</i> fatal by default, and swallowing it silently is
    /// how a failed background save becomes a mystery. Deliberately different from the two handlers
    /// above, which let the process die: nothing here is known to be mid-write.
    /// </remarks>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "A background task faulted and nobody observed it.");
        RecordCrash("unobserved task", e.Exception);

        e.SetObserved();
    }
}
