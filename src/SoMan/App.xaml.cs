using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SoMan.Data;
using SoMan.Services.Browser;
using SoMan.Services.Config;
using SoMan.Services.Delay;
using SoMan.Services.Logging;
using SoMan.Services.Recovery;
using SoMan.Services.Account;
using SoMan.Services.Proxy;
using SoMan.Services.Security;
using SoMan.Platforms.Threads;
using SoMan.Services.Template;
using SoMan.Services.Execution;
using SoMan.Services.Scheduler;
using SoMan.Services.Theming;
using SoMan.ViewModels;

namespace SoMan;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "SoMan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        // Ensure database is created and migrated
        using (var db = new SoManDbContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Apply saved theme (Dark/Light) before any window is shown.
        try { await _serviceProvider.GetRequiredService<IThemeService>().ApplyStartupThemeAsync(); }
        catch { /* fall back to the BundledTheme declared in App.xaml */ }

        // Start Quartz scheduler and register all enabled ScheduledTasks.
        var scheduler = _serviceProvider.GetRequiredService<ISchedulerService>();
        try { await scheduler.StartAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Scheduler failed to start: {ex.Message}\nScheduled tasks will not run this session.",
                "SoMan Scheduler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var mainWindow = new MainWindow
        {
            DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
        };
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database — services create their own DbContext per operation

        // Services
        services.AddSingleton<IEncryptionService, EncryptionService>();
        services.AddSingleton<IResourceMonitor, ResourceMonitor>();
        services.AddSingleton<IBrowserManager, BrowserManager>();
        services.AddTransient<ISessionValidator, SessionValidator>();
        services.AddTransient<IDelayService, DelayService>();
        services.AddTransient<IActivityLogger, ActivityLogger>();
        services.AddTransient<IConfigService, ConfigService>();
        services.AddTransient<IRecoveryService, RecoveryService>();
        services.AddTransient<IAccountService, AccountService>();
        services.AddTransient<ICategoryService, CategoryService>();
        services.AddTransient<IAccountLinkerService, AccountLinkerService>();
        services.AddTransient<IProxyManager, ProxyManager>();

        // Platform automation
        services.AddTransient<ThreadsAutomation>();

        // Template & Task engine
        services.AddTransient<ITemplateService, TemplateService>();
        services.AddSingleton<ITaskEngine, TaskEngine>();

        // Scheduler (Quartz). Singleton so the same IScheduler instance lives
        // for the whole app lifetime.
        services.AddSingleton<ISchedulerService, SchedulerService>();

        // Theme service — holds current base theme and applies via PaletteHelper.
        services.AddSingleton<IThemeService, ThemeService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AccountListViewModel>();
        services.AddTransient<TaskListViewModel>();
        services.AddTransient<TemplateEditorViewModel>();
        services.AddTransient<SchedulerViewModel>();
        services.AddTransient<LogViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            var scheduler = _serviceProvider.GetService<ISchedulerService>();
            if (scheduler != null)
                await scheduler.ShutdownAsync();

            var browserManager = _serviceProvider.GetService<IBrowserManager>();
            if (browserManager != null)
                await browserManager.DisposeAsync();

            _serviceProvider.Dispose();
        }
        base.OnExit(e);
    }
}

