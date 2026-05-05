using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SoMan.Models;
using SoMan.Services.Account;
using SoMan.Services.Logging;

namespace SoMan.ViewModels;

public partial class LogViewModel : ViewModelBase
{
    private readonly IActivityLogger _activityLogger;
    private readonly IAccountService _accountService;

    [ObservableProperty]
    private ObservableCollection<ActivityLog> _logs = new();

    [ObservableProperty]
    private ObservableCollection<Account> _availableAccounts = new();

    // Filters
    [ObservableProperty]
    private Account? _filterAccount;

    [ObservableProperty]
    private ActionResultFilter _filterResult = ActionResultFilter.All;

    [ObservableProperty]
    private int _fetchLimit = 500;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ActionResultFilter[] ResultFilterOptions =>
        (ActionResultFilter[])Enum.GetValues(typeof(ActionResultFilter));

    public LogViewModel(IActivityLogger activityLogger, IAccountService accountService)
    {
        _activityLogger = activityLogger;
        _accountService = accountService;
    }

    public override async Task InitializeAsync()
    {
        await LoadAccountsAsync();
        await RefreshAsync();
    }

    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _accountService.GetAllAsync();
            AvailableAccounts = new ObservableCollection<Account>(accounts.OrderBy(a => a.Username));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;

            List<ActivityLog> fetched = FilterAccount != null
                ? await _activityLogger.GetLogsForAccountAsync(FilterAccount.Id, FetchLimit)
                : await _activityLogger.GetRecentLogsAsync(FetchLimit);

            if (FilterResult != ActionResultFilter.All)
            {
                var want = FilterResult switch
                {
                    ActionResultFilter.Success => ActionResult.Success,
                    ActionResultFilter.Failed  => ActionResult.Failed,
                    _                          => ActionResult.Skipped,
                };
                fetched = fetched.Where(l => l.Result == want).ToList();
            }

            Logs = new ObservableCollection<ActivityLog>(fetched);
            StatusMessage = $"{Logs.Count} log entr{(Logs.Count == 1 ? "y" : "ies")}";
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
    private void ClearFilters()
    {
        FilterAccount = null;
        FilterResult = ActionResultFilter.All;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (Logs.Count == 0)
        {
            ErrorMessage = "Nothing to export.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            FileName = $"soman-logs-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "csv",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Account,Action,Target,Result,Details");
            foreach (var l in Logs)
            {
                sb.Append(l.ExecutedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
                sb.Append(Csv(l.Account?.Username ?? $"#{l.AccountId}")).Append(',');
                sb.Append(l.ActionType).Append(',');
                sb.Append(Csv(l.Target)).Append(',');
                sb.Append(l.Result).Append(',');
                sb.Append(Csv(l.Details)).AppendLine();
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            StatusMessage = $"Exported {Logs.Count} entries to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    partial void OnFilterAccountChanged(Account? value) => _ = RefreshAsync();
    partial void OnFilterResultChanged(ActionResultFilter value) => _ = RefreshAsync();
}

public enum ActionResultFilter
{
    All,
    Success,
    Failed,
    Skipped,
}
