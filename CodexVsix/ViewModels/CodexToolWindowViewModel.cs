using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    private const string SettingsSectionAccount = "account";
    private const string SettingsSectionCodexMenu = "codex-menu";
    private const string SettingsSectionCodex = "codex";
    private const string SettingsSectionIde = "ide";
    private const string SettingsSectionMcp = "mcp";
    private const string SettingsSectionSkills = "skills";
    private const string SettingsSectionLanguage = "language";

    private LocalizationService _localization;
    private readonly ExtensionSettingsStore _settingsStore = new();
    private readonly CodexProcessService _codexProcessService = new();
    private readonly CodexEnvironmentService _codexEnvironmentService = new();
    private readonly SolutionContextService _solutionContextService = new();

    private CancellationTokenSource? _cts;
    private ChatMessage? _currentAssistantMessage;
    private ChatMessage? _currentPlanMessage;
    private ChatMessage? _currentTransientStatusMessage;
    private bool _isBusy;
    private bool _isStopping;
    private bool _hideRecentTasksPreview;
    private bool _pinRecentTasksPreview;
    private bool _showExpandedRecentTasksPreview;
    private bool _showHistoryPanel;
    private bool _showSettingsPanel;
    private string _prompt = string.Empty;
    private string _promptEditorText = string.Empty;
    private string _output = string.Empty;
    private string _currentMentionQuery = string.Empty;
    private string _selectedModel = string.Empty;
    private string _selectedReasoningEffort = string.Empty;
    private string _selectedVerbosity = string.Empty;
    private string _promptDisplayText = string.Empty;
    private Geometry _contextRingGeometry = Geometry.Parse("M 8,1 A 7,7 0 1 1 7.99,1");
    private const double ContextWindowBaselineTokens = 12000d;
    private double _contextTokenBudget = DefaultContextTokenBudget;
    private double _lastKnownContextTokensInWindow;
    private double _lastKnownRemainingTokens = DefaultContextTokenBudget - ContextWindowBaselineTokens;
    private ApprovalPromptViewModel? _currentApprovalPrompt;
    private TaskCompletionSource<JToken?>? _approvalDecisionTcs;
    private UserInputPromptViewModel? _currentUserInputPrompt;
    private TaskCompletionSource<JObject?>? _userInputDecisionTcs;
    private CodexThreadSummary? _selectedThread;
    private bool _suppressThreadSelection;
    private string _renameThreadName = string.Empty;
    private string _newSkillName = string.Empty;
    private string _newSkillDescription = string.Empty;
    private string _skillSearchText = string.Empty;
    private string _historySearchText = string.Empty;
    private string _languageSearchText = string.Empty;
    private string _selectedSettingsSection = string.Empty;
    private CodexRateLimitSummary _rateLimitSummary = new();
    private CodexEnvironmentStatus _codexEnvironmentStatus = new() { Stage = CodexSetupStage.Checking };
    private bool _hasCompletedEnvironmentCheck;
    private bool _hasLoadedStartupSurfaces;
    private bool _isToolWindowStartupRefreshInProgress;
    private bool _isRefreshingModels;
    private string _modelRefreshStatus = string.Empty;
    private string _customModelInput = string.Empty;
    private long _conversationStateVersion;

    public CodexToolWindowViewModel()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Settings = _settingsStore.Load();
        EnsureSettingsCollectionsInitialized();
        _localization = new LocalizationService(Settings.LanguageOverride);

        if (string.IsNullOrWhiteSpace(Settings.DefaultModel)) Settings.DefaultModel = "gpt-5.4";
        if (string.IsNullOrWhiteSpace(Settings.ReasoningEffort)) Settings.ReasoningEffort = "high";
        if (string.IsNullOrWhiteSpace(Settings.ModelVerbosity)) Settings.ModelVerbosity = "medium";
        if (string.IsNullOrWhiteSpace(Settings.SandboxMode)) Settings.SandboxMode = "read-only";
        NormalizeSelectionSettings();
        ApplyStartupWorkingDirectory();

        _selectedModel = Settings.DefaultModel;
        _selectedReasoningEffort = Settings.ReasoningEffort;
        _selectedVerbosity = Settings.ModelVerbosity;
        _codexProcessService.ApprovalRequestHandler = HandleApprovalRequestAsync;
        _codexProcessService.UserInputRequestHandler = HandleUserInputRequestAsync;
        _codexProcessService.ThreadCatalogChanged += HandleThreadCatalogChanged;
        _codexProcessService.RateLimitsUpdated += HandleRateLimitsUpdated;
        _codexProcessService.AccountUpdated += HandleAccountUpdated;

        SendCommand = new DelegateCommand(Send, () => !IsBusy && IsCodexReady && !string.IsNullOrWhiteSpace(BuildEffectivePrompt()));
        CancelCommand = new DelegateCommand(Cancel, () => IsBusy && !IsStopping);
        SaveSettingsCommand = new DelegateCommand(ApplySettings);
        ClearOutputCommand = new DelegateCommand(() => Output = string.Empty);
        UseSolutionDirectoryCommand = new DelegateCommand(UseSolutionDirectory);
        OpenCodexConfigCommand = new DelegateCommand(OpenCodexConfig);
        OpenExtensionSettingsCommand = new DelegateCommand(OpenExtensionSettings);
        OpenCodexSkillsFolderCommand = new DelegateCommand(OpenCodexSkillsFolder);
        OpenCodexDocsCommand = new DelegateCommand(OpenCodexDocs);
        OpenKeyboardShortcutsCommand = new DelegateCommand(OpenKeyboardShortcuts);
        OpenPathCommand = new DelegateCommand(OpenPath);
        OpenReferencedPathCommand = new DelegateCommand(OpenReferencedPath, CanOpenReferencedPath);
        RefreshCodexStatusCommand = new DelegateCommand(RefreshCodexStatus);
        RunCodexLoginCommand = new DelegateCommand(RunCodexLogin, _ => CanRunCodexLogin);
        CopyCodexInstallCommand = new DelegateCommand(CopyCodexInstallCommandText);
        OpenSettingsPanelCommand = new DelegateCommand(OpenSettingsPanel);
        RefreshIntegrationsCommand = new DelegateCommand(RefreshIntegrations);
        AddManagedMcpCommand = new DelegateCommand(AddManagedMcp);
        RemoveManagedMcpCommand = new DelegateCommand(RemoveManagedMcp);
        RefreshModelsCommand = new DelegateCommand(RefreshModels, _ => !IsRefreshingModels);
        AddCustomModelCommand = new DelegateCommand(AddCustomModel, _ => !string.IsNullOrWhiteSpace(CustomModelInput) || !string.IsNullOrWhiteSpace(SelectedModel));
        RemoveCustomModelCommand = new DelegateCommand(RemoveCustomModel);
        CreateSkillCommand = new DelegateCommand(CreateSkill, _ => CanCreateSkill());
        PasteImageCommand = new DelegateCommand(PasteImageFromClipboard);
        AddImageFileCommand = new DelegateCommand(AddAttachment);
        RemoveSelectedImageCommand = new DelegateCommand(RemoveSelectedImage, () => SelectedImagePath is not null);
        RemoveAttachmentCommand = new DelegateCommand(RemoveAttachment);
        RemoveDetectedPromptSkillCommand = new DelegateCommand(RemoveDetectedPromptSkill);
        InsertSelectedMentionCommand = new DelegateCommand(InsertSelectedMention, () => SelectedMention is not null);
        ReuseHistoryPromptCommand = new DelegateCommand(ReuseHistoryPrompt, () => SelectedHistoryPrompt is not null);
        NewThreadCommand = new DelegateCommand(StartNewThread, () => IsCodexReady && !IsStopping);
        DismissRecentTasksPreviewCommand = new DelegateCommand(DismissRecentTasksPreview);
        RenameThreadCommand = new DelegateCommand(RenameSelectedThread, () => !IsBusy && SelectedThread is not null && !string.IsNullOrWhiteSpace(RenameThreadName));
        DeleteThreadCommand = new DelegateCommand(DeleteThread, parameter => !IsBusy && parameter is CodexThreadSummary);
        OpenHistoryPanelCommand = new DelegateCommand(OpenHistoryPanel);
        ToggleHistoryPanelCommand = new DelegateCommand(ToggleHistoryPanel);
        ToggleSettingsPanelCommand = new DelegateCommand(ToggleSettingsPanel);
        CloseSettingsDetailCommand = new DelegateCommand(CloseSettingsDetail);
        CloseSidebarCommand = new DelegateCommand(CloseSidebar);
        SelectSettingsSectionCommand = new DelegateCommand(SelectSettingsSection);
        TogglePreferredMcpCommand = new DelegateCommand(TogglePreferredMcp);
        SelectReasoningEffortCommand = new DelegateCommand(SelectReasoningEffort);
        SelectVerbosityCommand = new DelegateCommand(SelectVerbosity);
        SelectApprovalPolicyCommand = new DelegateCommand(SelectApprovalPolicy);
        SelectSandboxModeCommand = new DelegateCommand(SelectSandboxMode);
        ToggleSkillEnabledCommand = new DelegateCommand(ToggleSkillEnabled);
        InstallRemoteSkillCommand = new DelegateCommand(InstallRemoteSkill);
        SelectLanguageCommand = new DelegateCommand(SelectLanguage);
        LogOutCommand = new DelegateCommand(LogOut, _ => CanLogOutAndLogIn);
        LogOutAndLoginCommand = new DelegateCommand(LogOutAndLogin, _ => CanLogOutAndLogIn);
        ResolveApprovalCommand = new DelegateCommand(ResolveApproval);
        ResolveUserInputCommand = new DelegateCommand(ResolveUserInput);

        ReplaceModelOptions(MergeModelOptions(
            Enumerable.Empty<SelectionOption>(),
            CreateFallbackModelOptions(),
            Settings.CustomModels,
            _selectedModel));

        foreach (var item in GetRecentPromptHistory())
        {
            PromptHistory.Add(item);
        }

        foreach (var server in Settings.ManagedMcpServers)
        {
            ManagedMcpServers.Add(CloneManagedMcpServer(server));
        }

        ManagedMcpServers.CollectionChanged += HandleManagedMcpServersChanged;
        Skills.CollectionChanged += HandleSkillsChanged;
        McpServers.CollectionChanged += HandleMcpServersChanged;
        Threads.CollectionChanged += HandleThreadsChanged;
        Messages.CollectionChanged += HandleMessagesChanged;

        RefreshMentions();
        UpdateContextEstimate();
        ThreadHelper.JoinableTaskFactory.RunAsync(InitializeSafeAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService Localization => _localization;

    public void Dispose()
    {
        _approvalDecisionTcs?.TrySetResult(JValue.CreateString("cancel"));
        _userInputDecisionTcs?.TrySetResult(new JObject { ["answers"] = new JObject() });
        _cts?.Cancel();
        _cts?.Dispose();
        ManagedMcpServers.CollectionChanged -= HandleManagedMcpServersChanged;
        Skills.CollectionChanged -= HandleSkillsChanged;
        McpServers.CollectionChanged -= HandleMcpServersChanged;
        Threads.CollectionChanged -= HandleThreadsChanged;
        Messages.CollectionChanged -= HandleMessagesChanged;
        _codexProcessService.ThreadCatalogChanged -= HandleThreadCatalogChanged;
        _codexProcessService.RateLimitsUpdated -= HandleRateLimitsUpdated;
        _codexProcessService.AccountUpdated -= HandleAccountUpdated;
        _codexProcessService.Dispose();
    }

    public CodexExtensionSettings Settings { get; }

    public ObservableCollection<string> MentionSuggestions { get; } = new();
    public ObservableCollection<string> AttachedImages { get; } = new();
    public ObservableCollection<string> PromptHistory { get; } = new();
    public ObservableCollection<CodexThreadSummary> Threads { get; } = new();
    public ObservableCollection<CodexAppSummary> Apps { get; } = new();
    public ObservableCollection<CodexMcpServerSummary> McpServers { get; } = new();
    public ObservableCollection<CodexManagedMcpServer> ManagedMcpServers { get; } = new();
    public ObservableCollection<CodexSkillSummary> Skills { get; } = new();
    public ObservableCollection<CodexRemoteSkillSummary> RemoteSkills { get; } = new();
    public ObservableCollection<CodexSkillSummary> DetectedPromptSkills { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<SelectionOption> ModelOptions { get; } = new();

    public bool IsRefreshingModels
    {
        get => _isRefreshingModels;
        private set
        {
            if (_isRefreshingModels == value)
            {
                return;
            }

            _isRefreshingModels = value;
            OnPropertyChanged();
            RefreshModelsCommand?.RaiseCanExecuteChanged();
        }
    }

    public string ModelRefreshStatus
    {
        get => _modelRefreshStatus;
        private set
        {
            value ??= string.Empty;
            if (string.Equals(_modelRefreshStatus, value, StringComparison.Ordinal))
            {
                return;
            }

            _modelRefreshStatus = value;
            OnPropertyChanged();
        }
    }

    public string CustomModelInput
    {
        get => _customModelInput;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_customModelInput, value, StringComparison.Ordinal))
            {
                return;
            }

            _customModelInput = value;
            OnPropertyChanged();
            AddCustomModelCommand?.RaiseCanExecuteChanged();
        }
    }

    public SelectionOption[] ReasoningOptions => MergeConfigurableOptions(
        _localization.CreateReasoningOptions(),
        Settings.CustomReasoningEfforts,
        SelectedReasoningEffort).ToArray();

    public SelectionOption[] ReasoningMenuOptions => new[]
    {
        new SelectionOption(_localization.ReasoningEffortLabel, "__label")
    }.Concat(ReasoningOptions).ToArray();

    public SelectionOption[] VerbosityOptions => MergeConfigurableOptions(
        _localization.CreateVerbosityOptions(),
        Settings.CustomVerbosityOptions,
        SelectedVerbosity).ToArray();

    public SelectionOption[] ServiceTierOptions => MergeConfigurableOptions(
        _localization.CreateServiceTierOptions(),
        Settings.CustomServiceTiers,
        SelectedServiceTier).ToArray();

    public SelectionOption[] ApprovalPolicyOptions => _localization.CreateApprovalPolicyOptions();

    public SelectionOption[] SandboxModeOptions => _localization.CreateSandboxModeOptions();

    public SelectionOption[] LanguageOptions => _localization.CreateLanguageOptions();

    public SelectionOption[] McpTransportOptions => new[]
    {
        new SelectionOption(_localization.ManagedMcpStdioOption, "stdio"),
        new SelectionOption(_localization.ManagedMcpUrlOption, "url")
    };

    public DelegateCommand SendCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand SaveSettingsCommand { get; }
    public DelegateCommand ClearOutputCommand { get; }
    public DelegateCommand UseSolutionDirectoryCommand { get; }
    public DelegateCommand OpenCodexConfigCommand { get; }
    public DelegateCommand OpenExtensionSettingsCommand { get; }
    public DelegateCommand OpenCodexSkillsFolderCommand { get; }
    public DelegateCommand OpenCodexDocsCommand { get; }
    public DelegateCommand OpenKeyboardShortcutsCommand { get; }
    public DelegateCommand OpenPathCommand { get; }
    public DelegateCommand OpenReferencedPathCommand { get; }
    public DelegateCommand RefreshCodexStatusCommand { get; }
    public DelegateCommand RunCodexLoginCommand { get; }
    public DelegateCommand CopyCodexInstallCommand { get; }
    public DelegateCommand OpenSettingsPanelCommand { get; }
    public DelegateCommand RefreshIntegrationsCommand { get; }
    public DelegateCommand AddManagedMcpCommand { get; }
    public DelegateCommand RemoveManagedMcpCommand { get; }
    public DelegateCommand RefreshModelsCommand { get; }
    public DelegateCommand AddCustomModelCommand { get; }
    public DelegateCommand RemoveCustomModelCommand { get; }
    public DelegateCommand CreateSkillCommand { get; }
    public DelegateCommand PasteImageCommand { get; }
    public DelegateCommand AddImageFileCommand { get; }
    public DelegateCommand RemoveSelectedImageCommand { get; }
    public DelegateCommand RemoveAttachmentCommand { get; }
    public DelegateCommand RemoveDetectedPromptSkillCommand { get; }
    public DelegateCommand InsertSelectedMentionCommand { get; }
    public DelegateCommand ReuseHistoryPromptCommand { get; }
    public DelegateCommand NewThreadCommand { get; }
    public DelegateCommand DismissRecentTasksPreviewCommand { get; }
    public DelegateCommand RenameThreadCommand { get; }
    public DelegateCommand DeleteThreadCommand { get; }
    public DelegateCommand OpenHistoryPanelCommand { get; }
    public DelegateCommand ToggleHistoryPanelCommand { get; }
    public DelegateCommand ToggleSettingsPanelCommand { get; }
    public DelegateCommand CloseSettingsDetailCommand { get; }
    public DelegateCommand CloseSidebarCommand { get; }
    public DelegateCommand SelectSettingsSectionCommand { get; }
    public DelegateCommand TogglePreferredMcpCommand { get; }
    public DelegateCommand SelectReasoningEffortCommand { get; }
    public DelegateCommand SelectVerbosityCommand { get; }
    public DelegateCommand SelectApprovalPolicyCommand { get; }
    public DelegateCommand SelectSandboxModeCommand { get; }
    public DelegateCommand ToggleSkillEnabledCommand { get; }
    public DelegateCommand InstallRemoteSkillCommand { get; }
    public DelegateCommand SelectLanguageCommand { get; }
    public DelegateCommand LogOutCommand { get; }
    public DelegateCommand LogOutAndLoginCommand { get; }
    public DelegateCommand ResolveApprovalCommand { get; }
    public DelegateCommand ResolveUserInputCommand { get; }

    public string Prompt
    {
        get => _prompt;
        set => RunOnUiThread(() => ApplyRawPrompt(value));
    }

    public string PromptEditorText
    {
        get => _promptEditorText;
        set => RunOnUiThread(() => ApplyPromptEditorText(value));
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
            OnPropertyChanged(nameof(PrimaryActionCommand));
            OnPropertyChanged(nameof(PrimaryActionTooltip));
            OnPropertyChanged(nameof(ShowSendActionIcon));
            OnPropertyChanged(nameof(ShowStopActionIcon));
            OnPropertyChanged(nameof(ShowStoppingIndicator));
            SendCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            NewThreadCommand.RaiseCanExecuteChanged();
            RenameThreadCommand.RaiseCanExecuteChanged();
            DeleteThreadCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (_isStopping == value)
            {
                return;
            }

            _isStopping = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PrimaryActionCommand));
            OnPropertyChanged(nameof(PrimaryActionTooltip));
            OnPropertyChanged(nameof(ShowSendActionIcon));
            OnPropertyChanged(nameof(ShowStopActionIcon));
            OnPropertyChanged(nameof(ShowStoppingIndicator));
            CancelCommand.RaiseCanExecuteChanged();
            NewThreadCommand.RaiseCanExecuteChanged();
            DeleteThreadCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ShowHistoryPanel
    {
        get => _showHistoryPanel;
        set
        {
            _showHistoryPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSidebarExpanded));
            OnPropertyChanged(nameof(SidebarWidth));
            OnPropertyChanged(nameof(IsHistoryViewSelected));
            OnPropertyChanged(nameof(IsSettingsViewSelected));
            OnPropertyChanged(nameof(ShowRecentTasksPreview));
        }
    }

    public bool ShowSettingsPanel
    {
        get => _showSettingsPanel;
        set
        {
            _showSettingsPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSidebarExpanded));
            OnPropertyChanged(nameof(SidebarWidth));
            OnPropertyChanged(nameof(IsHistoryViewSelected));
            OnPropertyChanged(nameof(IsSettingsViewSelected));
            OnPropertyChanged(nameof(ShowRecentTasksPreview));
        }
    }

    public bool IsSidebarExpanded => ShowHistoryPanel || ShowSettingsPanel;

    public double SidebarWidth => IsSidebarExpanded ? 292d : 0d;

    public bool IsHistoryViewSelected => ShowHistoryPanel || (_pinRecentTasksPreview && ShowRecentTasksPreview);

    public bool IsSettingsViewSelected => ShowSettingsPanel;

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

    public CodexEnvironmentStatus CodexEnvironmentStatus
    {
        get => _codexEnvironmentStatus;
        private set
        {
            _codexEnvironmentStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCodexReady));
            OnPropertyChanged(nameof(ShowCodexSetupCard));
            OnPropertyChanged(nameof(CodexSetupTitle));
            OnPropertyChanged(nameof(CodexSetupSummary));
            OnPropertyChanged(nameof(CodexSetupDetail));
            OnPropertyChanged(nameof(CodexSetupInstallCommand));
            OnPropertyChanged(nameof(CodexSetupExecutablePath));
            OnPropertyChanged(nameof(CodexSetupAuthenticationLabel));
            OnPropertyChanged(nameof(CodexSetupVersionLabel));
            OnPropertyChanged(nameof(ShowCodexSetupDetail));
            OnPropertyChanged(nameof(ShowCodexSetupVersion));
            OnPropertyChanged(nameof(NeedsCodexInstall));
            OnPropertyChanged(nameof(NeedsCodexLogin));
            OnPropertyChanged(nameof(CanRunCodexLogin));
            OnPropertyChanged(nameof(CurrentAccountLabel));
            OnPropertyChanged(nameof(CanLogOutAndLogIn));
            SendCommand.RaiseCanExecuteChanged();
            NewThreadCommand.RaiseCanExecuteChanged();
            RunCodexLoginCommand.RaiseCanExecuteChanged();
            LogOutCommand.RaiseCanExecuteChanged();
            LogOutAndLoginCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCodexReady => CodexEnvironmentStatus.IsReady;

    public bool HasCompletedEnvironmentCheck
    {
        get => _hasCompletedEnvironmentCheck;
        private set
        {
            if (_hasCompletedEnvironmentCheck == value)
            {
                return;
            }

            _hasCompletedEnvironmentCheck = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowCodexSetupCard));
        }
    }

    public bool ShowCodexSetupCard => HasCompletedEnvironmentCheck
        && CodexEnvironmentStatus.Stage != CodexSetupStage.Unknown
        && CodexEnvironmentStatus.Stage != CodexSetupStage.Checking
        && CodexEnvironmentStatus.Stage != CodexSetupStage.Ready;

    public bool ShowCodexSetupDetail => !string.IsNullOrWhiteSpace(CodexSetupDetail);

    public bool ShowCodexSetupVersion => !string.IsNullOrWhiteSpace(CodexSetupVersionLabel);

    public bool NeedsCodexInstall => CodexEnvironmentStatus.Stage == CodexSetupStage.MissingExecutable;

    public bool NeedsCodexLogin => CodexEnvironmentStatus.Stage == CodexSetupStage.MissingAuthentication
        && CodexEnvironmentStatus.RequiresOpenaiAuth;

    public bool CanRunCodexLogin => CodexEnvironmentStatus.Stage == CodexSetupStage.MissingAuthentication
        && CodexEnvironmentStatus.RequiresOpenaiAuth
        && !string.IsNullOrWhiteSpace(CodexEnvironmentStatus.ResolvedExecutablePath);

    public bool CanLogOutAndLogIn => !string.IsNullOrWhiteSpace(GetLoginExecutablePath());

    public string CodexSetupTitle => CodexEnvironmentStatus.Stage switch
    {
        CodexSetupStage.Checking => _localization.SetupCheckingTitle,
        CodexSetupStage.MissingExecutable => _localization.SetupMissingExecutableTitle,
        CodexSetupStage.MissingAuthentication => CodexEnvironmentStatus.RequiresOpenaiAuth
            ? _localization.SetupMissingAuthTitle
            : _localization.SetupMissingProviderAuthTitle,
        CodexSetupStage.Ready => _localization.SetupReadyTitle,
        CodexSetupStage.Error => _localization.SetupErrorTitle,
        _ => _localization.SetupCheckingTitle
    };

    public string CodexSetupSummary => CodexEnvironmentStatus.Stage switch
    {
        CodexSetupStage.Checking => _localization.SetupCheckingSummary,
        CodexSetupStage.MissingExecutable => _localization.SetupMissingExecutableSummary,
        CodexSetupStage.MissingAuthentication => CodexEnvironmentStatus.RequiresOpenaiAuth
            ? _localization.SetupMissingAuthSummary
            : _localization.SetupMissingProviderAuthSummary,
        CodexSetupStage.Ready => _localization.SetupReadySummary,
        CodexSetupStage.Error => _localization.SetupErrorSummary,
        _ => string.Empty
    };

    public string CodexSetupDetail => CodexEnvironmentStatus.Stage switch
    {
        CodexSetupStage.MissingExecutable => _localization.SetupInstallDetail,
        CodexSetupStage.MissingAuthentication => CodexEnvironmentStatus.RequiresOpenaiAuth
            ? _localization.SetupMissingAuthDetail
            : _localization.SetupMissingProviderAuthDetail,
        CodexSetupStage.Error => CodexEnvironmentStatus.ErrorDetail,
        _ => string.Empty
    };

    public string CodexSetupInstallCommand => CodexEnvironmentService.FallbackInstallCommand;

    public string CodexSetupExecutablePath => string.IsNullOrWhiteSpace(CodexEnvironmentStatus.ResolvedExecutablePath)
        ? CodexEnvironmentStatus.ConfiguredExecutablePath
        : CodexEnvironmentStatus.ResolvedExecutablePath;

    public string CodexSetupAuthenticationLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CodexEnvironmentStatus.AuthenticationLabel))
            {
                return CodexEnvironmentStatus.AuthenticationLabel;
            }

            if (CodexEnvironmentStatus.HasApiKey)
            {
                return _localization.SetupApiKeyLabel;
            }

            if (CodexEnvironmentStatus.HasAuthFile)
            {
                return _localization.SetupAuthFileLabel;
            }

            return CodexEnvironmentStatus.AuthFilePath;
        }
    }

    public string CodexSetupVersionLabel => CodexEnvironmentStatus.Version;

    public string CurrentAccountLabel
    {
        get
        {
            if (CodexEnvironmentStatus.HasAccountEmail)
            {
                return CodexEnvironmentStatus.AccountEmail;
            }

            if (!string.IsNullOrWhiteSpace(CodexEnvironmentStatus.AuthenticationLabel))
            {
                return CodexEnvironmentStatus.AuthenticationLabel;
            }

            if (CodexEnvironmentStatus.HasApiKey)
            {
                return _localization.SetupApiKeyLabel;
            }

            return _localization.NotSignedInLabel;
        }
    }

    public bool HasManagedMcpServers => ManagedMcpServers.Count > 0;

    public bool HasDetectedMcpServers => McpServers.Count > 0;

    public bool HasSkills => Skills.Count > 0;

    public bool HasRemoteSkills => RemoteSkills.Count > 0;

    public bool HasDetectedPromptSkills => DetectedPromptSkills.Count > 0;

    public string PromptDisplayText
    {
        get => _promptDisplayText;
        private set
        {
            if (string.Equals(_promptDisplayText, value, StringComparison.Ordinal))
            {
                return;
            }

            _promptDisplayText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPromptDisplayText));
        }
    }

    public bool HasPromptDisplayText => !string.IsNullOrWhiteSpace(PromptDisplayText);

    public string CodexConfigPath => _solutionContextService.GetCodexConfigPath();

    public string ExtensionSettingsPath => _settingsStore.SettingsFilePath;

    public string CodexSkillsDirectory => _solutionContextService.GetCodexSkillsDirectory();

    public string SelectedSettingsSection
    {
        get => _selectedSettingsSection;
        private set
        {
            if (string.Equals(_selectedSettingsSection, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedSettingsSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAccountSectionSelected));
            OnPropertyChanged(nameof(IsCodexMenuExpanded));
            OnPropertyChanged(nameof(IsCodexSectionSelected));
            OnPropertyChanged(nameof(IsIdeSectionSelected));
            OnPropertyChanged(nameof(IsMcpSectionSelected));
            OnPropertyChanged(nameof(IsSkillsSectionSelected));
            OnPropertyChanged(nameof(IsLanguageSectionSelected));
            OnPropertyChanged(nameof(IsSettingsDetailPanelVisible));
            OnPropertyChanged(nameof(SelectedSettingsSectionTitle));
        }
    }

    public bool IsAccountSectionSelected => string.Equals(SelectedSettingsSection, SettingsSectionAccount, StringComparison.Ordinal);

    public bool IsCodexMenuExpanded => string.Equals(SelectedSettingsSection, SettingsSectionCodexMenu, StringComparison.Ordinal)
        || IsCodexSectionSelected;

    public bool IsCodexSectionSelected => string.Equals(SelectedSettingsSection, SettingsSectionCodex, StringComparison.Ordinal);

    public bool IsIdeSectionSelected => string.Equals(SelectedSettingsSection, SettingsSectionIde, StringComparison.Ordinal);

    public bool IsMcpSectionSelected => string.Equals(SelectedSettingsSection, SettingsSectionMcp, StringComparison.Ordinal);

    public bool IsSkillsSectionSelected => string.Equals(SelectedSettingsSection, SettingsSectionSkills, StringComparison.Ordinal);

    public bool IsLanguageSectionSelected => string.Equals(SelectedSettingsSection, SettingsSectionLanguage, StringComparison.Ordinal);

    public bool IsSettingsDetailPanelVisible => IsAccountSectionSelected
        || IsCodexSectionSelected
        || IsIdeSectionSelected
        || IsMcpSectionSelected
        || IsSkillsSectionSelected;

    public string SelectedSettingsSectionTitle => SelectedSettingsSection switch
    {
        SettingsSectionAccount => Localization.AccountTitle,
        SettingsSectionCodex => Localization.CodexSettingsNav,
        SettingsSectionIde => Localization.IdeSettingsNav,
        SettingsSectionMcp => Localization.McpSettingsNav,
        SettingsSectionSkills => Localization.SkillsSettingsNav,
        _ => Localization.SettingsTitle
    };

    public string SettingsWorkspaceTitle => Localization.CodexSettingsNav;

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
            EnsureSelectedModelOption(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileLabel));
            OnPropertyChanged(nameof(SelectedModelLabel));
            AddCustomModelCommand?.RaiseCanExecuteChanged();
            SaveSettings();
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
            OnPropertyChanged(nameof(ReasoningOptions));
            OnPropertyChanged(nameof(ReasoningMenuOptions));
            OnPropertyChanged(nameof(SelectedReasoningEffortLabel));
            SaveSettings();
        }
    }

    public string SelectedVerbosity
    {
        get => _selectedVerbosity;
        set
        {
            value = EnsureKnownOrCustomOptionValue(value, VerbosityOptions, "medium");
            if (string.Equals(_selectedVerbosity, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedVerbosity = value;
            Settings.ModelVerbosity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VerbosityOptions));
            OnPropertyChanged(nameof(SelectedVerbosityLabel));
            SaveSettings();
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
            SaveSettings();
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
            OnPropertyChanged(nameof(SelectedApprovalPolicyLabel));
            SaveSettings();
        }
    }

    public string ProfileLabel => string.IsNullOrWhiteSpace(Settings.Profile) ? "develop" : Settings.Profile;

    public string CollaborationModeLabel => PlanModeEnabled ? _localization.AgentModeLabel : _localization.QuestionModeLabel;

    public string SelectedModelLabel => GetOptionLabel(ModelOptions, SelectedModel, ModelOptions.FirstOrDefault()?.Label ?? "gpt-5.4");

    public bool IsFastModeEnabled => string.Equals(SelectedServiceTier, "fast", StringComparison.Ordinal);

    public string SelectedReasoningEffortLabel => GetOptionLabel(ReasoningOptions, SelectedReasoningEffort, ReasoningOptions.FirstOrDefault(option => string.Equals(option.Value, "high", StringComparison.Ordinal))?.Label ?? "high");

    public string SelectedVerbosityLabel => GetOptionLabel(VerbosityOptions, SelectedVerbosity, VerbosityOptions.FirstOrDefault(option => string.Equals(option.Value, "medium", StringComparison.Ordinal))?.Label ?? "medium");

    public string SelectedServiceTier
    {
        get => Settings.ServiceTier;
        set
        {
            value = EnsureKnownOrCustomOptionValue(value, ServiceTierOptions, string.Empty);
            if (string.Equals(Settings.ServiceTier, value, StringComparison.Ordinal))
            {
                return;
            }

            Settings.ServiceTier = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ServiceTierOptions));
            OnPropertyChanged(nameof(SelectedServiceTierLabel));
            OnPropertyChanged(nameof(IsFastModeEnabled));
            SaveSettings();
        }
    }

    public string SelectedServiceTierLabel => GetOptionLabel(ServiceTierOptions, SelectedServiceTier, ServiceTierOptions.FirstOrDefault()?.Label ?? string.Empty);

    public string SelectedApprovalPolicyLabel => GetOptionLabel(ApprovalPolicyOptions, SelectedApprovalPolicy, ApprovalPolicyOptions.FirstOrDefault()?.Label ?? string.Empty);

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
            SaveSettings();
        }
    }

    public bool IncludeIdeContextEnabled
    {
        get => Settings.IncludeIdeContext;
        set
        {
            if (Settings.IncludeIdeContext == value)
            {
                return;
            }

            Settings.IncludeIdeContext = value;
            OnPropertyChanged();
            SaveSettings();
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

    public string ContextWindowDetail => string.Format(
        CultureInfo.CurrentUICulture,
        _localization.ContextWindowDetailFormat,
        FormatPercent(GetContextUsedRatio(_lastKnownContextTokensInWindow, _contextTokenBudget)),
        FormatPercent(GetContextRemainingRatio(_lastKnownContextTokensInWindow, _contextTokenBudget)));

    public string SkillSearchText
    {
        get => _skillSearchText;
        set
        {
            if (string.Equals(_skillSearchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _skillSearchText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleSkills));
            OnPropertyChanged(nameof(VisibleRemoteSkills));
        }
    }

    public string HistorySearchText
    {
        get => _historySearchText;
        set
        {
            if (string.Equals(_historySearchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _historySearchText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleThreads));
            OnPropertyChanged(nameof(HasVisibleThreads));
        }
    }

    public string LanguageSearchText
    {
        get => _languageSearchText;
        set
        {
            if (string.Equals(_languageSearchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _languageSearchText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleLanguageOptions));
            OnPropertyChanged(nameof(HasVisibleLanguageOptions));
        }
    }

    public IEnumerable<CodexSkillSummary> VisibleSkills => Skills.Where(skill =>
        string.IsNullOrWhiteSpace(SkillSearchText)
        || (skill.DisplayTitle ?? string.Empty).IndexOf(SkillSearchText, StringComparison.OrdinalIgnoreCase) >= 0
        || (skill.Name ?? string.Empty).IndexOf(SkillSearchText, StringComparison.OrdinalIgnoreCase) >= 0
        || (skill.Summary ?? string.Empty).IndexOf(SkillSearchText, StringComparison.OrdinalIgnoreCase) >= 0);

    public IEnumerable<CodexRemoteSkillSummary> VisibleRemoteSkills => RemoteSkills.Where(skill =>
        string.IsNullOrWhiteSpace(SkillSearchText)
        || (skill.Name ?? string.Empty).IndexOf(SkillSearchText, StringComparison.OrdinalIgnoreCase) >= 0
        || (skill.Description ?? string.Empty).IndexOf(SkillSearchText, StringComparison.OrdinalIgnoreCase) >= 0);

    public IEnumerable<CodexThreadSummary> VisibleThreads => Threads.Where(thread =>
        string.IsNullOrWhiteSpace(HistorySearchText)
        || (thread.Title ?? string.Empty).IndexOf(HistorySearchText, StringComparison.OrdinalIgnoreCase) >= 0
        || (thread.Subtitle ?? string.Empty).IndexOf(HistorySearchText, StringComparison.OrdinalIgnoreCase) >= 0
        || (thread.Preview ?? string.Empty).IndexOf(HistorySearchText, StringComparison.OrdinalIgnoreCase) >= 0);

    public IEnumerable<CodexThreadSummary> RecentThreadsPreview => _showExpandedRecentTasksPreview ? Threads : Threads.Take(3);

    public bool IsRecentTasksPreviewExpanded => _showExpandedRecentTasksPreview;

    public IEnumerable<SelectionOption> VisibleLanguageOptions => LanguageOptions.Where(option =>
        string.IsNullOrWhiteSpace(LanguageSearchText)
        || option.Label.IndexOf(LanguageSearchText, StringComparison.OrdinalIgnoreCase) >= 0);

    public string LanguageSearchPlaceholder => Localization.SearchLanguagesPlaceholder;

    public CodexRateLimitSummary RateLimitSummary
    {
        get => _rateLimitSummary;
        private set
        {
            _rateLimitSummary = value ?? new CodexRateLimitSummary();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRateLimitData));
            OnPropertyChanged(nameof(RateLimitEntries));
            OnPropertyChanged(nameof(ShowRateLimitUnavailableEntry));
        }
    }

    public bool HasRateLimitData => RateLimitSummary.HasAnyData;

    public IEnumerable<CodexRateLimitWindowSummary> RateLimitEntries => RateLimitSummary.Entries;

    public bool ShowRateLimitUnavailableEntry => !HasRateLimitData;

    public bool HasPreferredMcpServers => (Settings.PreferredMcpServers?.Count ?? 0) > 0;

    public IEnumerable<string> PreferredMcpServers => Settings.PreferredMcpServers ?? Enumerable.Empty<string>();

    public bool HasThreads => Threads.Count > 0;

    public bool HasVisibleThreads => VisibleThreads.Any();

    public bool HasVisibleLanguageOptions => VisibleLanguageOptions.Any();

    public bool HasMoreThreadsThanPreview => !_showExpandedRecentTasksPreview && Threads.Count > 3;

    public bool ShowRecentTasksPreview => HasThreads
        && !ShowHistoryPanel
        && !ShowSettingsPanel
        && !_hideRecentTasksPreview
        && (Messages.Count == 0 || _pinRecentTasksPreview);

    public DelegateCommand PrimaryActionCommand => IsBusy ? CancelCommand : SendCommand;

    public string PrimaryActionTooltip => IsStopping
        ? Localization.StoppingTooltip
        : (IsBusy ? Localization.StopTooltip : Localization.SendTooltip);

    public bool ShowSendActionIcon => !IsBusy;

    public bool ShowStopActionIcon => IsBusy && !IsStopping;

    public bool ShowStoppingIndicator => IsBusy && IsStopping;

    public string SelectedLanguageTag
    {
        get => NormalizeLanguageTag(Settings.LanguageOverride);
        set
        {
            var normalized = NormalizeLanguageTag(value);
            if (string.Equals(NormalizeLanguageTag(Settings.LanguageOverride), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Settings.LanguageOverride = normalized;
            ApplyLocalization(normalized);
            OnPropertyChanged();
            SaveSettings();
        }
    }

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
            RenameThreadCommand?.RaiseCanExecuteChanged();
            DeleteThreadCommand?.RaiseCanExecuteChanged();

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
            RenameThreadCommand?.RaiseCanExecuteChanged();
        }
    }

    public string NewSkillName
    {
        get => _newSkillName;
        set
        {
            _newSkillName = value;
            OnPropertyChanged();
            CreateSkillCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewSkillDescription
    {
        get => _newSkillDescription;
        set
        {
            _newSkillDescription = value;
            OnPropertyChanged();
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
        var promptToSend = BuildEffectivePrompt();
        if (IsBusy || string.IsNullOrWhiteSpace(promptToSend))
        {
            return;
        }

        if (!IsCodexReady)
        {
            AppendOutput("[" + _localization.OutputTagSetup + "] " + CodexSetupSummary + Environment.NewLine);
            return;
        }

        EnsureThreadMatchesWorkingDirectory();
        var shouldAutoNameThread = Messages.Count == 0 && SelectedThread is null;

        if (shouldAutoNameThread)
        {
            BeginConversationStateChange();
            Settings.CurrentThreadId = string.Empty;
            Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
            _codexProcessService.ResetThread();
        }

        if (string.IsNullOrWhiteSpace(Settings.CurrentThreadId))
        {
            _codexProcessService.ResetThread();
        }

        var conversationStateVersion = CaptureConversationStateVersion();

        var ideContextSummary = await CaptureIdeContextSummaryAsync().ConfigureAwait(false);

        SaveSettings();
        AddPromptToHistory(promptToSend);
        ClearPersistedEventMessages();
        AddUserMessage(promptToSend.Trim());

        IsBusy = true;
        IsStopping = false;
        _cts = new CancellationTokenSource();
        _currentAssistantMessage = null;
        _currentPlanMessage = null;
        ClearTransientStatusMessage();
        Prompt = string.Empty;

        try
        {
            var exitCode = await _codexProcessService.ExecuteAsync(
                promptToSend,
                Settings,
                AttachedImages.ToList(),
                ideContextSummary,
                onOutput: text =>
                {
                    if (IsConversationStateCurrent(conversationStateVersion))
                    {
                        AppendAssistantOutput(text);
                    }
                },
                onError: text =>
                {
                    if (IsConversationStateCurrent(conversationStateVersion))
                    {
                        AppendStderr(text);
                    }
                },
                onEventMessage: message =>
                {
                    if (IsConversationStateCurrent(conversationStateVersion))
                    {
                        AddRuntimeEventMessage(message);
                    }
                },
                onTokenUsage: (tokensInContextWindow, contextWindow) =>
                {
                    if (IsConversationStateCurrent(conversationStateVersion))
                    {
                        UpdateTokenUsage(tokensInContextWindow, contextWindow);
                    }
                },
                cancellationToken: _cts.Token);

            if (!IsConversationStateCurrent(conversationStateVersion))
            {
                return;
            }

            if (exitCode != 0 && _currentAssistantMessage is null)
            {
                AddAssistantMessage(_localization.CodexNoResponse);
            }

            Settings.CurrentThreadId = _codexProcessService.CurrentThreadId ?? Settings.CurrentThreadId;
            Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
            SaveSettings();
            await RefreshThreadsAsync(Settings.CurrentThreadId).ConfigureAwait(false);
            await EnsureCurrentThreadHasFriendlyNameAsync(shouldAutoNameThread ? promptToSend : null).ConfigureAwait(false);
            await RefreshServerSurfacesAsync().ConfigureAwait(false);
            AppendOutput($"{Environment.NewLine}[{_localization.ExitCodeLabel}: {exitCode}]{Environment.NewLine}");
        }
        catch (OperationCanceledException)
        {
            if (IsConversationStateCurrent(conversationStateVersion))
            {
                AddAssistantMessage(_localization.ExecutionCanceled);
                AppendOutput($"{Environment.NewLine}[{_localization.ExecutionCanceledTag}]{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            if (IsConversationStateCurrent(conversationStateVersion))
            {
                AddAssistantMessage(_localization.ExecutionError + " " + ex.Message);
                AppendOutput($"{Environment.NewLine}[{_localization.ExecutionErrorTag}] {ex.Message}{Environment.NewLine}");
            }
        }
        finally
        {
            if (IsConversationStateCurrent(conversationStateVersion))
            {
                ClearTransientStatusMessage();
            }

            IsStopping = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task EnsureCurrentThreadHasFriendlyNameAsync(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(Settings.CurrentThreadId) || string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        CodexThreadSummary? currentThread = null;
        RunOnUiThread(() =>
        {
            currentThread = Threads.FirstOrDefault(thread => string.Equals(thread.ThreadId, Settings.CurrentThreadId, StringComparison.Ordinal));
        });

        if (currentThread is null || !string.IsNullOrWhiteSpace(currentThread.Name))
        {
            return;
        }

        var friendlyName = BuildFriendlyThreadName(prompt);
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return;
        }

        await _codexProcessService.RenameThreadAsync(Settings, currentThread.ThreadId, friendlyName, CancellationToken.None).ConfigureAwait(false);
        await RefreshThreadsAsync(currentThread.ThreadId).ConfigureAwait(false);
    }

    private static string BuildFriendlyThreadName(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        var compact = Regex.Replace(prompt.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
        if (compact.Length <= 96)
        {
            return compact;
        }

        var shortened = compact.Substring(0, 96).TrimEnd();
        return shortened + "...";
    }

    private void Send()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(SendAsync);
    }

    private async Task<string> CaptureIdeContextSummaryAsync()
    {
        if (!IncludeIdeContextEnabled)
        {
            return string.Empty;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var summary = _solutionContextService.BuildIdeContextSummary(Settings.WorkingDirectory);
        return string.IsNullOrWhiteSpace(summary)
            ? string.Empty
            : _localization.IdeContextPrefix + Environment.NewLine + summary;
    }

    private void SaveSettings()
    {
        Settings.ManagedMcpServers = ManagedMcpServers
            .Select(CloneManagedMcpServer)
            .ToList();
        Settings.PreferredMcpServers = Settings.PreferredMcpServers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Settings.CustomModels = Settings.CustomModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => NormalizeModelValue(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Settings.CustomReasoningEfforts = NormalizeManualOptionEntries(Settings.CustomReasoningEfforts)
            .Select(NormalizeManualReasoningOptionEntry)
            .Where(entry => !string.IsNullOrWhiteSpace(ParseManualSelectionOption(entry)?.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Settings.CustomVerbosityOptions = NormalizeManualOptionEntries(Settings.CustomVerbosityOptions);
        Settings.CustomServiceTiers = NormalizeManualOptionEntries(Settings.CustomServiceTiers);
        PersistSelectedModelIfCustom();
        Settings.DefaultModel = SelectedModel;
        Settings.ReasoningEffort = SelectedReasoningEffort;
        Settings.ModelVerbosity = SelectedVerbosity;
        Settings.ServiceTier = SelectedServiceTier;
        Settings.ApprovalPolicy = SelectedApprovalPolicy;
        Settings.SandboxMode = SelectedSandboxMode;
        _settingsStore.Save(Settings);
    }

    private void ApplySettings()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            SaveSettings();
            await RefreshCodexStatusAsync().ConfigureAwait(false);
            if (!IsCodexReady)
            {
                ClearServerSurfaces();
                return;
            }

            await RefreshModelOptionsAsync().ConfigureAwait(false);
            await RefreshServerSurfacesAsync(forceSkillReload: true).ConfigureAwait(false);
        });
    }

    private void Cancel()
    {
        if (!IsBusy || IsStopping)
        {
            return;
        }

        BeginConversationStateChange();
        IsStopping = true;
        _approvalDecisionTcs?.TrySetResult(JValue.CreateString("cancel"));
        CurrentApprovalPrompt = null;
        _approvalDecisionTcs = null;
        DismissUserInputPrompt();
        var cts = _cts;
        _cts = null;
        _codexProcessService.CancelActiveTurn();
        cts?.Cancel();
        cts?.Dispose();
        ClearTransientStatusMessage();
        _currentAssistantMessage = null;
        _currentPlanMessage = null;
        IsStopping = false;
        IsBusy = false;
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
            ApplyWorkingDirectory(_solutionContextService.GetBestWorkingDirectory(), resetConversation: true);
            OnPropertyChanged(nameof(Settings));
            SaveSettings();
            await RefreshThreadsAsync(Settings.CurrentThreadId).ConfigureAwait(false);
            await RefreshModelOptionsAsync().ConfigureAwait(false);
            await RefreshServerSurfacesAsync().ConfigureAwait(false);
        });
    }

    private void ApplyStartupWorkingDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var solutionDirectory = _solutionContextService.TryGetBestWorkspaceDirectory();
        if (string.IsNullOrWhiteSpace(solutionDirectory) || !Directory.Exists(solutionDirectory))
        {
            return;
        }

        if (string.Equals(Settings.WorkingDirectory, solutionDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyWorkingDirectory(solutionDirectory, resetConversation: true);
        OnPropertyChanged(nameof(Settings));
        _settingsStore.Save(Settings);
    }

    private void ApplyWorkingDirectory(string workingDirectory, bool resetConversation)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return;
        }

        Settings.WorkingDirectory = workingDirectory;
        if (!resetConversation)
        {
            return;
        }

        BeginConversationStateChange();
        Settings.CurrentThreadId = string.Empty;
        Settings.LastThreadWorkingDirectory = workingDirectory;
        _codexProcessService.ResetThread();
        RunOnUiThread(() =>
        {
            _suppressThreadSelection = true;
            SelectedThread = null;
            _suppressThreadSelection = false;
            RenameThreadName = string.Empty;
            Messages.Clear();
            Output = string.Empty;
        });
    }

    private void EnsureThreadMatchesWorkingDirectory()
    {
        var currentWorkingDirectory = (Settings.WorkingDirectory ?? string.Empty).Trim();
        var lastThreadWorkingDirectory = (Settings.LastThreadWorkingDirectory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(currentWorkingDirectory))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.CurrentThreadId))
        {
            Settings.LastThreadWorkingDirectory = currentWorkingDirectory;
            return;
        }

        if (string.Equals(currentWorkingDirectory, lastThreadWorkingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Settings.CurrentThreadId = string.Empty;
        Settings.LastThreadWorkingDirectory = currentWorkingDirectory;
        _codexProcessService.ResetThread();
    }

    private void NormalizeSelectionSettings()
    {
        Settings.DefaultModel = NormalizeModelValue(Settings.DefaultModel);
        if (string.IsNullOrWhiteSpace(Settings.DefaultModel))
        {
            Settings.DefaultModel = "gpt-5.4";
        }

        Settings.ReasoningEffort = EnsureKnownOrCustomOptionValue(NormalizeReasoningEffortValue(Settings.ReasoningEffort), ReasoningOptions, "high");
        Settings.ModelVerbosity = EnsureKnownOrCustomOptionValue(Settings.ModelVerbosity, VerbosityOptions, "medium");
        Settings.ServiceTier = EnsureKnownOrCustomOptionValue(Settings.ServiceTier, ServiceTierOptions, string.Empty);
        Settings.SandboxMode = EnsureOptionValue(Settings.SandboxMode, SandboxModeOptions, "read-only");
        Settings.ApprovalPolicy = EnsureOptionValue(Settings.ApprovalPolicy, ApprovalPolicyOptions, string.Empty);
    }

    private void EnsureSettingsCollectionsInitialized()
    {
        Settings.PromptHistory ??= new List<string>();
        Settings.CustomModels ??= new List<string>();
        Settings.CustomReasoningEfforts ??= new List<string>();
        Settings.CustomVerbosityOptions ??= new List<string>();
        Settings.CustomServiceTiers ??= new List<string>();
        Settings.ManagedMcpServers ??= new List<CodexManagedMcpServer>();
        Settings.PreferredMcpServers ??= new List<string>();

        Settings.PromptHistory = Settings.PromptHistory
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

        Settings.CustomModels = Settings.CustomModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => NormalizeModelValue(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Settings.CustomReasoningEfforts = NormalizeManualOptionEntries(Settings.CustomReasoningEfforts)
            .Select(NormalizeManualReasoningOptionEntry)
            .Where(entry => !string.IsNullOrWhiteSpace(ParseManualSelectionOption(entry)?.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Settings.CustomVerbosityOptions = NormalizeManualOptionEntries(Settings.CustomVerbosityOptions);
        Settings.CustomServiceTiers = NormalizeManualOptionEntries(Settings.CustomServiceTiers);

        Settings.ManagedMcpServers = Settings.ManagedMcpServers
            .Where(server => server is not null)
            .ToList();

        Settings.PreferredMcpServers = Settings.PreferredMcpServers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static string EnsureKnownOrCustomOptionValue(string? currentValue, IEnumerable<SelectionOption> options, string fallbackValue)
    {
        var normalized = (currentValue ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var knownValue = options.FirstOrDefault(option => string.Equals(option.Value, normalized, StringComparison.OrdinalIgnoreCase))?.Value;
            return string.IsNullOrWhiteSpace(knownValue) ? normalized : knownValue!;
        }

        if (options.Any(option => string.Equals(option.Value, fallbackValue, StringComparison.Ordinal)))
        {
            return fallbackValue;
        }

        return options.FirstOrDefault()?.Value ?? fallbackValue;
    }

    private static IReadOnlyList<SelectionOption> MergeConfigurableOptions(
        IEnumerable<SelectionOption> defaultOptions,
        IEnumerable<string>? manualOptions,
        string selectedValue)
    {
        var result = new List<SelectionOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddOption(SelectionOption? option)
        {
            if (option is null)
            {
                return;
            }

            var value = (option.Value ?? string.Empty).Trim();
            if (!seen.Add(value))
            {
                return;
            }

            var label = string.IsNullOrWhiteSpace(option.Label) ? value : option.Label.Trim();
            result.Add(new SelectionOption(label, value));
        }

        foreach (var option in defaultOptions ?? Enumerable.Empty<SelectionOption>())
        {
            AddOption(option);
        }

        foreach (var entry in manualOptions ?? Enumerable.Empty<string>())
        {
            AddOption(ParseManualSelectionOption(entry));
        }

        var selected = (selectedValue ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            AddOption(new SelectionOption(selected + " (custom)", selected));
        }

        return result;
    }

    private static List<string> NormalizeManualOptionEntries(IEnumerable<string>? entries)
    {
        return entries?
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();
    }

    private static string NormalizeManualReasoningOptionEntry(string entry)
    {
        var option = ParseManualSelectionOption(entry);
        if (option is null)
        {
            return string.Empty;
        }

        var value = NormalizeReasoningEffortValue(option.Value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Equals(option.Label, option.Value, StringComparison.Ordinal)
            ? value
            : option.Label + "|" + value;
    }

    private static SelectionOption? ParseManualSelectionOption(string? entry)
    {
        var normalized = (entry ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var separatorIndex = normalized.IndexOf('|');
        if (separatorIndex < 0)
        {
            separatorIndex = normalized.IndexOf('=');
        }

        if (separatorIndex > 0 && separatorIndex < normalized.Length - 1)
        {
            var label = normalized.Substring(0, separatorIndex).Trim();
            var value = normalized.Substring(separatorIndex + 1).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return new SelectionOption(string.IsNullOrWhiteSpace(label) ? value : label, value);
            }
        }

        return new SelectionOption(normalized, normalized);
    }

    private static string NormalizeModelValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.Equals(normalized, "gpt-5.2 codex", StringComparison.OrdinalIgnoreCase)
            ? "gpt-5.2-codex"
            : normalized;
    }

    private static string NormalizeReasoningEffortValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "minimum":
            case "min":
            case "minimal":
                return "minimal";
            case "maximum":
            case "max":
                return "xhigh";
            default:
                return normalized;
        }
    }

    private static string NormalizeLanguageTag(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.ToLowerInvariant() switch
        {
            "pt" => "pt-BR",
            "en-us" => "en",
            _ => normalized
        };
    }

    private static string GetOptionLabel(IEnumerable<SelectionOption> options, string? value, string fallbackLabel)
    {
        var normalized = (value ?? string.Empty).Trim();
        var label = options.FirstOrDefault(option => string.Equals(option.Value, normalized, StringComparison.OrdinalIgnoreCase))?.Label;
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label!;
        }

        return string.IsNullOrWhiteSpace(normalized) ? fallbackLabel : normalized;
    }

    private string GetLoginExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(CodexEnvironmentStatus.ResolvedExecutablePath))
        {
            return CodexEnvironmentStatus.ResolvedExecutablePath;
        }

        return Settings.CodexExecutablePath ?? string.Empty;
    }

    private void ApplyLocalization(string? languageOverride)
    {
        _localization = new LocalizationService(languageOverride);
        CultureInfo.CurrentUICulture = _localization.Culture;
        CultureInfo.CurrentCulture = _localization.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = _localization.Culture;
        CultureInfo.DefaultThreadCurrentCulture = _localization.Culture;
        CodexToolWindowManager.RefreshSettingsToolWindowCaption(_localization);
        OnPropertyChanged(nameof(Localization));
        OnPropertyChanged(nameof(ReasoningOptions));
        OnPropertyChanged(nameof(ReasoningMenuOptions));
        OnPropertyChanged(nameof(VerbosityOptions));
        OnPropertyChanged(nameof(ServiceTierOptions));
        OnPropertyChanged(nameof(ApprovalPolicyOptions));
        OnPropertyChanged(nameof(SandboxModeOptions));
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(SelectedLanguageTag));
        OnPropertyChanged(nameof(SelectedReasoningEffortLabel));
        OnPropertyChanged(nameof(SelectedVerbosityLabel));
        OnPropertyChanged(nameof(SelectedServiceTierLabel));
        OnPropertyChanged(nameof(SelectedApprovalPolicyLabel));
        OnPropertyChanged(nameof(SelectedSandboxModeLabel));
        OnPropertyChanged(nameof(CollaborationModeLabel));
        OnPropertyChanged(nameof(SelectedSettingsSectionTitle));
        OnPropertyChanged(nameof(SettingsWorkspaceTitle));
        OnPropertyChanged(nameof(CurrentAccountLabel));
        OnPropertyChanged(nameof(CodexSetupTitle));
        OnPropertyChanged(nameof(CodexSetupSummary));
        OnPropertyChanged(nameof(CodexSetupDetail));
        OnPropertyChanged(nameof(CodexSetupAuthenticationLabel));
        OnPropertyChanged(nameof(LanguageSearchPlaceholder));
        OnPropertyChanged(nameof(VisibleThreads));
        OnPropertyChanged(nameof(VisibleSkills));
        OnPropertyChanged(nameof(VisibleRemoteSkills));
        OnPropertyChanged(nameof(VisibleLanguageOptions));
        OnPropertyChanged(string.Empty);
    }

    private void OpenCodexConfig()
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContextService.OpenCodexConfig();
        });
    }

    private void OpenExtensionSettings()
    {
        SaveSettings();
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContextService.OpenPath(_settingsStore.SettingsFilePath);
        });
    }

    private void OpenSettingsPanel()
    {
        _pinRecentTasksPreview = false;
        _showExpandedRecentTasksPreview = false;
        SelectedSettingsSection = string.Empty;
        ShowSettingsPanel = true;
        ShowHistoryPanel = false;
        OnPropertyChanged(nameof(ShowRecentTasksPreview));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(IsRecentTasksPreviewExpanded));
        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void OpenHistoryPanel()
    {
        _hideRecentTasksPreview = false;
        _pinRecentTasksPreview = true;
        _showExpandedRecentTasksPreview = true;
        ShowHistoryPanel = false;
        ShowSettingsPanel = false;
        OnPropertyChanged(nameof(ShowRecentTasksPreview));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(IsRecentTasksPreviewExpanded));
        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void OpenCodexDocs()
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContextService.OpenUrl("https://openai.com/codex/get-started/");
        });
    }

    private void OpenKeyboardShortcuts()
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContextService.OpenUrl("https://learn.microsoft.com/visualstudio/ide/identifying-and-customizing-keyboard-shortcuts-in-visual-studio");
        });
    }

    private void OpenCodexSkillsFolder()
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContextService.OpenCodexSkillsDirectory();
        });
    }

    private void OpenPath(object? parameter)
    {
        if (parameter is not string path || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContextService.OpenPath(path);
        });
    }

    private void OpenReferencedPath(object? parameter)
    {
        if (parameter is not string reference || string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (TryResolveReferencedFile(reference, out var resolved))
            {
                _solutionContextService.OpenFileInVisualStudio(resolved.Path, resolved.Line, resolved.Column);
            }
        });
    }

    private bool CanOpenReferencedPath(object? parameter)
    {
        return parameter is string reference
            && TryResolveReferencedFile(reference, out _);
    }

    private bool TryResolveReferencedFile(string reference, out ReferencedFile resolved)
    {
        resolved = default;
        var normalized = NormalizeReferencedFileText(reference);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            normalized = uri.LocalPath;
        }

        var pathText = StripReferencedFilePosition(normalized, out var line, out var column);
        foreach (var candidate in GetReferencedFileCandidates(pathText))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    resolved = new ReferencedFile(fullPath, line, column);
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private IEnumerable<string> GetReferencedFileCandidates(string pathText)
    {
        if (string.IsNullOrWhiteSpace(pathText))
        {
            yield break;
        }

        if (Path.IsPathRooted(pathText))
        {
            yield return pathText;
            yield break;
        }

        var workingDirectory = (Settings.WorkingDirectory ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            yield return Path.Combine(workingDirectory, pathText);
        }

        var solutionDirectory = _solutionContextService.TryGetSolutionDirectory();
        if (!string.IsNullOrWhiteSpace(solutionDirectory)
            && !string.Equals(solutionDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(solutionDirectory, pathText);
        }
    }

    private static string NormalizeReferencedFileText(string reference)
    {
        return (reference ?? string.Empty)
            .Trim()
            .Trim('`', '\'', '"', '<', '>')
            .TrimEnd('.', ',', ';');
    }

    private static string StripReferencedFilePosition(string reference, out int? line, out int? column)
    {
        line = null;
        column = null;

        var match = Regex.Match(reference, @"^(?<path>.+?)(?::(?<line>\d+)(?::(?<column>\d+))?)$");
        if (!match.Success)
        {
            return reference;
        }

        line = int.TryParse(match.Groups["line"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLine)
            ? parsedLine
            : null;
        column = int.TryParse(match.Groups["column"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedColumn)
            ? parsedColumn
            : null;
        return match.Groups["path"].Value;
    }

    private readonly struct ReferencedFile
    {
        public ReferencedFile(string path, int? line, int? column)
        {
            Path = path;
            Line = line;
            Column = column;
        }

        public string Path { get; }

        public int? Line { get; }

        public int? Column { get; }
    }

    private void RefreshIntegrations()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            await RefreshCodexStatusAsync().ConfigureAwait(false);
            if (!IsCodexReady)
            {
                ClearServerSurfaces();
                return;
            }

            await RefreshServerSurfacesAsync(forceSkillReload: true).ConfigureAwait(false);
        });
    }

    private void RefreshCodexStatus()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(RefreshCodexStatusAsync);
    }

    private void RefreshModels(object? _)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(() => RefreshModelOptionsAsync(force: true));
    }

    private void AddCustomModel(object? _)
    {
        var model = NormalizeModelValue(string.IsNullOrWhiteSpace(CustomModelInput) ? SelectedModel : CustomModelInput);
        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        AddCustomModelToSettings(model);
        SelectedModel = model;
        CustomModelInput = string.Empty;
        ReplaceModelOptions(MergeModelOptions(
            ModelOptions,
            CreateFallbackModelOptions(),
            Settings.CustomModels,
            SelectedModel));
        ModelRefreshStatus = "Modelo personalizado adicionado.";
        SaveSettings();
    }

    private void RemoveCustomModel(object? parameter)
    {
        var model = NormalizeModelValue(parameter as string);
        if (string.IsNullOrWhiteSpace(model)
            || string.Equals(model, SelectedModel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Settings.CustomModels = Settings.CustomModels
            .Where(item => !string.Equals(item, model, StringComparison.OrdinalIgnoreCase))
            .ToList();
        ReplaceModelOptions(MergeModelOptions(
            ModelOptions,
            CreateFallbackModelOptions(),
            Settings.CustomModels,
            SelectedModel));
        SaveSettings();
    }

    private void RunCodexLogin(object? _)
    {
        var executablePath = GetLoginExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _codexEnvironmentService.LaunchLoginTerminal(executablePath);
        });
    }

    private void LogOutAndLogin(object? _)
    {
        var executablePath = GetLoginExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                _codexEnvironmentService.DeleteAuthFile(CodexEnvironmentStatus.AuthFilePath);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _codexEnvironmentService.LaunchLoginTerminal(executablePath);
                await RefreshCodexStatusAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppendOutput("[" + _localization.OutputTagAuth + "] " + ex.Message + Environment.NewLine);
            }
        });
    }

    private void LogOut(object? _)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                _codexEnvironmentService.DeleteAuthFile(CodexEnvironmentStatus.AuthFilePath);
                await RefreshCodexStatusAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppendOutput("[" + _localization.OutputTagAuth + "] " + ex.Message + Environment.NewLine);
            }
        });
    }

    private void CopyCodexInstallCommandText(object? _)
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Clipboard.SetText(CodexSetupInstallCommand);
        });
    }

    private void AddManagedMcp(object? parameter)
    {
        var transport = string.Equals(parameter as string, "url", StringComparison.OrdinalIgnoreCase)
            ? "url"
            : "stdio";

        ManagedMcpServers.Add(new CodexManagedMcpServer
        {
            Enabled = true,
            Name = transport == "url" ? _localization.ManagedMcpDefaultUrlName : _localization.ManagedMcpDefaultName,
            TransportType = transport
        });
    }

    private void RemoveManagedMcp(object? parameter)
    {
        if (parameter is CodexManagedMcpServer server)
        {
            ManagedMcpServers.Remove(server);
        }
    }

    private void CreateSkill(object? _)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(CreateSkillAsync);
    }

    private async Task CreateSkillAsync()
    {
        if (!CanCreateSkill())
        {
            return;
        }

        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var skillFile = _solutionContextService.CreateSkillTemplate(NewSkillName, NewSkillDescription);
            NewSkillName = string.Empty;
            NewSkillDescription = string.Empty;
            _codexProcessService.InvalidateSkillsCache();
            _solutionContextService.OpenPath(skillFile);
            await RefreshServerSurfacesAsync(forceSkillReload: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendOutput("[" + _localization.OutputTagSkills + "] " + ex.Message + Environment.NewLine);
        }
    }

    private bool CanCreateSkill()
    {
        return SolutionContextService.IsValidSkillName(NewSkillName);
    }

    private void ToggleHistoryPanel()
    {
        var nextState = !_pinRecentTasksPreview || !ShowRecentTasksPreview;
        _pinRecentTasksPreview = nextState;
        _hideRecentTasksPreview = !nextState;
        if (!nextState)
        {
            _showExpandedRecentTasksPreview = false;
        }

        ShowHistoryPanel = false;
        if (nextState)
        {
            ShowSettingsPanel = false;
        }

        OnPropertyChanged(nameof(ShowRecentTasksPreview));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(IsRecentTasksPreviewExpanded));
        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void ToggleSettingsPanel()
    {
        var nextState = !ShowSettingsPanel;
        _pinRecentTasksPreview = false;
        _showExpandedRecentTasksPreview = false;
        ShowSettingsPanel = nextState;
        if (nextState)
        {
            ShowHistoryPanel = false;
            SelectedSettingsSection = string.Empty;
        }

        OnPropertyChanged(nameof(ShowRecentTasksPreview));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(IsRecentTasksPreviewExpanded));
        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void CloseSidebar()
    {
        ShowHistoryPanel = false;
        ShowSettingsPanel = false;
        _pinRecentTasksPreview = false;
        _showExpandedRecentTasksPreview = false;
        OnPropertyChanged(nameof(ShowRecentTasksPreview));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(IsRecentTasksPreviewExpanded));
        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void CloseSettingsDetail()
    {
        SelectedSettingsSection = string.Empty;
    }

    private void SelectSettingsSection(object? parameter)
    {
        if (parameter is not string section || string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        if ((string.Equals(section, SettingsSectionCodexMenu, StringComparison.Ordinal)
                || string.Equals(section, SettingsSectionLanguage, StringComparison.Ordinal))
            && string.Equals(SelectedSettingsSection, section, StringComparison.Ordinal))
        {
            SelectedSettingsSection = string.Empty;
        }
        else
        {
            SelectedSettingsSection = section;
        }

        ShowSettingsPanel = true;
        ShowHistoryPanel = false;
        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));

        if (IsExternalSettingsSection(section))
        {
            PrepareExternalSettingsSection(section);
            CodexToolWindowManager.ShowSettingsToolWindow(section);
        }
    }

    public void EnsureExternalSettingsSection(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        SelectedSettingsSection = section;
        ShowSettingsPanel = false;
        ShowHistoryPanel = false;
        PrepareExternalSettingsSection(section);
    }

    private static bool IsExternalSettingsSection(string section)
    {
        return string.Equals(section, SettingsSectionAccount, StringComparison.Ordinal)
            || string.Equals(section, SettingsSectionCodex, StringComparison.Ordinal)
            || string.Equals(section, SettingsSectionMcp, StringComparison.Ordinal)
            || string.Equals(section, SettingsSectionSkills, StringComparison.Ordinal);
    }

    private void PrepareExternalSettingsSection(string section)
    {
        if (string.Equals(section, SettingsSectionMcp, StringComparison.Ordinal)
            || string.Equals(section, SettingsSectionSkills, StringComparison.Ordinal))
        {
            RefreshIntegrations();
            return;
        }

        if (string.Equals(section, SettingsSectionAccount, StringComparison.Ordinal)
            || string.Equals(section, SettingsSectionCodex, StringComparison.Ordinal))
        {
            RefreshCodexStatus();
        }
    }

    private void TogglePreferredMcp(object? parameter)
    {
        var serverName = parameter switch
        {
            CodexMcpServerSummary server => server.Name,
            string value => value,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(serverName))
        {
            return;
        }

        var preferredServers = Settings.PreferredMcpServers;
        var existingIndex = preferredServers.FindIndex(name => string.Equals(name, serverName, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            preferredServers.RemoveAt(existingIndex);
        }
        else
        {
            preferredServers.Add(serverName);
        }

        SyncMcpShortcutSelections();
        OnPropertyChanged(nameof(HasPreferredMcpServers));
        OnPropertyChanged(nameof(PreferredMcpServers));
        SaveSettings();
    }

    private void SelectReasoningEffort(object? parameter)
    {
        if (parameter is string value)
        {
            SelectedReasoningEffort = value;
        }
    }

    private void SelectVerbosity(object? parameter)
    {
        if (parameter is string value)
        {
            SelectedVerbosity = value;
        }
    }

    private void SelectApprovalPolicy(object? parameter)
    {
        if (parameter is string value)
        {
            SelectedApprovalPolicy = value;
        }
    }

    private void SelectSandboxMode(object? parameter)
    {
        if (parameter is string value)
        {
            SelectedSandboxMode = value;
        }
    }

    private void SelectLanguage(object? parameter)
    {
        if (parameter is string value)
        {
            SelectedLanguageTag = value;
        }
    }

    private void ToggleSkillEnabled(object? parameter)
    {
        if (parameter is not CodexSkillSummary skill)
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(() => ToggleSkillEnabledAsync(skill));
    }

    private async Task ToggleSkillEnabledAsync(CodexSkillSummary skill)
    {
        var requestedValue = skill.IsEnabled;

        try
        {
            var effectiveValue = await _codexProcessService.SetSkillEnabledAsync(Settings, skill.Path, requestedValue, CancellationToken.None).ConfigureAwait(false);
            RunOnUiThread(() => skill.IsEnabled = effectiveValue);
            _codexProcessService.InvalidateSkillsCache();
            await RefreshServerSurfacesAsync(forceSkillReload: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => skill.IsEnabled = !requestedValue);
            AppendOutput("[" + _localization.OutputTagSkills + "] " + ex.Message + Environment.NewLine);
        }
    }

    private void InstallRemoteSkill(object? parameter)
    {
        if (parameter is not CodexRemoteSkillSummary skill)
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(() => InstallRemoteSkillAsync(skill));
    }

    private async Task InstallRemoteSkillAsync(CodexRemoteSkillSummary skill)
    {
        try
        {
            var path = await _codexProcessService.InstallRemoteSkillAsync(Settings, skill.Id, CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _solutionContextService.OpenPath(path);
            }

            _codexProcessService.InvalidateSkillsCache();
            await RefreshServerSurfacesAsync(forceSkillReload: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendOutput("[" + _localization.OutputTagRemoteSkills + "] " + ex.Message + Environment.NewLine);
        }
    }

    private async Task InitializeSafeAsync()
    {
        try
        {
            await InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError("CodexVsix", _localization.AsyncPanelInitializeLogMessage + Environment.NewLine + ex);
            AppendOutput("[" + _localization.OutputTagInit + "] " + ex.Message + Environment.NewLine);
            CodexEnvironmentStatus = new CodexEnvironmentStatus
            {
                Stage = CodexSetupStage.Error,
                ErrorDetail = ex.Message
            };
            ClearServerSurfaces();
        }
    }

    private async Task InitializeAsync()
    {
        Settings.CurrentThreadId = string.Empty;
        Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
        _codexProcessService.ResetThread();

        await RefreshCodexStatusAsync().ConfigureAwait(false);
        if (!IsCodexReady)
        {
            ClearServerSurfaces();
            return;
        }

        await RefreshModelOptionsAsync().ConfigureAwait(false);
        await RefreshThreadsAsync(null).ConfigureAwait(false);
        await RefreshServerSurfacesAsync(forceSkillReload: true).ConfigureAwait(false);
        _hasLoadedStartupSurfaces = true;
    }

    private async Task RefreshCodexStatusAsync()
    {
        try
        {
            var status = await _codexEnvironmentService.InspectAsync(Settings, CancellationToken.None).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                CodexEnvironmentStatus = status;
                HasCompletedEnvironmentCheck = true;
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                CodexEnvironmentStatus = new CodexEnvironmentStatus
                {
                    Stage = CodexSetupStage.Error,
                    ConfiguredExecutablePath = Settings.CodexExecutablePath ?? string.Empty,
                    AuthFilePath = _codexEnvironmentService.GetAuthFilePath(Settings.EnvironmentVariables),
                    ErrorDetail = ex.Message
                };
                HasCompletedEnvironmentCheck = true;
            });
        }
    }

    private void ClearServerSurfaces()
    {
        RunOnUiThread(() =>
        {
            Apps.Clear();
            McpServers.Clear();
            Skills.Clear();
            RemoteSkills.Clear();
            DetectedPromptSkills.Clear();
            RateLimitSummary = new CodexRateLimitSummary();
            PromptDisplayText = string.Empty;
            _hasLoadedStartupSurfaces = false;
            OnPropertyChanged(nameof(HasDetectedPromptSkills));
            OnPropertyChanged(nameof(HasRemoteSkills));
            OnPropertyChanged(nameof(VisibleSkills));
            OnPropertyChanged(nameof(VisibleRemoteSkills));
        });
    }

    public void EnsureToolWindowStartupState()
    {
        if (_isToolWindowStartupRefreshInProgress)
        {
            return;
        }

        // Rehydrate status/surfaces when the tool window is shown again, but avoid
        // repeating expensive startup calls once the current session is already loaded.
        if (HasCompletedEnvironmentCheck && (IsCodexReady ? _hasLoadedStartupSurfaces : true))
        {
            return;
        }

        _isToolWindowStartupRefreshInProgress = true;
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                await RefreshCodexStatusAsync().ConfigureAwait(false);
                if (!IsCodexReady)
                {
                    ClearServerSurfaces();
                    return;
                }

                if (_hasLoadedStartupSurfaces)
                {
                    return;
                }

                await RefreshModelOptionsAsync().ConfigureAwait(false);
                await RefreshThreadsAsync(null).ConfigureAwait(false);
                await RefreshServerSurfacesAsync(forceSkillReload: false).ConfigureAwait(false);
                _hasLoadedStartupSurfaces = true;
            }
            catch (Exception ex)
            {
                AppendOutput("[" + _localization.OutputTagInit + "] " + ex.Message + Environment.NewLine);
            }
            finally
            {
                RunOnUiThread(() => _isToolWindowStartupRefreshInProgress = false);
            }
        });
    }

    private async Task RefreshModelOptionsAsync(bool force = false)
    {
        if (IsRefreshingModels && !force)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            IsRefreshingModels = true;
            ModelRefreshStatus = "Atualizando modelos...";
        });

        try
        {
            if (!IsCodexReady)
            {
                RunOnUiThread(() =>
                {
                    if (ModelOptions.Count == 0)
                    {
                        ReplaceModelOptions(MergeModelOptions(
                            Enumerable.Empty<SelectionOption>(),
                            CreateFallbackModelOptions(),
                            Settings.CustomModels,
                            SelectedModel));
                    }

                    ModelRefreshStatus = "Codex não está pronto. Mantendo modelos locais.";
                });
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var models = await _codexProcessService.ListModelsAsync(Settings, timeout.Token, Settings.IncludeHiddenModels).ConfigureAwait(false);
            if (models.Count == 0)
            {
                RunOnUiThread(() =>
                {
                    if (ModelOptions.Count == 0)
                    {
                        ReplaceModelOptions(MergeModelOptions(
                            Enumerable.Empty<SelectionOption>(),
                            CreateFallbackModelOptions(),
                            Settings.CustomModels,
                            SelectedModel));
                    }

                    ModelRefreshStatus = "Nenhum modelo remoto retornado. Mantendo lista atual.";
                });
                return;
            }

            RunOnUiThread(() =>
            {
                ReplaceModelOptions(MergeModelOptions(
                    models,
                    CreateFallbackModelOptions(),
                    Settings.CustomModels,
                    SelectedModel));
                ModelRefreshStatus = "Modelos atualizados pelo Codex.";
                OnPropertyChanged(nameof(SelectedModelLabel));
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                if (ModelOptions.Count == 0)
                {
                    ReplaceModelOptions(MergeModelOptions(
                        Enumerable.Empty<SelectionOption>(),
                        CreateFallbackModelOptions(),
                        Settings.CustomModels,
                        SelectedModel));
                }

                ModelRefreshStatus = "Falha ao atualizar modelos. Mantendo lista atual.";
            });
            AppendOutput(_localization.LoadModelsErrorPrefix + ex.Message + Environment.NewLine);
        }
        finally
        {
            RunOnUiThread(() => IsRefreshingModels = false);
        }
    }

    private static IReadOnlyList<SelectionOption> MergeModelOptions(
        IEnumerable<SelectionOption> remoteModels,
        IEnumerable<SelectionOption> fallbackModels,
        IEnumerable<string> customModels,
        string selectedModel)
    {
        var result = new List<SelectionOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackList = NormalizeOptions(fallbackModels).ToList();
        var fallbackValues = new HashSet<string>(fallbackList.Select(option => option.Value), StringComparer.OrdinalIgnoreCase);
        var selected = NormalizeModelValue(selectedModel);

        void AddOption(string? label, string? value)
        {
            value = NormalizeModelValue(value);
            if (!string.IsNullOrWhiteSpace(selected) && string.Equals(value, selected, StringComparison.OrdinalIgnoreCase))
            {
                value = selected;
            }

            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                return;
            }

            label = string.IsNullOrWhiteSpace(label) ? value : label!.Trim();
            result.Add(new SelectionOption(label, value));
        }

        foreach (var option in NormalizeOptions(remoteModels))
        {
            AddOption(option.Label, option.Value);
        }

        foreach (var model in customModels ?? Enumerable.Empty<string>())
        {
            var value = NormalizeModelValue(model);
            if (string.IsNullOrWhiteSpace(value) || fallbackValues.Contains(value))
            {
                continue;
            }

            AddOption(value + " (custom)", value);
        }

        if (!string.IsNullOrWhiteSpace(selected) && !fallbackValues.Contains(selected))
        {
            AddOption(selected + " (custom)", selected);
        }

        foreach (var option in fallbackList)
        {
            AddOption(option.Label, option.Value);
        }

        if (result.Count == 0)
        {
            foreach (var option in CreateFallbackModelOptions())
            {
                AddOption(option.Label, option.Value);
            }
        }

        return result;
    }

    private static IEnumerable<SelectionOption> NormalizeOptions(IEnumerable<SelectionOption>? options)
    {
        return options?
            .Where(option => option is not null && !string.IsNullOrWhiteSpace(option.Value))
            .Select(option =>
            {
                var value = NormalizeModelValue(option.Value);
                var label = string.IsNullOrWhiteSpace(option.Label) ? value : option.Label.Trim();
                return new SelectionOption(label, value);
            })
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            ?? Enumerable.Empty<SelectionOption>();
    }

    private void ReplaceModelOptions(IEnumerable<SelectionOption> options)
    {
        ModelOptions.Clear();
        foreach (var option in options)
        {
            ModelOptions.Add(option);
        }

        EnsureSelectedModelOption(SelectedModel);
        OnPropertyChanged(nameof(SelectedModelLabel));
    }

    private void EnsureSelectedModelOption(string? model)
    {
        var value = NormalizeModelValue(model);
        if (string.IsNullOrWhiteSpace(value)
            || ModelOptions.Any(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ModelOptions.Add(new SelectionOption(value + " (custom)", value));
    }

    private void AddCustomModelToSettings(string model)
    {
        model = NormalizeModelValue(model);
        if (string.IsNullOrWhiteSpace(model)
            || Settings.CustomModels.Any(item => string.Equals(item, model, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Settings.CustomModels.Add(model);
    }

    private void PersistSelectedModelIfCustom()
    {
        var selected = NormalizeModelValue(SelectedModel);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        if (CreateFallbackModelOptions().Any(option => string.Equals(option.Value, selected, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var existingOption = ModelOptions.FirstOrDefault(option => string.Equals(option.Value, selected, StringComparison.OrdinalIgnoreCase));
        if (existingOption is not null && !existingOption.Label.EndsWith(" (custom)", StringComparison.Ordinal))
        {
            return;
        }

        AddCustomModelToSettings(selected);
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

    private async Task RefreshServerSurfacesAsync(bool forceSkillReload = false)
    {
        if (!IsCodexReady)
        {
            ClearServerSurfaces();
            return;
        }

        try
        {
            var appsTask = _codexProcessService.ListAppsAsync(Settings, CancellationToken.None);
            var mcpTask = _codexProcessService.ListMcpServersAsync(Settings, CancellationToken.None);
            var skillsTask = _codexProcessService.ListSkillsAsync(Settings, CancellationToken.None, forceSkillReload);
            var remoteSkillsTask = GetRemoteSkillsSafeAsync();
            var rateLimitsTask = GetRateLimitsSafeAsync();
            await Task.WhenAll(appsTask, mcpTask, skillsTask, remoteSkillsTask, rateLimitsTask).ConfigureAwait(false);

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
                SyncMcpShortcutSelections();

                Skills.Clear();
                foreach (var skill in skillsTask.Result)
                {
                    Skills.Add(skill);
                }

                RemoteSkills.Clear();
                foreach (var skill in remoteSkillsTask.Result)
                {
                    if (Skills.Any(installed => string.Equals(installed.Name, skill.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    RemoteSkills.Add(skill);
                }

                RateLimitSummary = rateLimitsTask.Result;
                OnPropertyChanged(nameof(VisibleSkills));
                OnPropertyChanged(nameof(VisibleRemoteSkills));
                OnPropertyChanged(nameof(HasRemoteSkills));
            });
        }
        catch (Exception ex)
        {
            AppendOutput("[" + _localization.OutputTagServer + "] " + ex.Message + Environment.NewLine);
        }
    }

    private async Task<IReadOnlyList<CodexRemoteSkillSummary>> GetRemoteSkillsSafeAsync()
    {
        try
        {
            return await _codexProcessService.ListRemoteSkillsAsync(Settings, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<CodexRemoteSkillSummary>();
        }
    }

    private async Task<CodexRateLimitSummary> GetRateLimitsSafeAsync()
    {
        try
        {
            return await _codexProcessService.GetAccountRateLimitsAsync(Settings, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return new CodexRateLimitSummary();
        }
    }

    private async Task OpenThreadAsync(string threadId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        var conversationStateVersion = BeginConversationStateChange();

        try
        {
            var conversation = await _codexProcessService.LoadThreadConversationAsync(Settings, threadId, CancellationToken.None).ConfigureAwait(false);
            if (conversation is null || !IsConversationStateCurrent(conversationStateVersion))
            {
                return;
            }

            Settings.CurrentThreadId = conversation.Thread.ThreadId;
            Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
            SaveSettings();

            RunOnUiThread(() =>
            {
                _currentAssistantMessage = null;
                _currentPlanMessage = null;
                Messages.Clear();
                foreach (var message in conversation.Messages)
                {
                    Messages.Add(CreateDisplayMessage(message));
                }

                Output = string.Empty;
                CloseSidebar();
            });

            await RefreshThreadsAsync(conversation.Thread.ThreadId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (IsConversationStateCurrent(conversationStateVersion))
            {
                AppendOutput(_localization.LoadTopicsErrorPrefix + ex.Message + Environment.NewLine);
            }
        }
    }

    private void StartNewThread()
    {
        if (IsStopping)
        {
            return;
        }

        if (IsBusy)
        {
            Cancel();
        }

        BeginConversationStateChange();
        _hideRecentTasksPreview = true;
        _pinRecentTasksPreview = false;
        _showExpandedRecentTasksPreview = false;
        OnPropertyChanged(nameof(ShowRecentTasksPreview));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(IsRecentTasksPreviewExpanded));
        _approvalDecisionTcs?.TrySetResult(JValue.CreateString("cancel"));
        _approvalDecisionTcs = null;
        CurrentApprovalPrompt = null;
        _currentAssistantMessage = null;
        _currentPlanMessage = null;
        Settings.CurrentThreadId = string.Empty;
        Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
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
            CloseSidebar();
        });

        ThreadHelper.JoinableTaskFactory.RunAsync(() => RefreshThreadsAsync(null));
    }

    private void DismissRecentTasksPreview()
    {
        if (_hideRecentTasksPreview)
        {
            return;
        }

        _hideRecentTasksPreview = true;
        _pinRecentTasksPreview = false;
        _showExpandedRecentTasksPreview = false;
        OnPropertyChanged(nameof(ShowRecentTasksPreview));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(IsRecentTasksPreviewExpanded));
        OnPropertyChanged(nameof(IsHistoryViewSelected));
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

    private void DeleteThread(object? parameter)
    {
        if (IsBusy || parameter is not CodexThreadSummary thread)
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                await _codexProcessService.ArchiveThreadAsync(Settings, thread.ThreadId, CancellationToken.None).ConfigureAwait(false);

                if (string.Equals(Settings.CurrentThreadId, thread.ThreadId, StringComparison.Ordinal))
                {
                    BeginConversationStateChange();
                    Settings.CurrentThreadId = string.Empty;
                    Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
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
                    });
                }

                await RefreshThreadsAsync(Settings.CurrentThreadId).ConfigureAwait(false);
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
            new SelectionOption("GPT-5.4", "gpt-5.4"),
            new SelectionOption("GPT-5.2 Codex", "gpt-5.2-codex"),
            new SelectionOption("GPT-5.2", "gpt-5.2"),
            new SelectionOption("GPT-5", "gpt-5")
        };
    }

    private void RefreshMentions()
    {
        var mention = ExtractCurrentMention(PromptEditorText);
        CurrentMentionQuery = mention;
        MentionSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(mention))
        {
            OnPropertyChanged(nameof(HasMentionSuggestions));
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (var file in _solutionContextService.FindSolutionFiles(mention))
            {
                MentionSuggestions.Add(file);
            }

            OnPropertyChanged(nameof(HasMentionSuggestions));
        });
    }

    private void RefreshDetectedPromptSkills()
    {
        ApplyRawPrompt(_prompt);
    }

    private void InsertSelectedMention()
    {
        if (string.IsNullOrWhiteSpace(SelectedMention))
        {
            return;
        }

        var mention = ExtractCurrentMention(PromptEditorText);
        if (string.IsNullOrWhiteSpace(mention))
        {
            return;
        }

        var suffix = "@" + mention;
        var idx = PromptEditorText.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return;
        }

        PromptEditorText = PromptEditorText.Substring(0, idx) + "@" + SelectedMention + " " + PromptEditorText.Substring(idx + suffix.Length);
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

    private void HandleManagedMcpServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasManagedMcpServers));
    }

    private void HandleRateLimitsUpdated(CodexRateLimitSummary summary)
    {
        RunOnUiThread(() => RateLimitSummary = summary);
    }

    private void HandleAccountUpdated()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            if (!IsCodexReady)
            {
                return;
            }

            await RefreshServerSurfacesAsync(forceSkillReload: false).ConfigureAwait(false);
        });
    }

    private void HandleThreadsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasThreads));
        OnPropertyChanged(nameof(VisibleThreads));
        OnPropertyChanged(nameof(HasVisibleThreads));
        OnPropertyChanged(nameof(RecentThreadsPreview));
        OnPropertyChanged(nameof(HasMoreThreadsThanPreview));
        OnPropertyChanged(nameof(ShowRecentTasksPreview));
    }

    private void HandleMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowRecentTasksPreview));
    }

    private void HandleMcpServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncMcpShortcutSelections();
        OnPropertyChanged(nameof(HasDetectedMcpServers));
        OnPropertyChanged(nameof(HasPreferredMcpServers));
        OnPropertyChanged(nameof(PreferredMcpServers));
    }

    private void HandleSkillsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSkills));
        RefreshDetectedPromptSkills();
        RefreshDisplayedUserMessages();
        OnPropertyChanged(nameof(VisibleSkills));
    }

    private void SyncMcpShortcutSelections()
    {
        var selectedServers = new HashSet<string>(Settings.PreferredMcpServers ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var server in McpServers)
        {
            server.IsShortcutSelected = selectedServers.Contains(server.Name);
        }
    }

    private static CodexManagedMcpServer CloneManagedMcpServer(CodexManagedMcpServer? server)
    {
        if (server is null)
        {
            return new CodexManagedMcpServer();
        }

        return new CodexManagedMcpServer
        {
            Enabled = server.Enabled,
            Name = server.Name ?? string.Empty,
            TransportType = string.IsNullOrWhiteSpace(server.TransportType) ? "stdio" : server.TransportType,
            Command = server.Command ?? string.Empty,
            Arguments = server.Arguments ?? string.Empty,
            Url = server.Url ?? string.Empty
        };
    }

    private ChatMessage CreateDisplayMessage(bool isUser, string text, bool isEvent = false, string? title = null, string? detail = null, bool? supportsMarkdownText = null, bool supportsMarkdownDetail = false)
    {
        var message = new ChatMessage(isUser, text, isEvent, title, detail, supportsMarkdownText, supportsMarkdownDetail);
        DecorateUserMessageDisplay(message);
        return message;
    }

    private ChatMessage CreateDisplayMessage(ChatMessage source)
    {
        var message = CreateDisplayMessage(
            source.IsUser,
            source.Text,
            source.IsEvent,
            source.Title,
            source.Detail,
            source.SupportsMarkdownText,
            source.SupportsMarkdownDetail);
        message.RenderMarkdown = source.RenderMarkdown;
        return message;
    }

    private void RefreshDisplayedUserMessages()
    {
        RunOnUiThread(() =>
        {
            foreach (var message in Messages)
            {
                DecorateUserMessageDisplay(message);
            }
        });
    }

    private void DecorateUserMessageDisplay(ChatMessage message)
    {
        if (!message.IsUser || message.IsEvent)
        {
            message.ApplyPromptSkillDisplay(System.Array.Empty<string>(), message.Text);
            return;
        }

        var availableSkillNames = new HashSet<string>(
            Skills.Where(skill => skill.IsEnabled && !string.IsNullOrWhiteSpace(skill.Name))
                .Select(skill => skill.Name!),
            StringComparer.OrdinalIgnoreCase);

        var formattedPrompt = FormatPromptSkillDisplay(message.Text, availableSkillNames);
        message.ApplyPromptSkillDisplay(formattedPrompt.SkillNames, formattedPrompt.DisplayText);
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

    internal void DismissUserInputPrompt()
    {
        var tcs = _userInputDecisionTcs;
        CurrentUserInputPrompt = null;
        _userInputDecisionTcs = null;
        tcs?.TrySetResult(new JObject
        {
            ["answers"] = new JObject()
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
        var promptHistory = Settings.PromptHistory ?? new List<string>();
        var skip = Math.Max(0, promptHistory.Count - 30);
        return promptHistory.Skip(skip).Reverse();
    }

    private void AddUserMessage(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(CreateDisplayMessage(true, text));
        });
    }

    private void AddAssistantMessage(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Add(CreateDisplayMessage(false, text));
        });
    }

    private void AddRuntimeEventMessage(ChatMessage message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (IsPlanEvent(message))
            {
                if (_currentPlanMessage is null || !Messages.Contains(_currentPlanMessage))
                {
                    _currentPlanMessage = CreateDisplayMessage(message);
                    Messages.Add(_currentPlanMessage);
                }
                else
                {
                    _currentPlanMessage.Title = message.Title;
                    _currentPlanMessage.Text = message.Text;
                    _currentPlanMessage.Detail = message.Detail;
                }

                return;
            }

            if (_currentTransientStatusMessage is null)
            {
                _currentTransientStatusMessage = CreateDisplayMessage(message);
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
                _currentAssistantMessage = CreateDisplayMessage(false, string.Empty);
                Messages.Add(_currentAssistantMessage);
            }

            _currentAssistantMessage.Text += chunk;
        });
    }

    private void AppendStderr(string text)
    {
        AppendOutput("[" + _localization.OutputTagStderr + "] " + text);
    }

    private void UpdateTokenUsage(long tokensInContextWindow, long? contextWindow)
    {
        if (contextWindow.HasValue && contextWindow.Value > 0)
        {
            _contextTokenBudget = contextWindow.Value;
        }

        var clampedTokens = Math.Max(0d, tokensInContextWindow);

        RunOnUiThread(() =>
        {
            _lastKnownContextTokensInWindow = clampedTokens;
            _lastKnownRemainingTokens = GetContextRemainingTokenCount(clampedTokens, _contextTokenBudget);
            OnPropertyChanged(nameof(ContextTokensLabel));
            OnPropertyChanged(nameof(ContextWindowDetail));
        });

        SetContextRemainingRatio(GetContextRemainingRatio(clampedTokens, _contextTokenBudget));
    }

    private void UpdateContextEstimate()
    {
        var estimatedPromptTokens = Math.Max(1d, Prompt.Length / 4d);
        var estimatedImageTokens = AttachedImages.Count * 1200d;
        var estimated = estimatedPromptTokens + estimatedImageTokens;
        RunOnUiThread(() =>
        {
            _lastKnownContextTokensInWindow = estimated;
            _lastKnownRemainingTokens = GetContextRemainingTokenCount(estimated, _contextTokenBudget);
            OnPropertyChanged(nameof(ContextTokensLabel));
            OnPropertyChanged(nameof(ContextWindowDetail));
        });
        SetContextRemainingRatio(GetContextRemainingRatio(estimated, _contextTokenBudget));
    }

    private void SetContextRemainingRatio(double ratio)
    {
        ContextRingGeometry = BuildRingGeometry(ratio);
    }

    private static double GetContextRemainingRatio(double tokensInContextWindow, double contextWindow)
    {
        if (contextWindow <= ContextWindowBaselineTokens)
        {
            return 0d;
        }

        var effectiveWindow = contextWindow - ContextWindowBaselineTokens;
        var used = Math.Max(0d, tokensInContextWindow - ContextWindowBaselineTokens);
        var remaining = Math.Max(0d, effectiveWindow - used);
        return Math.Max(0d, Math.Min(1d, remaining / effectiveWindow));
    }

    private static double GetContextRemainingTokenCount(double tokensInContextWindow, double contextWindow)
    {
        if (contextWindow <= ContextWindowBaselineTokens)
        {
            return 0d;
        }

        var effectiveWindow = contextWindow - ContextWindowBaselineTokens;
        var used = Math.Max(0d, tokensInContextWindow - ContextWindowBaselineTokens);
        return Math.Max(0d, effectiveWindow - used);
    }

    private static double GetContextUsedRatio(double tokensInContextWindow, double contextWindow)
    {
        return 1d - GetContextRemainingRatio(tokensInContextWindow, contextWindow);
    }

    private static Geometry BuildRingGeometry(double ratio)
    {
        var clampedRatio = Math.Max(0d, Math.Min(1d, ratio));
        if (clampedRatio >= 0.9995d)
        {
            var fullCircle = Geometry.Parse("M 8,1 A 7,7 0 1 1 7.99,1");
            if (fullCircle.CanFreeze)
            {
                fullCircle.Freeze();
            }

            return fullCircle;
        }

        if (clampedRatio <= 0d)
        {
            return Geometry.Empty;
        }

        const double center = 8d;
        const double radius = 7d;
        var sweepAngle = clampedRatio * 359.999d;
        var startAngle = -90d;
        var endAngle = startAngle + sweepAngle;
        var startPoint = PointOnCircle(center, radius, startAngle);
        var endPoint = PointOnCircle(center, radius, endAngle);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(startPoint, isFilled: false, isClosed: false);
            context.ArcTo(
                endPoint,
                new Size(radius, radius),
                rotationAngle: 0d,
                isLargeArc: sweepAngle > 180d,
                sweepDirection: SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private static Point PointOnCircle(double center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(
            center + (Math.Cos(radians) * radius),
            center + (Math.Sin(radians) * radius));
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
                if (Messages[index].IsEvent && !IsPlanEvent(Messages[index]))
                {
                    Messages.RemoveAt(index);
                }
            }
        });
    }

    private bool IsPlanEvent(ChatMessage message)
    {
        return message.IsEvent
            && string.Equals(message.Title, _localization.EventPlanTitle, StringComparison.CurrentCulture);
    }

    private void AppendFileReferenceToPrompt(string filePath)
    {
        var fileReference = " @" + filePath;
        PromptEditorText = string.IsNullOrWhiteSpace(PromptEditorText)
            ? ("@" + filePath)
            : (PromptEditorText.TrimEnd() + fileReference);
    }

    private void ApplyRawPrompt(string? rawPrompt)
    {
        var availableSkills = GetAvailableSkillsByName();
        var formattedPrompt = FormatPromptSkillDisplay(rawPrompt ?? string.Empty, new HashSet<string>(availableSkills.Keys, StringComparer.OrdinalIgnoreCase), preserveWhitespace: true);

        _promptEditorText = formattedPrompt.DisplayText;
        SyncDetectedPromptSkills(formattedPrompt.SkillNames, availableSkills);
        PromptDisplayText = _promptEditorText;
        _prompt = BuildEffectivePrompt();

        OnPropertyChanged(nameof(Prompt));
        OnPropertyChanged(nameof(PromptEditorText));
        RefreshMentions();
        UpdateContextEstimate();
        SendCommand.RaiseCanExecuteChanged();
    }

    private void ApplyPromptEditorText(string? editorText)
    {
        var availableSkills = GetAvailableSkillsByName();
        var formattedPrompt = FormatPromptSkillDisplay(editorText ?? string.Empty, new HashSet<string>(availableSkills.Keys, StringComparer.OrdinalIgnoreCase), preserveWhitespace: true);
        var mergedSkillNames = DetectedPromptSkills
            .Where(skill => skill.IsEnabled && !string.IsNullOrWhiteSpace(skill.Name))
            .Select(skill => skill.Name)
            .Concat(formattedPrompt.SkillNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _promptEditorText = formattedPrompt.DisplayText;
        SyncDetectedPromptSkills(mergedSkillNames, availableSkills);
        PromptDisplayText = _promptEditorText;
        _prompt = BuildEffectivePrompt();

        OnPropertyChanged(nameof(Prompt));
        OnPropertyChanged(nameof(PromptEditorText));
        RefreshMentions();
        UpdateContextEstimate();
        SendCommand.RaiseCanExecuteChanged();
    }

    private IReadOnlyDictionary<string, CodexSkillSummary> GetAvailableSkillsByName()
    {
        return Skills
            .Where(skill => skill.IsEnabled && !string.IsNullOrWhiteSpace(skill.Name))
            .ToDictionary(skill => skill.Name!, StringComparer.OrdinalIgnoreCase);
    }

    private void SyncDetectedPromptSkills(IEnumerable<string> skillNames, IReadOnlyDictionary<string, CodexSkillSummary> availableSkills)
    {
        var selectedSkills = new List<CodexSkillSummary>();
        var uniqueSkillNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skillName in skillNames)
        {
            if (string.IsNullOrWhiteSpace(skillName) || !uniqueSkillNames.Add(skillName))
            {
                continue;
            }

            if (availableSkills.TryGetValue(skillName, out var skill))
            {
                selectedSkills.Add(skill);
            }
        }

        DetectedPromptSkills.Clear();
        foreach (var skill in selectedSkills)
        {
            DetectedPromptSkills.Add(skill);
        }

        OnPropertyChanged(nameof(HasDetectedPromptSkills));
    }

    private string BuildEffectivePrompt()
    {
        var skillPrefix = string.Join(
            " ",
            DetectedPromptSkills
                .Where(skill => skill.IsEnabled && !string.IsNullOrWhiteSpace(skill.Name))
                .Select(skill => "/" + skill.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(skillPrefix))
        {
            return _promptEditorText;
        }

        if (string.IsNullOrWhiteSpace(_promptEditorText))
        {
            return skillPrefix;
        }

        return skillPrefix + " " + _promptEditorText.TrimStart();
    }

    private void RemoveDetectedPromptSkill(object? parameter)
    {
        if (parameter is not CodexSkillSummary skillToRemove)
        {
            return;
        }

        var remainingSkills = DetectedPromptSkills
            .Where(skill => !string.Equals(skill.Name, skillToRemove.Name, StringComparison.OrdinalIgnoreCase))
            .Select(skill => skill.Name)
            .ToArray();

        SyncDetectedPromptSkills(remainingSkills, GetAvailableSkillsByName());
        _prompt = BuildEffectivePrompt();
        OnPropertyChanged(nameof(Prompt));
        UpdateContextEstimate();
        SendCommand.RaiseCanExecuteChanged();
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

    private static string FormatPercent(double value)
    {
        return Math.Round(value * 100d).ToString("0", CultureInfo.CurrentUICulture) + "%";
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

    private static IReadOnlyList<string> ExtractPromptSkillNames(string prompt)
    {
        var detectedNames = new List<string>();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return detectedNames;
        }

        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < prompt.Length; index++)
        {
            if (prompt[index] != '/')
            {
                continue;
            }

            if (index > 0 && !char.IsWhiteSpace(prompt[index - 1]))
            {
                continue;
            }

            var start = index + 1;
            var end = start;
            while (end < prompt.Length && IsSkillNameCharacter(prompt[end]))
            {
                end++;
            }

            if (end <= start)
            {
                continue;
            }

            var skillName = prompt.Substring(start, end - start);
            if (uniqueNames.Add(skillName))
            {
                detectedNames.Add(skillName);
            }
        }

        return detectedNames;
    }

    private static (IReadOnlyList<string> SkillNames, string DisplayText) FormatPromptSkillDisplay(string prompt, ISet<string> availableSkillNames, bool preserveWhitespace = false)
    {
        var detectedNames = new List<string>();
        if (string.IsNullOrWhiteSpace(prompt) || availableSkillNames.Count == 0)
        {
            return (detectedNames, prompt);
        }

        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new System.Text.StringBuilder(prompt.Length);

        for (var index = 0; index < prompt.Length; index++)
        {
            if (prompt[index] == '/'
                && (index == 0 || char.IsWhiteSpace(prompt[index - 1])))
            {
                var start = index + 1;
                var end = start;
                while (end < prompt.Length && IsSkillNameCharacter(prompt[end]))
                {
                    end++;
                }

                if (end > start)
                {
                    var skillName = prompt.Substring(start, end - start);
                    if (availableSkillNames.Contains(skillName))
                    {
                        if (uniqueNames.Add(skillName))
                        {
                            detectedNames.Add(skillName);
                        }

                        index = preserveWhitespace && end < prompt.Length && (prompt[end] == ' ' || prompt[end] == '\t')
                            ? end
                            : end - 1;
                        continue;
                    }
                }
            }

            builder.Append(prompt[index]);
        }

        return (detectedNames, preserveWhitespace ? builder.ToString() : CleanupPromptDisplayText(builder.ToString()));
    }

    private static string CleanupPromptDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var cleanedLines = lines
            .Select(CollapseInlineWhitespace)
            .ToArray();

        return string.Join(Environment.NewLine, cleanedLines).Trim();
    }

    private static string CollapseInlineWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var previousWasWhitespace = false;

        foreach (var character in text)
        {
            if (character == ' ' || character == '\t')
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool IsSkillNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '-' || value == '_' || value == '.';
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

    private long CaptureConversationStateVersion()
    {
        return Interlocked.Read(ref _conversationStateVersion);
    }

    private long BeginConversationStateChange()
    {
        return Interlocked.Increment(ref _conversationStateVersion);
    }

    private bool IsConversationStateCurrent(long version)
    {
        return CaptureConversationStateVersion() == version;
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
