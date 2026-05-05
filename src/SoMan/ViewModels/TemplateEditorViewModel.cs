using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoMan.Models;
using SoMan.Services.Template;

namespace SoMan.ViewModels;

public partial class TemplateEditorViewModel : ViewModelBase
{
    private readonly ITemplateService _templateService;

    // Template list
    [ObservableProperty]
    private ObservableCollection<ActionTemplate> _templates = new();

    [ObservableProperty]
    private ActionTemplate? _selectedTemplate;

    // Steps of selected template
    [ObservableProperty]
    private ObservableCollection<ActionStep> _steps = new();

    [ObservableProperty]
    private ActionStep? _selectedStep;

    // Template form
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    private bool _isTemplateDialogOpen;

    [ObservableProperty]
    private bool _isEditingTemplate;

    [ObservableProperty]
    private string _formTemplateName = string.Empty;

    [ObservableProperty]
    private Platform _formTemplatePlatform = Platform.Threads;

    [ObservableProperty]
    private string _formTemplateDescription = string.Empty;

    // Step form
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    private bool _isStepDialogOpen;

    [ObservableProperty]
    private bool _isEditingStep;

    // Computed dialog open — settable so DialogHost.CloseOnClickAway can close it
    public bool IsDialogOpen
    {
        get => IsTemplateDialogOpen || IsStepDialogOpen;
        set
        {
            if (!value)
            {
                IsTemplateDialogOpen = false;
                IsStepDialogOpen = false;
            }
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private ActionType _formStepActionType = ActionType.ScrollFeed;

    [ObservableProperty]
    private int _formStepDelayMin = 3000;

    [ObservableProperty]
    private int _formStepDelayMax = 10000;

    // Step parameters (dynamic per action type)
    [ObservableProperty]
    private int _paramCount = 5;

    [ObservableProperty]
    private int _paramDurationSeconds = 60;

    [ObservableProperty]
    private string _paramTexts = "Nice!|Great post! 🔥|Interesting! 👍";

    [ObservableProperty]
    private string _paramUsername = string.Empty;

    [ObservableProperty]
    private string _paramPostText = string.Empty;

    [ObservableProperty]
    private string _paramKeyword = string.Empty;

    [ObservableProperty]
    private bool _paramInteract;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private int? _editingStepId;

    public Platform[] PlatformOptions => Enum.GetValues<Platform>();
    public ActionType[] ActionTypeOptions => Enum.GetValues<ActionType>();

    public TemplateEditorViewModel(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    public override async Task InitializeAsync()
    {
        await LoadTemplatesAsync();
    }

    [RelayCommand]
    private async Task LoadTemplatesAsync()
    {
        IsLoading = true;
        try
        {
            var all = await _templateService.GetAllAsync();
            Templates = new ObservableCollection<ActionTemplate>(all);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    partial void OnSelectedTemplateChanged(ActionTemplate? value)
    {
        if (value != null)
            Steps = new ObservableCollection<ActionStep>(value.Steps.OrderBy(s => s.Order));
        else
            Steps.Clear();
    }

    // ── Template CRUD ──

    [RelayCommand]
    private void OpenNewTemplateDialog()
    {
        IsEditingTemplate = false;
        FormTemplateName = string.Empty;
        FormTemplatePlatform = Platform.Threads;
        FormTemplateDescription = string.Empty;
        IsTemplateDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditTemplateDialog()
    {
        if (SelectedTemplate == null) return;
        IsEditingTemplate = true;
        FormTemplateName = SelectedTemplate.Name;
        FormTemplatePlatform = SelectedTemplate.Platform;
        FormTemplateDescription = SelectedTemplate.Description ?? string.Empty;
        IsTemplateDialogOpen = true;
    }

    [RelayCommand]
    private async Task SaveTemplateAsync()
    {
        System.Diagnostics.Debug.WriteLine("[SAVE] SaveTemplateAsync CALLED");
        System.Diagnostics.Debug.WriteLine($"[SAVE] FormTemplateName='{FormTemplateName}', Platform={FormTemplatePlatform}, Desc='{FormTemplateDescription}'");
        
        if (string.IsNullOrWhiteSpace(FormTemplateName))
        {
            ErrorMessage = "Template name is required.";
            System.Diagnostics.Debug.WriteLine("[SAVE] Name is empty, returning");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[SAVE] IsEditing={IsEditingTemplate}, SelectedTemplate={SelectedTemplate?.Name}");
            if (IsEditingTemplate && SelectedTemplate != null)
            {
                SelectedTemplate.Name = FormTemplateName.Trim();
                SelectedTemplate.Platform = FormTemplatePlatform;
                SelectedTemplate.Description = string.IsNullOrWhiteSpace(FormTemplateDescription) ? null : FormTemplateDescription.Trim();
                await _templateService.UpdateAsync(SelectedTemplate);
            }
            else
            {
                await _templateService.CreateAsync(
                    FormTemplateName.Trim(),
                    FormTemplatePlatform,
                    string.IsNullOrWhiteSpace(FormTemplateDescription) ? null : FormTemplateDescription.Trim());
            }

            IsTemplateDialogOpen = false;
            await LoadTemplatesAsync();
            StatusMessage = IsEditingTemplate ? "✓ Template updated." : "✓ Template created.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync()
    {
        if (SelectedTemplate == null) return;
        await _templateService.DeleteAsync(SelectedTemplate.Id);
        SelectedTemplate = null;
        await LoadTemplatesAsync();
        StatusMessage = "✓ Template deleted.";
    }

    [RelayCommand]
    private async Task DuplicateTemplateAsync()
    {
        if (SelectedTemplate == null) return;
        await _templateService.DuplicateAsync(SelectedTemplate.Id);
        await LoadTemplatesAsync();
        StatusMessage = "✓ Template duplicated.";
    }

    // ── Step CRUD ──

    [RelayCommand]
    private void OpenAddStepDialog()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "⚠ Select a template first.";
            return;
        }
        IsEditingStep = false;
        _editingStepId = null;
        FormStepActionType = ActionType.ScrollFeed;
        FormStepDelayMin = 3000;
        FormStepDelayMax = 10000;
        ClearStepParams();
        IsStepDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditStepDialog()
    {
        if (SelectedStep == null) return;
        IsEditingStep = true;
        _editingStepId = SelectedStep.Id;
        FormStepActionType = SelectedStep.ActionType;
        FormStepDelayMin = SelectedStep.DelayMinMs;
        FormStepDelayMax = SelectedStep.DelayMaxMs;
        LoadStepParams(SelectedStep.ParametersJson);
        IsStepDialogOpen = true;
    }

    [RelayCommand]
    private async Task SaveStepAsync()
    {
        if (SelectedTemplate == null) return;

        string paramsJson = BuildParametersJson();

        try
        {
            if (IsEditingStep && _editingStepId.HasValue)
            {
                var step = SelectedStep!;
                step.ActionType = FormStepActionType;
                step.ParametersJson = paramsJson;
                step.DelayMinMs = FormStepDelayMin;
                step.DelayMaxMs = FormStepDelayMax;
                await _templateService.UpdateStepAsync(step);
            }
            else
            {
                await _templateService.AddStepAsync(
                    SelectedTemplate.Id, FormStepActionType, paramsJson,
                    FormStepDelayMin, FormStepDelayMax);
            }

            IsStepDialogOpen = false;
            await RefreshStepsAsync();
            StatusMessage = IsEditingStep ? "✓ Step updated." : "✓ Step added.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteStepAsync()
    {
        if (SelectedStep == null) return;
        await _templateService.DeleteStepAsync(SelectedStep.Id);
        await RefreshStepsAsync();
        StatusMessage = "✓ Step deleted.";
    }

    [RelayCommand]
    private async Task MoveStepUpAsync()
    {
        if (SelectedStep == null || SelectedTemplate == null) return;
        var list = Steps.ToList();
        int idx = list.FindIndex(s => s.Id == SelectedStep.Id);
        if (idx <= 0) return;
        var ids = list.Select(s => s.Id).ToList();
        (ids[idx], ids[idx - 1]) = (ids[idx - 1], ids[idx]);
        await _templateService.ReorderStepsAsync(SelectedTemplate.Id, ids);
        await RefreshStepsAsync();
    }

    [RelayCommand]
    private async Task MoveStepDownAsync()
    {
        if (SelectedStep == null || SelectedTemplate == null) return;
        var list = Steps.ToList();
        int idx = list.FindIndex(s => s.Id == SelectedStep.Id);
        if (idx < 0 || idx >= list.Count - 1) return;
        var ids = list.Select(s => s.Id).ToList();
        (ids[idx], ids[idx + 1]) = (ids[idx + 1], ids[idx]);
        await _templateService.ReorderStepsAsync(SelectedTemplate.Id, ids);
        await RefreshStepsAsync();
    }

    [RelayCommand]
    private void CancelDialog()
    {
        IsTemplateDialogOpen = false;
        IsStepDialogOpen = false;
        ErrorMessage = null;
    }

    // ── Helpers ──

    private async Task RefreshStepsAsync()
    {
        if (SelectedTemplate == null) return;
        var fresh = await _templateService.GetByIdAsync(SelectedTemplate.Id);
        if (fresh != null)
        {
            SelectedTemplate = fresh;
            Steps = new ObservableCollection<ActionStep>(fresh.Steps.OrderBy(s => s.Order));
        }
    }

    private void ClearStepParams()
    {
        ParamCount = 5;
        ParamDurationSeconds = 60;
        ParamTexts = "Nice!|Great post! 🔥|Interesting! 👍";
        ParamUsername = string.Empty;
        ParamPostText = string.Empty;
        ParamKeyword = string.Empty;
        ParamInteract = false;
    }

    private void LoadStepParams(string json)
    {
        ClearStepParams();
        try
        {
            var p = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (p == null) return;

            if (p.TryGetValue("count", out var c) && c.ValueKind == JsonValueKind.Number)
                ParamCount = c.GetInt32();
            if (p.TryGetValue("durationSeconds", out var d) && d.ValueKind == JsonValueKind.Number)
                ParamDurationSeconds = d.GetInt32();
            if (p.TryGetValue("texts", out var t) && t.ValueKind == JsonValueKind.Array)
                ParamTexts = string.Join("|", t.EnumerateArray().Select(x => x.GetString()));
            if (p.TryGetValue("username", out var u) && u.ValueKind == JsonValueKind.String)
                ParamUsername = u.GetString() ?? string.Empty;
            if (p.TryGetValue("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                ParamPostText = tx.GetString() ?? string.Empty;
            if (p.TryGetValue("keyword", out var k) && k.ValueKind == JsonValueKind.String)
                ParamKeyword = k.GetString() ?? string.Empty;
            if (p.TryGetValue("interactWithResults", out var ir))
                ParamInteract = ir.ValueKind == JsonValueKind.True;
        }
        catch { /* ignore parse errors */ }
    }

    private string BuildParametersJson()
    {
        var obj = new Dictionary<string, object>();

        switch (FormStepActionType)
        {
            case ActionType.ScrollFeed:
                obj["durationSeconds"] = ParamDurationSeconds;
                break;
            case ActionType.Like:
            case ActionType.Repost:
                obj["count"] = ParamCount;
                obj["source"] = "feed";
                break;
            case ActionType.Comment:
                obj["count"] = ParamCount;
                obj["texts"] = ParamTexts.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                break;
            case ActionType.Follow:
                obj["count"] = ParamCount;
                obj["source"] = "suggested";
                if (!string.IsNullOrWhiteSpace(ParamUsername))
                    obj["username"] = ParamUsername.Trim();
                break;
            case ActionType.Unfollow:
                obj["username"] = ParamUsername.Trim();
                break;
            case ActionType.CreatePost:
                obj["text"] = ParamPostText.Trim();
                break;
            case ActionType.ViewProfile:
                obj["username"] = ParamUsername.Trim();
                break;
            case ActionType.Search:
                obj["keyword"] = ParamKeyword.Trim();
                obj["interactWithResults"] = ParamInteract;
                break;
            case ActionType.OpenRandomPost:
                // No parameters needed — picks random post from current page
                break;
            case ActionType.ReplyToOwnLastPost:
                // Random pick from pipe-separated variants (reuses the same
                // "texts" bucket as Comment for UI consistency).
                obj["texts"] = ParamTexts.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                break;
        }

        return JsonSerializer.Serialize(obj);
    }
}
