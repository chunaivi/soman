using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SoMan.Models;
using SoMan.Services.Template;
using SoMan.Services.Text;

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

    // ── Advanced (raw JSON) editor for step parameters ──
    // When IsAdvancedJsonExpanded is true the user sees a textbox containing the
    // raw ParametersJson and can edit any field (including ones the form doesn't
    // surface). The form fields and JSON stay in lockstep — editing one updates
    // the other — and AdvancedParamsJson is the source of truth at Save time so
    // unknown fields the user adds in JSON are preserved.
    [ObservableProperty]
    private bool _isAdvancedJsonExpanded;

    [ObservableProperty]
    private string _advancedParamsJson = "{}";

    [ObservableProperty]
    private string? _advancedJsonError;

    // Internal guards to prevent JSON↔form sync from feedback-looping
    private bool _suppressFormToJson;
    private bool _suppressJsonToForm;

    private bool CanSaveStep() => string.IsNullOrEmpty(AdvancedJsonError);

    // Thread-from-text parameters
    [ObservableProperty]
    private string _paramThreadText = string.Empty;

    [ObservableProperty]
    private string _paramThreadFilePath = string.Empty;

    [ObservableProperty]
    private int _paramMaxCharsPerSegment = 500;

    [ObservableProperty]
    private int _paramSegmentDelayMin = 3000;

    [ObservableProperty]
    private int _paramSegmentDelayMax = 8000;

    [ObservableProperty]
    private string _paramThreadPreview = string.Empty;

    // AddToThread — URL of an existing thread's head post (or any segment)
    // to append a reply to. Commentary variants reuse ParamTexts.
    [ObservableProperty]
    private string _paramTargetUrl = string.Empty;

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
        IsAdvancedJsonExpanded = false;
        AdvancedJsonError = null;
        AdvancedParamsJson = BuildParametersJson();
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
        IsAdvancedJsonExpanded = false;
        AdvancedJsonError = null;
        // Pretty-print the existing JSON so it's easier to edit.
        AdvancedParamsJson = PrettyPrintJson(SelectedStep.ParametersJson);
        IsStepDialogOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanSaveStep))]
    private async Task SaveStepAsync()
    {
        if (SelectedTemplate == null) return;

        // Source of truth: the AdvancedParamsJson, which we keep in lockstep with
        // the form fields. Re-validate one last time before saving.
        string paramsJson;
        try
        {
            using var _ = JsonDocument.Parse(AdvancedParamsJson);
            paramsJson = AdvancedParamsJson;
        }
        catch
        {
            // Fallback to a fresh build from form fields if the JSON is somehow
            // invalid at save time (CanExecute should have prevented this).
            paramsJson = BuildParametersJson();
        }

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

    // ── Thread-from-text helpers ──

    [RelayCommand]
    private void BrowseThreadFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Select thread text file",
        };
        if (dlg.ShowDialog() == true)
        {
            ParamThreadFilePath = dlg.FileName;
            try
            {
                // Load content straight into the paste textbox so user can tweak
                // before saving the step.
                ParamThreadText = File.ReadAllText(dlg.FileName);
                PreviewThreadSplit();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not read file: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private void PreviewThreadSplit()
    {
        var source = !string.IsNullOrWhiteSpace(ParamThreadText)
            ? ParamThreadText
            : (File.Exists(ParamThreadFilePath) ? SafeReadFile(ParamThreadFilePath) : string.Empty);

        if (string.IsNullOrWhiteSpace(source))
        {
            ParamThreadPreview = "(no text yet)";
            return;
        }

        var segs = ThreadTextSplitter.Split(source, ParamMaxCharsPerSegment);
        if (segs.Count == 0)
        {
            ParamThreadPreview = "(no segments produced)";
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{segs.Count} segments (max {ParamMaxCharsPerSegment} chars each):");
        sb.AppendLine();
        for (int i = 0; i < segs.Count; i++)
        {
            sb.AppendLine($"── [{i + 1}/{segs.Count}] ({segs[i].Length} chars) ─────────");
            sb.AppendLine(segs[i]);
            sb.AppendLine();
        }
        ParamThreadPreview = sb.ToString();
    }

    /// <summary>
    /// Browses a .txt file and auto-generates template steps (1 CreatePost +
    /// N−1 ReplyToOwnLastPost) with each segment pre-filled. Lets the user
    /// edit per-segment afterwards.
    /// </summary>
    [RelayCommand]
    private async Task ImportThreadFromFileAsync()
    {
        if (SelectedTemplate == null)
        {
            ErrorMessage = "Select or create a template first.";
            return;
        }

        var dlg = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Import thread text file",
        };
        if (dlg.ShowDialog() != true) return;

        string content;
        try
        {
            content = await File.ReadAllTextAsync(dlg.FileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not read file: {ex.Message}";
            return;
        }

        var segments = ThreadTextSplitter.Split(content, ThreadTextSplitter.ThreadsMaxCharsPerPost);
        if (segments.Count == 0)
        {
            ErrorMessage = "File produced zero segments.";
            return;
        }

        // First segment → CreatePost
        var headJson = JsonSerializer.Serialize(new Dictionary<string, object> { ["text"] = segments[0] });
        await _templateService.AddStepAsync(SelectedTemplate.Id, ActionType.CreatePost, headJson, 3000, 8000);

        // Remaining segments → ReplyToOwnLastPost (single text via `texts` array of 1)
        for (int i = 1; i < segments.Count; i++)
        {
            var replyJson = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["texts"] = new[] { segments[i] },
            });
            await _templateService.AddStepAsync(SelectedTemplate.Id, ActionType.ReplyToOwnLastPost, replyJson, 3000, 8000);
        }

        await RefreshStepsAsync();
        StatusMessage = $"✓ Imported {segments.Count} segments from {Path.GetFileName(dlg.FileName)}.";
    }

    private static string SafeReadFile(string path)
    {
        try { return File.ReadAllText(path); } catch { return string.Empty; }
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
        ParamThreadText = string.Empty;
        ParamThreadFilePath = string.Empty;
        ParamMaxCharsPerSegment = 500;
        ParamSegmentDelayMin = 3000;
        ParamSegmentDelayMax = 8000;
        ParamThreadPreview = string.Empty;
        ParamTargetUrl = string.Empty;
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

            // CreateThreadFromText — reuse `text` bucket for the long blob, plus
            // dedicated fields for file / split / segment delays.
            if (FormStepActionType == ActionType.CreateThreadFromText &&
                p.TryGetValue("text", out var tt) && tt.ValueKind == JsonValueKind.String)
                ParamThreadText = tt.GetString() ?? string.Empty;
            if (p.TryGetValue("filePath", out var fp) && fp.ValueKind == JsonValueKind.String)
                ParamThreadFilePath = fp.GetString() ?? string.Empty;
            if (p.TryGetValue("maxCharsPerSegment", out var mc) && mc.ValueKind == JsonValueKind.Number)
                ParamMaxCharsPerSegment = mc.GetInt32();
            if (p.TryGetValue("segmentDelayMinMs", out var sdm) && sdm.ValueKind == JsonValueKind.Number)
                ParamSegmentDelayMin = sdm.GetInt32();
            if (p.TryGetValue("segmentDelayMaxMs", out var sdx) && sdx.ValueKind == JsonValueKind.Number)
                ParamSegmentDelayMax = sdx.GetInt32();

            // AddToThread — URL of the target post. Texts are already loaded
            // above via the shared `texts` bucket.
            if (p.TryGetValue("url", out var uu) && uu.ValueKind == JsonValueKind.String)
                ParamTargetUrl = uu.GetString() ?? string.Empty;
        }
        catch { /* ignore parse errors */ }
    }

    // ── Form ↔ JSON sync ──

    // Any form-side change re-builds the JSON, preserving any unknown fields
    // the user may have added in the advanced editor.
    partial void OnFormStepActionTypeChanged(ActionType value) => SyncFormToJson();
    partial void OnParamCountChanged(int value) => SyncFormToJson();
    partial void OnParamDurationSecondsChanged(int value) => SyncFormToJson();
    partial void OnParamTextsChanged(string value) => SyncFormToJson();
    partial void OnParamUsernameChanged(string value) => SyncFormToJson();
    partial void OnParamPostTextChanged(string value) => SyncFormToJson();
    partial void OnParamKeywordChanged(string value) => SyncFormToJson();
    partial void OnParamTargetUrlChanged(string value) => SyncFormToJson();
    partial void OnParamInteractChanged(bool value) => SyncFormToJson();

    // JSON-side edits validate, surface errors, and refresh the form when valid.
    partial void OnAdvancedParamsJsonChanged(string value)
    {
        if (_suppressJsonToForm) return;

        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                AdvancedJsonError = "Top-level value must be a JSON object.";
                return;
            }
            AdvancedJsonError = null;

            _suppressFormToJson = true;
            try { LoadStepParams(value); }
            finally { _suppressFormToJson = false; }
        }
        catch (JsonException ex)
        {
            AdvancedJsonError = $"Invalid JSON: {ex.Message}";
        }
    }

    partial void OnAdvancedJsonErrorChanged(string? value)
    {
        SaveStepCommand.NotifyCanExecuteChanged();
    }

    private void SyncFormToJson()
    {
        if (_suppressFormToJson) return;

        // Merge: start from any unknown keys in the current AdvancedParamsJson,
        // then overlay form-derived keys so the form is the source of truth for
        // surfaced fields while extras the user added in JSON survive.
        var merged = new Dictionary<string, object?>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(AdvancedParamsJson) ? "{}" : AdvancedParamsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    merged[prop.Name] = JsonElementToObject(prop.Value);
            }
        }
        catch { /* if current JSON is invalid, just rebuild from scratch */ }

        var fresh = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(BuildParametersJson());
        if (fresh != null)
        {
            foreach (var kv in fresh)
                merged[kv.Key] = JsonElementToObject(kv.Value);
        }

        _suppressJsonToForm = true;
        try
        {
            AdvancedParamsJson = JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
            AdvancedJsonError = null;
        }
        finally { _suppressJsonToForm = false; }
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToArray(),
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
        _ => el.ToString(),
    };

    private static string PrettyPrintJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
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
            case ActionType.CreateThreadFromText:
                if (!string.IsNullOrWhiteSpace(ParamThreadText))
                    obj["text"] = ParamThreadText;
                if (!string.IsNullOrWhiteSpace(ParamThreadFilePath))
                    obj["filePath"] = ParamThreadFilePath.Trim();
                obj["maxCharsPerSegment"] = ParamMaxCharsPerSegment;
                obj["segmentDelayMinMs"] = ParamSegmentDelayMin;
                obj["segmentDelayMaxMs"] = ParamSegmentDelayMax;
                break;
            case ActionType.AddToThread:
                obj["url"] = ParamTargetUrl.Trim();
                obj["texts"] = ParamTexts.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                break;
        }

        return JsonSerializer.Serialize(obj);
    }
}
