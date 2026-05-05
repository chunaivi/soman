using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoMan.Models;
using SoMan.Services.Account;
using SoMan.Services.Browser;
using SoMan.Services.Proxy;

namespace SoMan.ViewModels;

public partial class AccountListViewModel : ViewModelBase
{
    private readonly IAccountService _accountService;
    private readonly ICategoryService _categoryService;
    private readonly IProxyManager _proxyManager;
    private readonly IBrowserManager _browserManager;
    private readonly ISessionValidator _sessionValidator;
    private readonly System.Timers.Timer _browserStatusTimer;

    // All data
    private List<Account> _allAccounts = new();

    // Filtered view
    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private ObservableCollection<AccountCategory> _categories = new();

    [ObservableProperty]
    private ObservableCollection<ProxyConfig> _proxies = new();

    // Filter
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Platform? _filterPlatform;

    [ObservableProperty]
    private AccountCategory? _filterCategory;

    [ObservableProperty]
    private AccountStatus? _filterStatus;

    // Stats
    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _activeCount;

    // Dialog state
    [ObservableProperty]
    private bool _isAddDialogOpen;

    [ObservableProperty]
    private bool _isEditMode;

    // Form
    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private Platform _formPlatform = Platform.Threads;

    [ObservableProperty]
    private string _formUsername = string.Empty;

    [ObservableProperty]
    private string _formCookiesJson = string.Empty;

    [ObservableProperty]
    private ProxyConfig? _formProxy;

    [ObservableProperty]
    private bool _formIsHeadless = true;

    [ObservableProperty]
    private string _formNotes = string.Empty;

    [ObservableProperty]
    private AccountCategory? _formCategory;

    private int? _editingAccountId;

    public Platform[] PlatformOptions => Enum.GetValues<Platform>();
    public AccountStatus[] StatusOptions => Enum.GetValues<AccountStatus>();

    // Browser status
    [ObservableProperty]
    private string _browserStatus = string.Empty;

    [ObservableProperty]
    private int _activeBrowsers;

    // Select all checkbox
    private bool? _isAllSelected = false;
    public bool? IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (_isAllSelected == value) return;
            _isAllSelected = value;
            OnPropertyChanged();
            if (value.HasValue)
            {
                foreach (var a in Accounts) a.IsSelected = value.Value;
            }
        }
    }

    private List<Models.Account> GetSelectedAccounts()
    {
        var selected = Accounts.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0 && SelectedAccount != null)
            selected.Add(SelectedAccount);
        return selected;
    }

    public AccountListViewModel(
        IAccountService accountService,
        ICategoryService categoryService,
        IProxyManager proxyManager,
        IBrowserManager browserManager,
        ISessionValidator sessionValidator)
    {
        _accountService = accountService;
        _categoryService = categoryService;
        _proxyManager = proxyManager;
        _browserManager = browserManager;
        _sessionValidator = sessionValidator;

        _browserStatusTimer = new System.Timers.Timer(5000);
        _browserStatusTimer.Elapsed += (_, _) =>
        {
            try
            {
                RefreshBrowserFlags();
            }
            catch
            {
                // ignore timer refresh errors
            }
        };
        _browserStatusTimer.AutoReset = true;
        _browserStatusTimer.Start();
    }

    public override async Task InitializeAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allAccounts = await _accountService.GetAllAsync();
            Categories = new ObservableCollection<AccountCategory>(await _categoryService.GetAllAsync());
            Proxies = new ObservableCollection<ProxyConfig>(await _proxyManager.GetAllAsync());
            TotalCount = _allAccounts.Count;
            ActiveCount = _allAccounts.Count(a => a.Status == AccountStatus.Active);
            ApplyFilter();
            RefreshBrowserFlags();
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

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnFilterPlatformChanged(Platform? value) => ApplyFilter();
    partial void OnFilterCategoryChanged(AccountCategory? value) => ApplyFilter();
    partial void OnFilterStatusChanged(AccountStatus? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = _allAccounts.AsEnumerable();

        if (FilterPlatform.HasValue)
            filtered = filtered.Where(a => a.Platform == FilterPlatform.Value);

        if (FilterStatus.HasValue)
            filtered = filtered.Where(a => a.Status == FilterStatus.Value);

        if (FilterCategory != null)
            filtered = filtered.Where(a => a.Categories.Any(c => c.AccountCategoryId == FilterCategory.Id));

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            filtered = filtered.Where(a =>
                a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.Username.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        Accounts = new ObservableCollection<Account>(filtered);
    }

    private void RefreshBrowserFlags()
    {
        foreach (var a in Accounts)
            a.IsBrowserRunning = _browserManager.IsContextAlive(a.Id);
        ActiveBrowsers = _browserManager.GetActiveContextCount();
    }

    [RelayCommand]
    private void OpenAddDialog()
    {
        _editingAccountId = null;
        IsEditMode = false;
        ClearForm();
        IsAddDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditDialog()
    {
        if (SelectedAccount == null) return;
        _editingAccountId = SelectedAccount.Id;
        IsEditMode = true;
        FormName = SelectedAccount.Name;
        FormPlatform = SelectedAccount.Platform;
        FormUsername = SelectedAccount.Username;
        FormCookiesJson = string.Empty; // don't show encrypted
        FormProxy = Proxies.FirstOrDefault(p => p.Id == SelectedAccount.ProxyConfigId);
        FormIsHeadless = SelectedAccount.IsHeadless;
        FormNotes = SelectedAccount.Notes ?? string.Empty;
        FormCategory = Categories.FirstOrDefault(c => SelectedAccount.Categories.Any(m => m.AccountCategoryId == c.Id));
        IsAddDialogOpen = true;
    }

    [RelayCommand]
    private async Task SaveAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(FormName) || string.IsNullOrWhiteSpace(FormUsername))
        {
            ErrorMessage = "Name and Username are required.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            if (IsEditMode && _editingAccountId.HasValue)
            {
                var account = await _accountService.GetByIdAsync(_editingAccountId.Value);
                if (account != null)
                {
                    account.Name = FormName.Trim();
                    account.Platform = FormPlatform;
                    account.Username = FormUsername.Trim();
                    account.ProxyConfigId = FormProxy?.Id;
                    account.IsHeadless = FormIsHeadless;
                    account.Notes = string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim();
                    await _accountService.UpdateAsync(account);

                    if (!string.IsNullOrWhiteSpace(FormCookiesJson))
                        await _accountService.ImportCookiesAsync(account.Id, FormCookiesJson.Trim());

                    var catIds = FormCategory != null ? new List<int> { FormCategory.Id } : new List<int>();
                    await _categoryService.SetAccountCategoriesAsync(account.Id, catIds);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(FormCookiesJson))
                {
                    ErrorMessage = "Cookies JSON is required for new accounts.";
                    IsLoading = false;
                    return;
                }
                await _accountService.AddAsync(
                    FormName.Trim(),
                    FormPlatform,
                    FormUsername.Trim(),
                    FormCookiesJson.Trim(),
                    FormProxy?.Id,
                    FormIsHeadless,
                    string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim());

                // Assign category to newly created account
                if (FormCategory != null)
                {
                    var allAccounts = await _accountService.GetAllAsync();
                    var newAccount = allAccounts.OrderByDescending(a => a.Id).FirstOrDefault();
                    if (newAccount != null)
                        await _categoryService.SetAccountCategoriesAsync(newAccount.Id, new List<int> { FormCategory.Id });
                }
            }

            IsAddDialogOpen = false;
            ClearForm();
            await LoadDataAsync();
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
    private void CancelDialog()
    {
        IsAddDialogOpen = false;
        ClearForm();
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        if (SelectedAccount == null) return;
        IsLoading = true;
        try
        {
            await _accountService.DeleteAsync(SelectedAccount.Id);
            await LoadDataAsync();
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
    private async Task ToggleStatusAsync()
    {
        if (SelectedAccount == null) return;
        var newStatus = SelectedAccount.Status == AccountStatus.Active
            ? AccountStatus.Disabled
            : AccountStatus.Active;
        await _accountService.UpdateStatusAsync(SelectedAccount.Id, newStatus);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task ImportCookiesAsync()
    {
        if (SelectedAccount == null || string.IsNullOrWhiteSpace(FormCookiesJson)) return;
        await _accountService.ImportCookiesAsync(SelectedAccount.Id, FormCookiesJson.Trim());
        FormCookiesJson = string.Empty;
        await LoadDataAsync();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchText = string.Empty;
        FilterPlatform = null;
        FilterCategory = null;
        FilterStatus = null;
    }

    private void ClearForm()
    {
        FormName = string.Empty;
        FormPlatform = Platform.Threads;
        FormUsername = string.Empty;
        FormCookiesJson = string.Empty;
        FormProxy = null;
        FormIsHeadless = true;
        FormNotes = string.Empty;
        FormCategory = null;
        _editingAccountId = null;
    }

    [RelayCommand]
    private async Task OpenBrowserAsync()
    {
        var selected = GetSelectedAccounts();
        if (selected.Count == 0)
        {
            BrowserStatus = "⚠ Select an account first (use checkbox or click row).";
            return;
        }

        IsLoading = true;
        int opened = 0;
        int failed = 0;
        int alreadyOpen = 0;
        try
        {
            foreach (var sel in selected)
            {
                if (_browserManager.IsContextAlive(sel.Id))
                {
                    alreadyOpen++;
                    continue; // already open
                }

                if (!_browserManager.CanLaunchMore())
                {
                    BrowserStatus = $"⚠ Opened {opened}/{selected.Count} — RAM/CPU too high, cannot open more.";
                    break;
                }

                BrowserStatus = $"Opening browser for '{sel.Name}'... ({opened + 1}/{selected.Count})";
                try
                {
                    var account = await _accountService.GetByIdAsync(sel.Id);
                    if (account == null) continue;

                    await _browserManager.OpenAccountPageAsync(account, "https://www.threads.net");
                    opened++;
                }
                catch (Exception ex)
                {
                    failed++;
                    BrowserStatus = $"✗ Failed '{sel.Name}': {ex.Message}";
                }
            }

            RefreshBrowserFlags();
            if (opened > 0)
            {
                var cookieInfo = _browserManager.LastInjectedCookieCount > 0
                    ? $" ({_browserManager.LastInjectedCookieCount} cookies injected)"
                    : " (0 cookies — check cookie data!)";
                if (failed == 0)
                    BrowserStatus = $"✓ Opened {opened} browser(s){cookieInfo}.";
                else
                    BrowserStatus = $"Opened {opened}, failed {failed}{cookieInfo}.";
            }
            else if (failed > 0)
            {
                BrowserStatus = $"✗ Failed to open {failed} browser(s).";
            }
            else if (alreadyOpen == selected.Count && alreadyOpen > 0)
            {
                // Every selected account already has a live context — surface
                // that explicitly so the click never feels like a no-op.
                BrowserStatus = alreadyOpen == 1
                    ? "ℹ Browser already open for this account."
                    : $"ℹ All {alreadyOpen} selected browser(s) already open.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CloseBrowserAsync()
    {
        var selected = GetSelectedAccounts();
        if (selected.Count == 0)
        {
            BrowserStatus = "⚠ Select an account first.";
            return;
        }

        int closed = 0;
        foreach (var sel in selected)
        {
            if (_browserManager.IsContextAlive(sel.Id))
            {
                await _browserManager.CloseContextAsync(sel.Id);
                closed++;
            }
        }

        RefreshBrowserFlags();
        BrowserStatus = closed > 0 ? $"✓ Closed {closed} browser(s)." : "No browsers were open for selected accounts.";
    }

    [RelayCommand]
    private async Task ValidateSessionAsync()
    {
        var selected = GetSelectedAccounts();
        if (selected.Count == 0)
        {
            BrowserStatus = "⚠ Select an account first.";
            return;
        }

        IsLoading = true;
        int valid = 0, expired = 0, errors = 0;
        try
        {
            foreach (var sel in selected)
            {
                BrowserStatus = $"Validating '{sel.Name}'... ({valid + expired + errors + 1}/{selected.Count})";
                try
                {
                    var account = await _accountService.GetByIdAsync(sel.Id);
                    if (account == null) continue;

                    account.IsHeadless = true;
                    var result = await _sessionValidator.ValidateSessionAsync(account);

                    if (result.Status == SessionStatus.Valid) valid++;
                    else if (result.Status == SessionStatus.Expired) expired++;
                    else errors++;
                }
                catch
                {
                    errors++;
                }
            }

            BrowserStatus = $"✓ Valid: {valid}, Expired: {expired}, Error: {errors}";
            await LoadDataAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CloseAllBrowsersAsync()
    {
        await _browserManager.CloseAllAsync();
        RefreshBrowserFlags();
        BrowserStatus = "✓ All browsers closed.";
    }
}
