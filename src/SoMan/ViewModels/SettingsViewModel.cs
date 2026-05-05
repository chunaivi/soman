using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoMan.Models;
using SoMan.Services.Account;
using SoMan.Services.Config;
using SoMan.Services.Proxy;
using SoMan.Services.Theming;

namespace SoMan.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ICategoryService _categoryService;
    private readonly IProxyManager _proxyManager;
    private readonly IConfigService _configService;
    private readonly IThemeService _themeService;

    // ── General ──
    [ObservableProperty] private string _theme = "Dark";
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private int _historyRetentionDays = 30;

    // ── Browser ──
    [ObservableProperty] private string _browserDefaultMode = "Headless";
    [ObservableProperty] private string _browserEngine = "Chromium";
    [ObservableProperty] private string _maxConcurrent = "auto";

    // ── Delay ──
    [ObservableProperty] private int _delayBetweenActionsMinMs = 3000;
    [ObservableProperty] private int _delayBetweenActionsMaxMs = 10000;
    [ObservableProperty] private int _delayBetweenAccountsMinMs = 5000;
    [ObservableProperty] private int _delayBetweenAccountsMaxMs = 15000;
    [ObservableProperty] private int _delayJitterPercent = 20;
    [ObservableProperty] private bool _enableHumanSimulation = true;

    // ── Resource Limits ──
    [ObservableProperty] private int _maxCpuPercent = 85;
    [ObservableProperty] private int _minFreeRamPercent = 20;

    public string[] ThemeOptions { get; } = { "Dark", "Light" };
    public string[] BrowserModeOptions { get; } = { "Headless", "Headed" };
    public string[] BrowserEngineOptions { get; } = { "Chromium", "Firefox", "WebKit" };
    public string[] MaxConcurrentOptions { get; } = { "auto", "1", "2", "3", "4", "6", "8", "12", "16" };

    // ── Categories ──
    [ObservableProperty] private ObservableCollection<AccountCategory> _categories = new();
    [ObservableProperty] private AccountCategory? _selectedCategory;
    [ObservableProperty] private string _newCategoryName = string.Empty;
    [ObservableProperty] private string _newCategoryColor = "#2196F3";

    public Color CategoryPickerColor
    {
        get
        {
            try { return (Color)ColorConverter.ConvertFromString(NewCategoryColor); }
            catch { return (Color)ColorConverter.ConvertFromString("#2196F3"); }
        }
        set
        {
            NewCategoryColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            OnPropertyChanged();
        }
    }

    partial void OnNewCategoryColorChanged(string value) => OnPropertyChanged(nameof(CategoryPickerColor));

    // ── Proxies ──
    [ObservableProperty] private ObservableCollection<ProxyConfig> _proxyList = new();
    [ObservableProperty] private ProxyConfig? _selectedProxy;
    [ObservableProperty] private string _newProxyHost = string.Empty;
    [ObservableProperty] private string _newProxyPort = string.Empty;
    [ObservableProperty] private ProxyType _newProxyType = ProxyType.Http;
    [ObservableProperty] private string _newProxyUser = string.Empty;
    [ObservableProperty] private string _newProxyPass = string.Empty;
    [ObservableProperty] private string _bulkProxyText = string.Empty;

    public ProxyType[] ProxyTypeOptions => Enum.GetValues<ProxyType>();

    public SettingsViewModel(
        ICategoryService categoryService,
        IProxyManager proxyManager,
        IConfigService configService,
        IThemeService themeService)
    {
        _categoryService = categoryService;
        _proxyManager = proxyManager;
        _configService = configService;
        _themeService = themeService;
    }

    public override async Task InitializeAsync()
    {
        await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        IsLoading = true;
        try
        {
            Categories = new ObservableCollection<AccountCategory>(await _categoryService.GetAllAsync());
            ProxyList = new ObservableCollection<ProxyConfig>(await _proxyManager.GetAllAsync());

            // General
            Theme = await _configService.GetAsync("Theme", "Dark");
            StartWithWindows = await _configService.GetBoolAsync("StartWithWindows", false);
            MinimizeToTray = await _configService.GetBoolAsync("MinimizeToTray", true);
            HistoryRetentionDays = await _configService.GetIntAsync("HistoryRetentionDays", 30);

            // Browser
            BrowserDefaultMode = await _configService.GetAsync("BrowserDefaultMode", "Headless");
            BrowserEngine = await _configService.GetAsync("BrowserEngine", "Chromium");
            MaxConcurrent = await _configService.GetAsync("MaxConcurrent", "auto");

            // Delay
            DelayBetweenActionsMinMs = await _configService.GetIntAsync("DelayBetweenActionsMinMs", 3000);
            DelayBetweenActionsMaxMs = await _configService.GetIntAsync("DelayBetweenActionsMaxMs", 10000);
            DelayBetweenAccountsMinMs = await _configService.GetIntAsync("DelayBetweenAccountsMinMs", 5000);
            DelayBetweenAccountsMaxMs = await _configService.GetIntAsync("DelayBetweenAccountsMaxMs", 15000);
            DelayJitterPercent = await _configService.GetIntAsync("DelayJitterPercent", 20);
            EnableHumanSimulation = await _configService.GetBoolAsync("EnableHumanSimulation", true);

            // Resource limits
            MaxCpuPercent = await _configService.GetIntAsync("MaxCpuPercent", 85);
            MinFreeRamPercent = await _configService.GetIntAsync("MinFreeRamPercent", 20);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            // General
            await _themeService.SetThemeAsync(Theme); // also persists "Theme"
            await _configService.SetAsync("StartWithWindows", StartWithWindows.ToString().ToLowerInvariant());
            await _configService.SetAsync("MinimizeToTray", MinimizeToTray.ToString().ToLowerInvariant());
            await _configService.SetAsync("HistoryRetentionDays", HistoryRetentionDays.ToString());

            // Browser
            await _configService.SetAsync("BrowserDefaultMode", BrowserDefaultMode);
            await _configService.SetAsync("BrowserEngine", BrowserEngine);
            await _configService.SetAsync("MaxConcurrent", MaxConcurrent);

            // Delay
            await _configService.SetAsync("DelayBetweenActionsMinMs", DelayBetweenActionsMinMs.ToString());
            await _configService.SetAsync("DelayBetweenActionsMaxMs", DelayBetweenActionsMaxMs.ToString());
            await _configService.SetAsync("DelayBetweenAccountsMinMs", DelayBetweenAccountsMinMs.ToString());
            await _configService.SetAsync("DelayBetweenAccountsMaxMs", DelayBetweenAccountsMaxMs.ToString());
            await _configService.SetAsync("DelayJitterPercent", DelayJitterPercent.ToString());
            await _configService.SetAsync("EnableHumanSimulation", EnableHumanSimulation.ToString().ToLowerInvariant());

            // Resource limits
            await _configService.SetAsync("MaxCpuPercent", MaxCpuPercent.ToString());
            await _configService.SetAsync("MinFreeRamPercent", MinFreeRamPercent.ToString());

            ErrorMessage = "✓ Settings saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // Apply theme live the moment user switches it in the ComboBox,
    // so they don't have to click Save to see the change.
    partial void OnThemeChanged(string value)
    {
        try { _ = _themeService.SetThemeAsync(value); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // --- Category Commands ---

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;
        await _categoryService.AddAsync(NewCategoryName.Trim(), NewCategoryColor);
        NewCategoryName = string.Empty;
        Categories = new ObservableCollection<AccountCategory>(await _categoryService.GetAllAsync());
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory == null) return;
        await _categoryService.DeleteAsync(SelectedCategory.Id);
        Categories = new ObservableCollection<AccountCategory>(await _categoryService.GetAllAsync());
    }

    // --- Proxy Commands ---

    [RelayCommand]
    private async Task AddProxyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProxyHost) || !int.TryParse(NewProxyPort, out int port)) return;
        await _proxyManager.AddAsync(
            $"{NewProxyHost}:{port}",
            NewProxyType,
            NewProxyHost.Trim(),
            port,
            string.IsNullOrWhiteSpace(NewProxyUser) ? null : NewProxyUser.Trim(),
            string.IsNullOrWhiteSpace(NewProxyPass) ? null : NewProxyPass.Trim());
        NewProxyHost = string.Empty;
        NewProxyPort = string.Empty;
        NewProxyUser = string.Empty;
        NewProxyPass = string.Empty;
        ProxyList = new ObservableCollection<ProxyConfig>(await _proxyManager.GetAllAsync());
    }

    [RelayCommand]
    private async Task DeleteProxyAsync()
    {
        if (SelectedProxy == null) return;
        await _proxyManager.DeleteAsync(SelectedProxy.Id);
        ProxyList = new ObservableCollection<ProxyConfig>(await _proxyManager.GetAllAsync());
    }

    [RelayCommand]
    private async Task ImportProxiesAsync()
    {
        if (string.IsNullOrWhiteSpace(BulkProxyText)) return;
        var imported = await _proxyManager.ImportBulkAsync(BulkProxyText);
        BulkProxyText = string.Empty;
        ProxyList = new ObservableCollection<ProxyConfig>(await _proxyManager.GetAllAsync());
        ErrorMessage = $"Imported {imported.Count} proxies.";
    }
}
