using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoMan.Models;
using SoMan.Services.Account;
using SoMan.Services.Config;
using SoMan.Services.Proxy;

namespace SoMan.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ICategoryService _categoryService;
    private readonly IProxyManager _proxyManager;
    private readonly IConfigService _configService;

    [ObservableProperty]
    private int _historyRetentionDays = 30;

    // Categories
    [ObservableProperty]
    private ObservableCollection<AccountCategory> _categories = new();

    [ObservableProperty]
    private AccountCategory? _selectedCategory;

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string _newCategoryColor = "#2196F3";

    // Color picker bridge: WPF Color <-> hex string
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

    // Proxies
    [ObservableProperty]
    private ObservableCollection<ProxyConfig> _proxyList = new();

    [ObservableProperty]
    private ProxyConfig? _selectedProxy;

    [ObservableProperty]
    private string _newProxyHost = string.Empty;

    [ObservableProperty]
    private string _newProxyPort = string.Empty;

    [ObservableProperty]
    private ProxyType _newProxyType = ProxyType.Http;

    [ObservableProperty]
    private string _newProxyUser = string.Empty;

    [ObservableProperty]
    private string _newProxyPass = string.Empty;

    [ObservableProperty]
    private string _bulkProxyText = string.Empty;

    public ProxyType[] ProxyTypeOptions => Enum.GetValues<ProxyType>();

    public SettingsViewModel(ICategoryService categoryService, IProxyManager proxyManager, IConfigService configService)
    {
        _categoryService = categoryService;
        _proxyManager = proxyManager;
        _configService = configService;
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
            HistoryRetentionDays = await _configService.GetIntAsync("HistoryRetentionDays", 30);
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
            await _configService.SetAsync("HistoryRetentionDays", HistoryRetentionDays.ToString());
            ErrorMessage = "✓ Settings saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
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
