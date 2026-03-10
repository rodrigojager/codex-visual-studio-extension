using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexVsix.Models;
using CodexVsix.Services;
using CodexVsix.UI;
using Microsoft.Win32;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

namespace CodexVsix.ViewModels;

public sealed class CodexToolWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const double DefaultContextTokenBudget = 128000d;

    private readonly LocalizationService _localization = new();
    private readonly ExtensionSettingsStore _settingsStore = new();
    private readonly CodexProcessService _codexProcessService = new();
    private readonly SolutionContextService _solutionContextService = new();

    private CancellationTokenSource? _cts;
    private ChatMessage? _currentAssistantMessage;
    private ChatMessage? _currentTransientStatusMessage;
    private bool _isBusy;
    private bool _showHistoryPanel;
    private bool _showSettingsPanel;
    private string _prompt = string.Empty;
    private string _output = string.Empty;
    private string _currentMentionQuery = string.Empty;
    private string _selectedModel = string.Empty;
    private string _selectedReasoningEffort = string.Empty;
    private string _selectedVerbosity = string.Empty;
    private Geometry _contextRingGeometry = Geometry.Parse("M 8,1 A 7,7 0 1 1 7.99,1");
    private double _contextTokenBudget = DefaultContextTokenBudget;
    private double _lastKnownRemainingTokens = DefaultContextTokenBudget;
    private ApprovalPromptViewModel? _currentApprovalPrompt;
    private TaskCompletionSource<JToken?>? _approvalDecisionTcs;
    private UserInputPromptViewModel? _currentUserInputPrompt;
    private TaskCompletionSource<JObject?>? _userInputDecisionTcs;
    private CodexThreadSummary? _selectedThread;
    private bool _suppressThreadSelection;
    private string _renameThreadName = string.Empty;

    public CodexToolWindowViewModel()
    {
        Settings = _settingsStore.Load();

        if (string.IsNullOrWhiteSpace(Settings.DefaultModel)) Settings.DefaultModel = "gpt-5-codex";
        if (string.IsNullOrWhiteSpace(Settings.ReasoningEffort)) Settings.ReasoningEffort = "high";
        if (string.IsNullOrWhiteSpace(Settings.ModelVerbosity)) Settings.ModelVerbosity = "medium";
        if (string.IsNullOrWhiteSpace(Settings.SandboxMode)) Settings.SandboxMode = "read-only";
        NormalizeSelectionSettings();

        _selectedModel = Settings.DefaultModel;
        _selectedReasoningEffort = Settings.ReasoningEffort;
        _selectedVerbosity = Settings.ModelVerbosity;
        _codexProcessService.ApprovalRequestHandler = HandleApprovalRequestAsync;
        _codexProcessService.UserInputRequestHandler = HandleUserInputRequestAsync;
        _codexProcessService.ThreadCatalogChanged += HandleThreadCatalogChanged;

        SendCommand = new DelegateCommand(Send, () => !IsBusy && !string.IsNullOrWhiteSpace(Prompt));
        CancelCommand = new DelegateCommand(Cancel, () => IsBusy);
        SaveSettingsCommand = new DelegateCommand(SaveSettings);
        ClearOutputCommand = new DelegateCommand(() => Output = string.Empty);
        UseSolutionDirectoryCommand = new DelegateCommand(UseSolutionDirectory);
        OpenCodexConfigCommand = new DelegateCommand(OpenCodexConfig);
        PasteImageCommand = new DelegateCommand(PasteImageFromClipboard);
        AddImageFileCommand = new DelegateCommand(AddAttachment);
        RemoveSelectedImageCommand = new DelegateCommand(RemoveSelectedImage, () => SelectedImagePath is not null);
        RemoveAttachmentCommand = new DelegateCommand(RemoveAttachment);
        InsertSelectedMentionCommand = new DelegateCommand(InsertSelectedMention, () => SelectedMention is not null);
        ReuseHistoryPromptCommand = new DelegateCommand(ReuseHistoryPrompt, () => SelectedHistoryPrompt is not null);
        NewThreadCommand = new DelegateCommand(StartNewThread, () => !IsBusy);
        RenameThreadCommand = new DelegateCommand(RenameSelectedThread, () => !IsBusy && SelectedThread is not null && !string.IsNullOrWhiteSpace(RenameThreadName));
        ToggleHistoryPanelCommand = new DelegateCommand(ToggleHistoryPanel);
        ToggleSettingsPanelCommand = new DelegateCommand(ToggleSettingsPanel);
        ResolveApprovalCommand = new DelegateCommand(ResolveApproval);
        ResolveUserInputCommand = new DelegateCommand(ResolveUserInput);

        foreach (var option in CreateFallbackModelOptions())
        {
            ModelOptions.Add(option);
        }

        _selectedModel = EnsureOptionValue(_selectedModel, ModelOptions, ModelOptions.First().Value);

        foreach (var item in GetRecentPromptHistory())
        {
            PromptHistory.Add(item);
        }

        RefreshMentions();
        UpdateContextEstimate();
        ThreadHelper.JoinableTaskFactory.RunAsync(InitializeAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService Localization => _localization;

    public void Dispose()
    {
        _approvalDecisionTcs?.TrySetResult(JValue.CreateString("cancel"));
        _userInputDecisionTcs?.TrySetResult(new JObject { ["answers"] = new JObject() });
        _cts?.Cancel();
        _cts?.Dispose();
        _codexProcessService.ThreadCatalogChanged -= HandleThreadCatalogChanged;
        _codexProcessService.Dispose();
    }

    public CodexExtensionSettings Settings { get; }

    public ObservableCollection<string> MentionSuggestions { get; } = new();
    public ObservableCollection<string> AttachedImages { get; } = new();
    public ObservableCollection<string> PromptHistory { get; } = new();
    public ObservableCollection<CodexThreadSummary> Threads { get; } = new();
    public ObservableCollection<CodexAppSummary> Apps { get; } = new();
    public ObservableCollection<CodexMcpServerSummary> McpServers { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<SelectionOption> ModelOptions { get; } = new();

    public SelectionOption[] ReasoningOptions => _localization.CreateReasoningOptions();

    public SelectionOption[] VerbosityOptions => _localization.CreateVerbosityOptions();

    public SelectionOption[] ApprovalPolicyOptions => _localization.CreateApprovalPolicyOptions();

    public SelectionOption[] SandboxModeOptions => _localization.CreateSandboxModeOptions();

    public DelegateCommand SendCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand SaveSettingsCommand { get; }
    public DelegateCommand ClearOutputCommand { get; }
    public DelegateCommand UseSolutionDirectoryCommand { get; }
    public DelegateCommand OpenCodexConfigCommand { get; }
    public DelegateCommand PasteImageCommand { get; }
    public DelegateCommand AddImageFileCommand { get; }
    public DelegateCommand RemoveSelectedImageCommand { get; }
    public DelegateCommand RemoveAttachmentCommand { get; }
    public DelegateCommand InsertSelectedMentionCommand { get; }
    public DelegateCommand ReuseHistoryPromptCommand { get; }
    public DelegateCommand NewThreadCommand { get; }
    public DelegateCommand RenameThreadCommand { get; }
    public DelegateCommand ToggleHistoryPanelCommand { get; }
    public DelegateCommand ToggleSettingsPanelCommand { get; }
    public DelegateCommand ResolveApprovalCommand { get; }
    public DelegateCommand ResolveUserInputCommand { get; }

    public string Prompt
    {
        get => _prompt;
        set => RunOnUiThread(() =>
        {
            _prompt = value;
            OnPropertyChanged();
            RefreshMentions();
            UpdateContextEstimate();
            SendCommand.RaiseCanExecuteChanged();
        });
    }

    public string Output
    {
        get => _output;
        set => RunOnUiThread(() =>
        {
            _output = value;
            OnPropertyChanged();
        });
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            SendCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            NewThreadCommand.RaiseCanExecuteChanged();
            RenameThreadCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ShowHistoryPanel
    {
        get => _showHistoryPanel;
        private set
        {
            _showHistoryPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSidebarExpanded));
            OnPropertyChanged(nameof(SidebarWidth));
        }
    }

    public bool ShowSettingsPanel
    {
        get => _showSettingsPanel;
        private set
        {
            _showSettingsPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSidebarExpanded));
            OnPropertyChanged(nameof(SidebarWidth));
        }
    }

    public bool IsSidebarExpanded => ShowHistoryPanel || ShowSettingsPanel;

    public double SidebarWidth => IsSidebarExpanded ? 292d : 0d;

    public string CurrentMentionQuery
    {
        get => _currentMentionQuery;
        private set
        {
            _currentMentionQuery = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMentionSuggestions));
        }
    }

    public bool HasMentionSuggestions => MentionSuggestions.Count > 0 && !string.IsNullOrWhiteSpace(CurrentMentionQuery);

    public ApprovalPromptViewModel? CurrentApprovalPrompt
    {
        get => _currentApprovalPrompt;
        private set
        {
            _currentApprovalPrompt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCurrentApprovalPrompt));
        }
    }

    public bool HasCurrentApprovalPrompt => CurrentApprovalPrompt is not null;

    public UserInputPromptViewModel? CurrentUserInputPrompt
    {
        get => _currentUserInputPrompt;
        private set
        {
            _currentUserInputPrompt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCurrentUserInputPrompt));
        }
    }

    public bool HasCurrentUserInputPrompt => CurrentUserInputPrompt is not null;

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            value = NormalizeModelValue(value);
            if (string.Equals(_selectedModel, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedModel = value;
            Settings.DefaultModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileLabel));
            OnPropertyChanged(nameof(SelectedModelLabel));
        }
    }

    public string SelectedReasoningEffort
    {
        get => _selectedReasoningEffort;
        set
        {
            value = NormalizeReasoningEffortValue(value);
            if (string.Equals(_selectedReasoningEffort, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedReasoningEffort = value;
            Settings.ReasoningEffort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedReasoningEffortLabel));
        }
    }

    public string SelectedVerbosity
    {
        get => _selectedVerbosity;
        set
        {
            value = EnsureOptionValue(value, VerbosityOptions, "medium");
            if (string.Equals(_selectedVerbosity, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedVerbosity = value;
            Settings.ModelVerbosity = value;
            OnPropertyChanged();
        }
    }

    public string SelectedSandboxMode
    {
        get => Settings.SandboxMode;
        set
        {
            value = EnsureOptionValue(value, SandboxModeOptions, "read-only");
            if (string.Equals(Settings.SandboxMode, value, StringComparison.Ordinal))
            {
                return;
            }

            Settings.SandboxMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSandboxModeLabel));
        }
    }

    public string SelectedApprovalPolicy
    {
        get => Settings.ApprovalPolicy;
        set
        {
            value = EnsureOptionValue(value, ApprovalPolicyOptions, string.Empty);
            if (string.Equals(Settings.ApprovalPolicy, value, StringComparison.Ordinal))
            {
                return;
            }

            Settings.ApprovalPolicy = value;
            OnPropertyChanged();
        }
    }

    public string ProfileLabel => string.IsNullOrWhiteSpace(Settings.Profile) ? "develop" : Settings.Profile;

    public string CollaborationModeLabel => PlanModeEnabled ? _localization.AgentModeLabel : _localization.QuestionModeLabel;

    public string SelectedModelLabel => GetOptionLabel(ModelOptions, SelectedModel, ModelOptions.FirstOrDefault()?.Label ?? "gpt-5-codex");

    public string SelectedReasoningEffortLabel => GetOptionLabel(ReasoningOptions, SelectedReasoningEffort, ReasoningOptions.FirstOrDefault(option => string.Equals(option.Value, "high", StringComparison.Ordinal))?.Label ?? "high");

    public string SelectedSandboxModeLabel => GetOptionLabel(SandboxModeOptions, SelectedSandboxMode, SandboxModeOptions.FirstOrDefault(option => string.Equals(option.Value, "read-only", StringComparison.Ordinal))?.Label ?? "read-only");

    public bool PlanModeEnabled
    {
        get => Settings.PlanModeEnabled;
        set
        {
            if (Settings.PlanModeEnabled == value)
            {
                return;
            }

            Settings.PlanModeEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CollaborationModeLabel));
        }
    }

    public Geometry ContextRingGeometry
    {
        get => _contextRingGeometry;
        private set => RunOnUiThread(() =>
        {
            _contextRingGeometry = value;
            OnPropertyChanged();
        });
    }

    public string ContextTokensLabel => _lastKnownRemainingTokens > 0
        ? FormatCompactTokenCount(_lastKnownRemainingTokens)
        : string.Empty;

    public CodexThreadSummary? SelectedThread
    {
        get => _selectedThread;
        set
        {
            if (ReferenceEquals(_selectedThread, value))
            {
                return;
            }

            _selectedThread = value;
            RenameThreadName = value?.Title ?? string.Empty;
            OnPropertyChanged();
            RenameThreadCommand.RaiseCanExecuteChanged();

            if (!_suppressThreadSelection && value is not null)
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(() => OpenThreadAsync(value.ThreadId));
            }
        }
    }

    public string RenameThreadName
    {
        get => _renameThreadName;
        set
        {
            _renameThreadName = value;
            OnPropertyChanged();
            RenameThreadCommand.RaiseCanExecuteChanged();
        }
    }

    private string? _selectedMention;
    public string? SelectedMention
    {
        get => _selectedMention;
        set
        {
            _selectedMention = value;
            OnPropertyChanged();
            InsertSelectedMentionCommand.RaiseCanExecuteChanged();
        }
    }

    private string? _selectedImagePath;
    public string? SelectedImagePath
    {
        get => _selectedImagePath;
        set
        {
            _selectedImagePath = value;
            OnPropertyChanged();
            RemoveSelectedImageCommand.RaiseCanExecuteChanged();
        }
    }

    private string? _selectedHistoryPrompt;
    public string? SelectedHistoryPrompt
    {
        get => _selectedHistoryPrompt;
        set
        {
            _selectedHistoryPrompt = value;
            OnPropertyChanged();
            ReuseHistoryPromptCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task SendAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.CurrentThreadId))
        {
            _codexProcessService.ResetThread();
        }

        SaveSettings();
        AddPromptToHistory(Prompt);
        ClearPersistedEventMessages();
        AddUserMessage(Prompt.Trim());

        IsBusy = true;
        _cts = new CancellationTokenSource();
        _currentAssistantMessage = null;
        ClearTransientStatusMessage();
        var promptToSend = Prompt;
        Prompt = string.Empty;

        try
        {
            var exitCode = await _codexProcessService.ExecuteAsync(
                promptToSend,
                Settings,
                AttachedImages.ToList(),
                onOutput: AppendAssistantOutput,
                onError: AppendStderr,
                onEventMessage: AddRuntimeEventMessage,
                onTokenUsage: UpdateTokenUsage,
                cancellationToken: _cts.Token);

            if (exitCode != 0 && _currentAssistantMessage is null)
            {
                AddAssistantMessage(_localization.CodexNoResponse);
            }

            Settings.CurrentThreadId = _codexProcessService.CurrentThreadId ?? Settings.CurrentThreadId;
            SaveSettings();
            await RefreshThreadsAsync(Settings.CurrentThreadId).ConfigureAwait(false);
            await RefreshServerSurfacesAsync().ConfigureAwait(false);
            AppendOutput($"{Environment.NewLine}[exit code: {exitCode}]{Environment.NewLine}");
        }
        catch (OperationCanceledException)
        {
            AddAssistantMessage(_localization.ExecutionCanceled);
            AppendOutput($"{Environment.NewLine}[cancelado]{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            AddAssistantMessage(_localization.ExecutionError);
            AppendOutput($"{Environment.NewLine}[erro] {ex.Message}{Environment.NewLine}");
        }
        finally
        {
            ClearTransientStatusMessage();
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Send()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(SendAsync);
    }

    private void SaveSettings()
    {
        Settings.DefaultModel = SelectedModel;
        Settings.ReasoningEffort = SelectedReasoningEffort;
        Settings.ModelVerbosity = SelectedVerbosity;
        Settings.ApprovalPolicy = SelectedApprovalPolicy;
        Settings.SandboxMode = SelectedSandboxMode;
        _settingsStore.Save(Settings);
    }

    private void Cancel()
    {
        _approvalDecisionTcs?.TrySetResult(JValue.CreateString("cancel"));
        CurrentApprovalPrompt = null;
        _approvalDecisionTcs = null;
        _userInputDecisionTcs?.TrySetResult(new JObject { ["answers"] = new JObject() });
        CurrentUserInputPrompt = null;
        _userInputDecisionTcs = null;
        _cts?.Cancel();
    }

    public void PasteImageFromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                return;
            }

            var image = Clipboard.GetImage();
            if (image is null)
            {
                return;
            }

            var filePath = SaveBitmapToTempPng(image);
            AttachedImages.Add(filePath);
            SelectedImagePath = filePath;
            UpdateContextEstimate();
        }
        catch (Exception ex)
        {
            AppendOutput(_localization.ImagePasteErrorPrefix + ex.Message + Environment.NewLine);
        }
    }

    private void AddAttachment()
    {
        var dialog = new OpenFileDialog
        {
            Filter = _localization.AllFilesFilter,
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var fileName in dialog.FileNames)
        {
            if (IsImageFile(fileName))
            {
                AttachedImages.Add(fileName);
                SelectedImagePath = fileName;
            }
            else
            {
                AppendFileReferenceToPrompt(fileName);
            }
        }

        UpdateContextEstimate();
    }

    private void RemoveSelectedImage()
    {
        if (SelectedImagePath is null)
        {
            return;
        }

        AttachedImages.Remove(SelectedImagePath);
        SelectedImagePath = null;
        UpdateContextEstimate();
    }

    private void RemoveAttachment(object? parameter)
    {
        if (parameter is not string filePath || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        AttachedImages.Remove(filePath);
        if (string.Equals(SelectedImagePath, filePath, StringComparison.Ordinal))
        {
            SelectedImagePath = null;
        }

        UpdateContextEstimate();
    }

    private void UseSolutionDirectory()
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Settings.WorkingDirectory = _solutionContextService.GetBestWorkingDirectory();
            OnPropertyChanged(nameof(Settings));
            await RefreshThreadsAsync(Settings.CurrentThreadId).ConfigureAwait(false);
            await RefreshModelOptionsAsync().ConfigureAwait(false);
            await RefreshServerSurfacesAsync().ConfigureAwait(false);
        });
    }

    private void NormalizeSelectionSettings()
    {
        Settings.DefaultModel = EnsureOptionValue(NormalizeModelValue(Settings.DefaultModel), CreateFallbackModelOptions(), "gpt-5-codex");
        Settings.ReasoningEffort = EnsureOptionValue(NormalizeReasoningEffortValue(Settings.ReasoningEffort), ReasoningOptions, "high");
        Settings.ModelVerbosity = EnsureOptionValue(Settings.ModelVerbosity, VerbosityOptions, "medium");
        Settings.SandboxMode = EnsureOptionValue(Settings.SandboxMode, SandboxModeOptions, "read-only");
        Settings.ApprovalPolicy = EnsureOptionValue(Settings.ApprovalPolicy, ApprovalPolicyOptions, string.Empty);
    }

    private static string EnsureOptionValue(string? currentValue, IEnumerable<SelectionOption> options, string fallbackValue)
    {
        var normalized = (currentValue ?? string.Empty).Trim();
        if (options.Any(option => string.Equals(option.Value, normalized, StringComparison.Ordinal)))
        {
            return normalized;
        }

        if (options.Any(option => string.Equals(option.Value, fallbackValue, StringComparison.Ordinal)))
        {
            return fallbackValue;
        }

        return options.FirstOrDefault()?.Value ?? fallbackValue;
    }

    private static string NormalizeModelValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        switch (normalized.ToLowerInvariant())
        {
            case "gpt-5.4":
            case "gpt-5.4-codex":
            case "gpt-5-codex":
                return "gpt-5-codex";
            case "gpt-5.2 codex":
            case "gpt-5.2-codex":
                return "gpt-5.2-codex";
            default:
                return normalized;
        }
    }

    private static string NormalizeReasoningEffortValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "minimum":
            case "min":
                return "minimal";
            case "maximum":
            case "max":
                return "xhigh";
            default:
                return normalized;
        }
    }

    private static string GetOptionLabel(IEnumerable<SelectionOption> options, string? value, string fallbackLabel)
    {
        var normalized = (value ?? string.Empty).Trim();
        var label = options.FirstOrDefault(option => string.Equals(option.Value, normalized, StringComparison.Ordinal))?.Label;
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return string.IsNullOrWhiteSpace(normalized) ? fallbackLabel : normalized;
    }

    private void OpenCodexConfig()
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContextService.OpenCodexConfig();
        });
    }

    private void ToggleHistoryPanel()
    {
        ShowHistoryPanel = !ShowHistoryPanel;
        if (ShowHistoryPanel)
        {
            ShowSettingsPanel = false;
        }
    }

    private void ToggleSettingsPanel()
    {
        ShowSettingsPanel = !ShowSettingsPanel;
        if (ShowSettingsPanel)
        {
            ShowHistoryPanel = false;
        }
    }

    private async Task InitializeAsync()
    {
        await RefreshModelOptionsAsync().ConfigureAwait(false);
        await RefreshThreadsAsync(Settings.CurrentThreadId).ConfigureAwait(false);
        await RefreshServerSurfacesAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Settings.CurrentThreadId))
        {
            await OpenThreadAsync(Settings.CurrentThreadId).ConfigureAwait(false);
        }
    }

    private async Task RefreshModelOptionsAsync()
    {
        try
        {
            var models = await _codexProcessService.ListModelsAsync(Settings, CancellationToken.None).ConfigureAwait(false);
            if (models.Count == 0)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                ModelOptions.Clear();
                foreach (var model in models)
                {
                    ModelOptions.Add(model);
                }

                SelectedModel = EnsureOptionValue(SelectedModel, ModelOptions, ModelOptions[0].Value);
                OnPropertyChanged(nameof(SelectedModelLabel));
            });
        }
        catch (Exception ex)
        {
            AppendOutput(_localization.LoadModelsErrorPrefix + ex.Message + Environment.NewLine);
        }
    }

    private async Task RefreshThreadsAsync(string? preferredThreadId = null)
    {
        try
        {
            var threads = await _codexProcessService.ListThreadsAsync(Settings, CancellationToken.None).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                var selectedThreadId = preferredThreadId ?? SelectedThread?.ThreadId ?? Settings.CurrentThreadId;
                Threads.Clear();
                foreach (var thread in threads)
                {
                    Threads.Add(thread);
                }

                _suppressThreadSelection = true;
                SelectedThread = Threads.FirstOrDefault(thread => string.Equals(thread.ThreadId, selectedThreadId, StringComparison.Ordinal));
                _suppressThreadSelection = false;
            });
        }
        catch (Exception ex)
        {
            AppendOutput(_localization.LoadTopicsErrorPrefix + ex.Message + Environment.NewLine);
        }
    }

    private async Task RefreshServerSurfacesAsync()
    {
        try
        {
            var appsTask = _codexProcessService.ListAppsAsync(Settings, CancellationToken.None);
            var mcpTask = _codexProcessService.ListMcpServersAsync(Settings, CancellationToken.None);
            await Task.WhenAll(appsTask, mcpTask).ConfigureAwait(false);

            RunOnUiThread(() =>
            {
                Apps.Clear();
                foreach (var app in appsTask.Result)
                {
                    Apps.Add(app);
                }

                McpServers.Clear();
                foreach (var server in mcpTask.Result)
                {
                    McpServers.Add(server);
                }
            });
        }
        catch (Exception ex)
        {
            AppendOutput("[server] " + ex.Message + Environment.NewLine);
        }
    }

    private async Task OpenThreadAsync(string threadId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        try
        {
            var conversation = await _codexProcessService.LoadThreadConversationAsync(Settings, threadId, CancellationToken.None).ConfigureAwait(false);
            if (conversation is null)
            {
                return;
            }

            Settings.CurrentThreadId = conversation.Thread.ThreadId;
            SaveSettings();

            RunOnUiThread(() =>
            {
                _currentAssistantMessage = null;
                Messages.Clear();
                foreach (var message in conversation.Messages)
                {
                    Messages.Add(new ChatMessage(message.IsUser, message.Text, message.IsEvent, message.Title, message.Detail));
                }

                Output = string.Empty;
                ShowHistoryPanel = false;
            });

            await RefreshThreadsAsync(conversation.Thread.ThreadId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendOutput(_localization.LoadTopicsErrorPrefix + ex.Message + Environment.NewLine);
        }
    }

    private void StartNewThread()
    {
        if (IsBusy)
        {
            return;
        }

        _approvalDecisionTcs?.TrySetResult(JValue.CreateString("cancel"));
        _approvalDecisionTcs = null;
        CurrentApprovalPrompt = null;
        _currentAssistantMessage = null;
        Settings.CurrentThreadId = string.Empty;
        _codexProcessService.ResetThread();
        SaveSettings();

        RunOnUiThread(() =>
        {
            _suppressThreadSelection = true;
            SelectedThread = null;
            _suppressThreadSelection = false;
            RenameThreadName = string.Empty;
            Messages.Clear();
            Output = string.Empty;
            UpdateContextEstimate();
        });

        ThreadHelper.JoinableTaskFactory.RunAsync(() => RefreshThreadsAsync(null));
    }

    private void RenameSelectedThread()
    {
        var selectedThread = SelectedThread;
        var newName = RenameThreadName;
        if (selectedThread is null || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                await _codexProcessService.RenameThreadAsync(Settings, selectedThread.ThreadId, newName, CancellationToken.None).ConfigureAwait(false);
                await RefreshThreadsAsync(selectedThread.ThreadId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppendOutput(_localization.LoadTopicsErrorPrefix + ex.Message + Environment.NewLine);
            }
        });
    }

    private void HandleThreadCatalogChanged()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(() => RefreshThreadsAsync(Settings.CurrentThreadId));
    }

    private static IEnumerable<SelectionOption> CreateFallbackModelOptions()
    {
        return new[]
        {
            new SelectionOption("GPT-5.4", "gpt-5-codex"),
            new SelectionOption("GPT-5.2 Codex", "gpt-5.2-codex"),
            new SelectionOption("GPT-5.2", "gpt-5.2"),
            new SelectionOption("GPT-5", "gpt-5")
        };
    }

    private void RefreshMentions()
    {
        var mention = ExtractCurrentMention(Prompt);
        CurrentMentionQuery = mention;
        MentionSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(mention))
        {
            OnPropertyChanged(nameof(HasMentionSuggestions));
            return;
        }

        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (var file in _solutionContextService.FindSolutionFiles(mention))
            {
                MentionSuggestions.Add(file);
            }

            OnPropertyChanged(nameof(HasMentionSuggestions));
        });
    }

    private void InsertSelectedMention()
    {
        if (string.IsNullOrWhiteSpace(SelectedMention))
        {
            return;
        }

        var mention = ExtractCurrentMention(Prompt);
        if (string.IsNullOrWhiteSpace(mention))
        {
            return;
        }

        var suffix = "@" + mention;
        var idx = Prompt.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return;
        }

        Prompt = Prompt.Substring(0, idx) + "@" + SelectedMention + " " + Prompt.Substring(idx + suffix.Length);
        MentionSuggestions.Clear();
        CurrentMentionQuery = string.Empty;
    }

    private void ReuseHistoryPrompt()
    {
        var selectedHistoryPrompt = SelectedHistoryPrompt;
        if (!string.IsNullOrWhiteSpace(selectedHistoryPrompt))
        {
            Prompt = selectedHistoryPrompt!;
            ShowHistoryPanel = false;
        }
    }

    private async Task<JToken?> HandleApprovalRequestAsync(CodexApprovalRequest request)
    {
        var prompt = BuildApprovalPrompt(request);
        var decisionTcs = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUiThread(() =>
        {
            _approvalDecisionTcs = decisionTcs;
            CurrentApprovalPrompt = prompt;
        });

        return await decisionTcs.Task.ConfigureAwait(false);
    }

    private async Task<JObject?> HandleUserInputRequestAsync(CodexUserInputRequest request)
    {
        var prompt = BuildUserInputPrompt(request);
        var decisionTcs = new TaskCompletionSource<JObject?>(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUiThread(() =>
        {
            _userInputDecisionTcs = decisionTcs;
            CurrentUserInputPrompt = prompt;
        });

        return await decisionTcs.Task.ConfigureAwait(false);
    }

    private ApprovalPromptViewModel BuildApprovalPrompt(CodexApprovalRequest request)
    {
        var prompt = new ApprovalPromptViewModel
        {
            Title = string.Equals(request.Method, "item/fileChange/requestApproval", StringComparison.Ordinal)
                ? _localization.ApprovalFileChangeTitle
                : _localization.ApprovalCommandTitle,
            Subtitle = request.ProposedExecpolicyLabel,
            Command = request.Command,
            WorkingDirectory = request.WorkingDirectory,
            Reason = request.Reason,
            GrantRoot = request.GrantRoot
        };

        foreach (var option in request.Options)
        {
            var isDanger = string.Equals(option.Key, "decline", StringComparison.Ordinal) || string.Equals(option.Key, "cancel", StringComparison.Ordinal);
            var isPrimary = string.Equals(option.Key, "accept", StringComparison.Ordinal) || string.Equals(option.Key, "acceptForSession", StringComparison.Ordinal);
            prompt.Options.Add(new ApprovalOptionViewModel(
                _localization.GetApprovalOptionLabel(option.Key),
                option.Decision.DeepClone(),
                isPrimary,
                isDanger));
        }

        if (prompt.Options.Count == 0)
        {
            prompt.Options.Add(new ApprovalOptionViewModel(_localization.ApprovalDecline, JValue.CreateString("decline"), isDanger: true));
        }

        return prompt;
    }

    private void ResolveApproval(object? parameter)
    {
        if (parameter is not ApprovalOptionViewModel option)
        {
            return;
        }

        var tcs = _approvalDecisionTcs;
        CurrentApprovalPrompt = null;
        _approvalDecisionTcs = null;
        tcs?.TrySetResult(option.Decision.DeepClone());
    }

    private UserInputPromptViewModel BuildUserInputPrompt(CodexUserInputRequest request)
    {
        var prompt = new UserInputPromptViewModel
        {
            Title = _localization.UserInputTitle
        };

        foreach (var question in request.Questions)
        {
            var item = new UserInputQuestionViewModel
            {
                Header = question.Header,
                Id = question.Id,
                Question = question.Question,
                IsSecret = question.IsSecret,
                AcceptsText = question.IsOther || question.Options.Count == 0
            };

            foreach (var option in question.Options)
            {
                item.Options.Add(new SelectionOption(option.Label, option.Label));
            }

            if (!item.AcceptsText && item.Options.Count > 0)
            {
                item.SelectedOptionValue = item.Options[0].Value;
            }

            prompt.Questions.Add(item);
        }

        return prompt;
    }

    private void ResolveUserInput()
    {
        var prompt = CurrentUserInputPrompt;
        var tcs = _userInputDecisionTcs;
        if (prompt is null)
        {
            return;
        }

        var answers = new JObject();
        foreach (var question in prompt.Questions)
        {
            var answer = question.ResolvedAnswer;
            if (string.IsNullOrWhiteSpace(answer))
            {
                continue;
            }

            answers[question.Id] = new JObject
            {
                ["answers"] = new JArray(answer)
            };
        }

        CurrentUserInputPrompt = null;
        _userInputDecisionTcs = null;
        tcs?.TrySetResult(new JObject
        {
            ["answers"] = answers
        });
    }

    private void AddPromptToHistory(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        Settings.PromptHistory.RemoveAll(p => string.Equals(p, prompt, StringComparison.Ordinal));
        Settings.PromptHistory.Add(prompt);
        while (Settings.PromptHistory.Count > 50)
        {
            Settings.PromptHistory.RemoveAt(0);
        }

        PromptHistory.Clear();
        foreach (var item in GetRecentPromptHistory())
        {
            PromptHistory.Add(item);
        }
    }

    private System.Collections.Generic.IEnumerable<string> GetRecentPromptHistory()
    {
        var skip = Math.Max(0, Settings.PromptHistory.Count - 30);
        return Settings.PromptHistory.Skip(skip).Reverse();
    }

    private void AddUserMessage(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(new ChatMessage(true, text));
        });
    }

    private void AddAssistantMessage(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(new ChatMessage(false, text));
        });
    }

    private void AddRuntimeEventMessage(ChatMessage message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_currentTransientStatusMessage is null)
            {
                _currentTransientStatusMessage = new ChatMessage(message.IsUser, message.Text, message.IsEvent, message.Title, message.Detail);
                Messages.Add(_currentTransientStatusMessage);
            }
            else
            {
                _currentTransientStatusMessage.Title = message.Title;
                _currentTransientStatusMessage.Text = message.Text;
                _currentTransientStatusMessage.Detail = message.Detail;
            }
        });
    }

    private void AppendAssistantOutput(string text)
    {
        var chunk = text.Replace("\r", string.Empty);

        Application.Current.Dispatcher.Invoke(() =>
        {
            ClearTransientStatusMessage();
            if (_currentAssistantMessage is null)
            {
                _currentAssistantMessage = new ChatMessage(false, string.Empty);
                Messages.Add(_currentAssistantMessage);
            }

            _currentAssistantMessage.Text += chunk;
        });
    }

    private void AppendStderr(string text)
    {
        AppendOutput("[stderr] " + text);
    }

    private void UpdateTokenUsage(long totalTokens, long? contextWindow)
    {
        if (contextWindow.HasValue && contextWindow.Value > 0)
        {
            _contextTokenBudget = contextWindow.Value;
        }

        RunOnUiThread(() =>
        {
            _lastKnownRemainingTokens = Math.Max(0d, _contextTokenBudget - totalTokens);
            OnPropertyChanged(nameof(ContextTokensLabel));
        });

        SetContextRemainingRatio(Math.Max(0d, 1d - (totalTokens / _contextTokenBudget)));
    }

    private void UpdateContextEstimate()
    {
        var estimatedPromptTokens = Math.Max(1d, Prompt.Length / 4d);
        var estimatedImageTokens = AttachedImages.Count * 1200d;
        var estimated = estimatedPromptTokens + estimatedImageTokens;
        RunOnUiThread(() =>
        {
            _lastKnownRemainingTokens = Math.Max(0d, _contextTokenBudget - estimated);
            OnPropertyChanged(nameof(ContextTokensLabel));
        });
        SetContextRemainingRatio(Math.Max(0d, 1d - (estimated / _contextTokenBudget)));
    }

    private void SetContextRemainingRatio(double ratio)
    {
        ContextRingGeometry = BuildRingGeometry(ratio);
    }

    private static Geometry BuildRingGeometry(double ratio)
    {
        _ = ratio;
        var geometry = Geometry.Parse("M 8,1 A 7,7 0 1 1 7.99,1");
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private void ClearTransientStatusMessage()
    {
        RunOnUiThread(() =>
        {
            if (_currentTransientStatusMessage is null)
            {
                return;
            }

            Messages.Remove(_currentTransientStatusMessage);
            _currentTransientStatusMessage = null;
        });
    }

    private void ClearPersistedEventMessages()
    {
        RunOnUiThread(() =>
        {
            for (var index = Messages.Count - 1; index >= 0; index--)
            {
                if (Messages[index].IsEvent)
                {
                    Messages.RemoveAt(index);
                }
            }
        });
    }

    private void AppendFileReferenceToPrompt(string filePath)
    {
        var fileReference = " @" + filePath;
        Prompt = string.IsNullOrWhiteSpace(Prompt)
            ? ("@" + filePath)
            : (Prompt.TrimEnd() + fileReference);
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCompactTokenCount(double tokens)
    {
        if (tokens >= 1000d)
        {
            return (tokens / 1000d).ToString("0.#", CultureInfo.CurrentUICulture) + "k";
        }

        return Math.Round(tokens).ToString(CultureInfo.CurrentUICulture);
    }

    private static string ExtractCurrentMention(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        var atIndex = prompt.LastIndexOf('@');
        if (atIndex < 0)
        {
            return string.Empty;
        }

        var tail = prompt.Substring(atIndex + 1);
        if (tail.Contains(" ") || tail.Contains(Environment.NewLine))
        {
            return string.Empty;
        }

        return tail.Trim();
    }

    private static string SaveBitmapToTempPng(BitmapSource bitmapSource)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexVsixImages");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private void AppendOutput(string text)
    {
        RunOnUiThread(() => _output += text);
        RunOnUiThread(() => OnPropertyChanged(nameof(Output)));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
