using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private ChatMessage? _currentTransientStatusMessage;
    private bool _isBusy;
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
    private double _contextTokenBudget = DefaultContextTokenBudget;
    private double _lastKnownRemainingTokens = DefaultContextTokenBudget;
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

        SendCommand = new DelegateCommand(Send, () => !IsBusy && IsCodexReady && !string.IsNullOrWhiteSpace(BuildEffectivePrompt()));
        CancelCommand = new DelegateCommand(Cancel, () => IsBusy);
        SaveSettingsCommand = new DelegateCommand(ApplySettings);
        ClearOutputCommand = new DelegateCommand(() => Output = string.Empty);
        UseSolutionDirectoryCommand = new DelegateCommand(UseSolutionDirectory);
        OpenCodexConfigCommand = new DelegateCommand(OpenCodexConfig);
        OpenCodexSkillsFolderCommand = new DelegateCommand(OpenCodexSkillsFolder);
        OpenCodexDocsCommand = new DelegateCommand(OpenCodexDocs);
        OpenKeyboardShortcutsCommand = new DelegateCommand(OpenKeyboardShortcuts);
        OpenPathCommand = new DelegateCommand(OpenPath);
        RefreshCodexStatusCommand = new DelegateCommand(RefreshCodexStatus);
        RunCodexLoginCommand = new DelegateCommand(RunCodexLogin, _ => CanRunCodexLogin);
        CopyCodexInstallCommand = new DelegateCommand(CopyCodexInstallCommandText);
        OpenSettingsPanelCommand = new DelegateCommand(OpenSettingsPanel);
        RefreshIntegrationsCommand = new DelegateCommand(RefreshIntegrations);
        AddManagedMcpCommand = new DelegateCommand(AddManagedMcp);
        RemoveManagedMcpCommand = new DelegateCommand(RemoveManagedMcp);
        CreateSkillCommand = new DelegateCommand(CreateSkill, _ => CanCreateSkill());
        PasteImageCommand = new DelegateCommand(PasteImageFromClipboard);
        AddImageFileCommand = new DelegateCommand(AddAttachment);
        RemoveSelectedImageCommand = new DelegateCommand(RemoveSelectedImage, () => SelectedImagePath is not null);
        RemoveAttachmentCommand = new DelegateCommand(RemoveAttachment);
        RemoveDetectedPromptSkillCommand = new DelegateCommand(RemoveDetectedPromptSkill);
        InsertSelectedMentionCommand = new DelegateCommand(InsertSelectedMention, () => SelectedMention is not null);
        ReuseHistoryPromptCommand = new DelegateCommand(ReuseHistoryPrompt, () => SelectedHistoryPrompt is not null);
        NewThreadCommand = new DelegateCommand(StartNewThread, () => !IsBusy);
        RenameThreadCommand = new DelegateCommand(RenameSelectedThread, () => !IsBusy && SelectedThread is not null && !string.IsNullOrWhiteSpace(RenameThreadName));
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

        foreach (var option in CreateFallbackModelOptions())
        {
            ModelOptions.Add(option);
        }

        _selectedModel = EnsureOptionValue(_selectedModel, ModelOptions, ModelOptions.First().Value);

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

    public SelectionOption[] ReasoningOptions => _localization.CreateReasoningOptions();

    public SelectionOption[] ReasoningMenuOptions => new[]
    {
        new SelectionOption(_localization.ReasoningEffortLabel, "__label")
    }.Concat(ReasoningOptions).ToArray();

    public SelectionOption[] VerbosityOptions => _localization.CreateVerbosityOptions();

    public SelectionOption[] ServiceTierOptions => _localization.CreateServiceTierOptions();

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
    public DelegateCommand OpenCodexSkillsFolderCommand { get; }
    public DelegateCommand OpenCodexDocsCommand { get; }
    public DelegateCommand OpenKeyboardShortcutsCommand { get; }
    public DelegateCommand OpenPathCommand { get; }
    public DelegateCommand RefreshCodexStatusCommand { get; }
    public DelegateCommand RunCodexLoginCommand { get; }
    public DelegateCommand CopyCodexInstallCommand { get; }
    public DelegateCommand OpenSettingsPanelCommand { get; }
    public DelegateCommand RefreshIntegrationsCommand { get; }
    public DelegateCommand AddManagedMcpCommand { get; }
    public DelegateCommand RemoveManagedMcpCommand { get; }
    public DelegateCommand CreateSkillCommand { get; }
    public DelegateCommand PasteImageCommand { get; }
    public DelegateCommand AddImageFileCommand { get; }
    public DelegateCommand RemoveSelectedImageCommand { get; }
    public DelegateCommand RemoveAttachmentCommand { get; }
    public DelegateCommand RemoveDetectedPromptSkillCommand { get; }
    public DelegateCommand InsertSelectedMentionCommand { get; }
    public DelegateCommand ReuseHistoryPromptCommand { get; }
    public DelegateCommand NewThreadCommand { get; }
    public DelegateCommand RenameThreadCommand { get; }
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
            SendCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            NewThreadCommand.RaiseCanExecuteChanged();
            RenameThreadCommand.RaiseCanExecuteChanged();
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

    public bool IsHistoryViewSelected => ShowHistoryPanel;

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
            RunCodexLoginCommand.RaiseCanExecuteChanged();
            LogOutCommand.RaiseCanExecuteChanged();
            LogOutAndLoginCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCodexReady => CodexEnvironmentStatus.IsReady;

    public bool ShowCodexSetupCard => CodexEnvironmentStatus.Stage != CodexSetupStage.Unknown
        && CodexEnvironmentStatus.Stage != CodexSetupStage.Ready;

    public bool ShowCodexSetupDetail => !string.IsNullOrWhiteSpace(CodexSetupDetail);

    public bool ShowCodexSetupVersion => !string.IsNullOrWhiteSpace(CodexSetupVersionLabel);

    public bool NeedsCodexInstall => CodexEnvironmentStatus.Stage == CodexSetupStage.MissingExecutable;

    public bool NeedsCodexLogin => CodexEnvironmentStatus.Stage == CodexSetupStage.MissingAuthentication;

    public bool CanRunCodexLogin => CodexEnvironmentStatus.Stage == CodexSetupStage.MissingAuthentication
        && !string.IsNullOrWhiteSpace(CodexEnvironmentStatus.ResolvedExecutablePath);

    public bool CanLogOutAndLogIn => !string.IsNullOrWhiteSpace(GetLoginExecutablePath());

    public string CodexSetupTitle => CodexEnvironmentStatus.Stage switch
    {
        CodexSetupStage.Checking => _localization.SetupCheckingTitle,
        CodexSetupStage.MissingExecutable => _localization.SetupMissingExecutableTitle,
        CodexSetupStage.MissingAuthentication => _localization.SetupMissingAuthTitle,
        CodexSetupStage.Ready => _localization.SetupReadyTitle,
        CodexSetupStage.Error => _localization.SetupErrorTitle,
        _ => _localization.SetupCheckingTitle
    };

    public string CodexSetupSummary => CodexEnvironmentStatus.Stage switch
    {
        CodexSetupStage.Checking => _localization.SetupCheckingSummary,
        CodexSetupStage.MissingExecutable => _localization.SetupMissingExecutableSummary,
        CodexSetupStage.MissingAuthentication => _localization.SetupMissingAuthSummary,
        CodexSetupStage.Ready => _localization.SetupReadySummary,
        CodexSetupStage.Error => _localization.SetupErrorSummary,
        _ => string.Empty
    };

    public string CodexSetupDetail => CodexEnvironmentStatus.Stage switch
    {
        CodexSetupStage.MissingExecutable => _localization.SetupInstallDetail,
        CodexSetupStage.MissingAuthentication => _localization.SetupMissingAuthDetail,
        CodexSetupStage.Error => CodexEnvironmentStatus.ErrorDetail,
        _ => string.Empty
    };

    public string CodexSetupInstallCommand => CodexEnvironmentService.DefaultInstallCommand;

    public string CodexSetupExecutablePath => string.IsNullOrWhiteSpace(CodexEnvironmentStatus.ResolvedExecutablePath)
        ? CodexEnvironmentStatus.ConfiguredExecutablePath
        : CodexEnvironmentStatus.ResolvedExecutablePath;

    public string CodexSetupAuthenticationLabel
    {
        get
        {
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileLabel));
            OnPropertyChanged(nameof(SelectedModelLabel));
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
            OnPropertyChanged(nameof(SelectedReasoningEffortLabel));
            SaveSettings();
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
            value = EnsureOptionValue(value, ServiceTierOptions, string.Empty);
            if (string.Equals(Settings.ServiceTier, value, StringComparison.Ordinal))
            {
                return;
            }

            Settings.ServiceTier = value;
            OnPropertyChanged();
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
        "{0} used ({1} left)",
        FormatPercent(Math.Max(0d, (_contextTokenBudget - _lastKnownRemainingTokens) / _contextTokenBudget)),
        FormatPercent(Math.Max(0d, _lastKnownRemainingTokens / _contextTokenBudget)));

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

    public IEnumerable<CodexThreadSummary> RecentThreadsPreview => Threads.Take(3);

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
        }
    }

    public bool HasRateLimitData => RateLimitSummary.HasAnyData;

    public bool HasPreferredMcpServers => (Settings.PreferredMcpServers?.Count ?? 0) > 0;

    public IEnumerable<string> PreferredMcpServers => Settings.PreferredMcpServers ?? Enumerable.Empty<string>();

    public bool HasThreads => Threads.Count > 0;

    public bool HasVisibleThreads => VisibleThreads.Any();

    public bool HasVisibleLanguageOptions => VisibleLanguageOptions.Any();

    public bool HasMoreThreadsThanPreview => Threads.Count > 3;

    public bool ShowRecentTasksPreview => HasThreads && Messages.Count == 0 && !ShowHistoryPanel && !ShowSettingsPanel;

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
            AppendOutput("[setup] " + CodexSetupSummary + Environment.NewLine);
            return;
        }

        EnsureThreadMatchesWorkingDirectory();

        if (string.IsNullOrWhiteSpace(Settings.CurrentThreadId))
        {
            _codexProcessService.ResetThread();
        }

        var ideContextSummary = await CaptureIdeContextSummaryAsync().ConfigureAwait(false);

        SaveSettings();
        AddPromptToHistory(promptToSend);
        ClearPersistedEventMessages();
        AddUserMessage(promptToSend.Trim());

        IsBusy = true;
        _cts = new CancellationTokenSource();
        _currentAssistantMessage = null;
        ClearTransientStatusMessage();
        Prompt = string.Empty;

        try
        {
            var exitCode = await _codexProcessService.ExecuteAsync(
                promptToSend,
                Settings,
                AttachedImages.ToList(),
                ideContextSummary,
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
            Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
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
            AddAssistantMessage(_localization.ExecutionError + " " + ex.Message);
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
            : "Contexto atual da IDE:" + Environment.NewLine + summary;
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
        Settings.DefaultModel = EnsureOptionValue(NormalizeModelValue(Settings.DefaultModel), CreateFallbackModelOptions(), "gpt-5.4");
        Settings.ReasoningEffort = EnsureOptionValue(NormalizeReasoningEffortValue(Settings.ReasoningEffort), ReasoningOptions, "high");
        Settings.ModelVerbosity = EnsureOptionValue(Settings.ModelVerbosity, VerbosityOptions, "medium");
        Settings.ServiceTier = EnsureOptionValue(Settings.ServiceTier, ServiceTierOptions, string.Empty);
        Settings.SandboxMode = EnsureOptionValue(Settings.SandboxMode, SandboxModeOptions, "read-only");
        Settings.ApprovalPolicy = EnsureOptionValue(Settings.ApprovalPolicy, ApprovalPolicyOptions, string.Empty);
    }

    private void EnsureSettingsCollectionsInitialized()
    {
        Settings.PromptHistory ??= new List<string>();
        Settings.ManagedMcpServers ??= new List<CodexManagedMcpServer>();
        Settings.PreferredMcpServers ??= new List<string>();

        Settings.PromptHistory = Settings.PromptHistory
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

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

    private static string NormalizeModelValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        switch (normalized.ToLowerInvariant())
        {
            case "gpt-5.4-codex":
            case "gpt-5-codex":
                return "gpt-5.4";
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
        var label = options.FirstOrDefault(option => string.Equals(option.Value, normalized, StringComparison.Ordinal))?.Label;
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
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

    private void OpenSettingsPanel()
    {
        SelectedSettingsSection = string.Empty;
        ShowSettingsPanel = true;
        ShowHistoryPanel = false;
        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void OpenHistoryPanel()
    {
        ShowHistoryPanel = true;
        ShowSettingsPanel = false;
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
                AppendOutput("[auth] " + ex.Message + Environment.NewLine);
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
                AppendOutput("[auth] " + ex.Message + Environment.NewLine);
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
            Name = transport == "url" ? "novo-mcp-url" : "novo-mcp",
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
            AppendOutput("[skills] " + ex.Message + Environment.NewLine);
        }
    }

    private bool CanCreateSkill()
    {
        return SolutionContextService.IsValidSkillName(NewSkillName);
    }

    private void ToggleHistoryPanel()
    {
        var nextState = !ShowHistoryPanel;
        ShowHistoryPanel = nextState;
        if (nextState)
        {
            ShowSettingsPanel = false;
        }

        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void ToggleSettingsPanel()
    {
        var nextState = !ShowSettingsPanel;
        ShowSettingsPanel = nextState;
        if (nextState)
        {
            ShowHistoryPanel = false;
            SelectedSettingsSection = string.Empty;
        }

        OnPropertyChanged(nameof(IsHistoryViewSelected));
        OnPropertyChanged(nameof(IsSettingsViewSelected));
    }

    private void CloseSidebar()
    {
        ShowHistoryPanel = false;
        ShowSettingsPanel = false;
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
            AppendOutput("[skills] " + ex.Message + Environment.NewLine);
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
            AppendOutput("[skills-remote] " + ex.Message + Environment.NewLine);
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
            ActivityLog.TryLogError("CodexVsix", "Falha durante a inicialização assíncrona do painel do Codex." + Environment.NewLine + ex);
            AppendOutput("[init] " + ex.Message + Environment.NewLine);
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
        await RefreshCodexStatusAsync().ConfigureAwait(false);
        if (!IsCodexReady)
        {
            ClearServerSurfaces();
            return;
        }

        await RefreshModelOptionsAsync().ConfigureAwait(false);
        await RefreshThreadsAsync(Settings.CurrentThreadId).ConfigureAwait(false);
        await RefreshServerSurfacesAsync(forceSkillReload: true).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Settings.CurrentThreadId))
        {
            await OpenThreadAsync(Settings.CurrentThreadId).ConfigureAwait(false);
        }
    }

    private async Task RefreshCodexStatusAsync()
    {
        try
        {
            var status = await _codexEnvironmentService.InspectAsync(Settings, CancellationToken.None).ConfigureAwait(false);
            RunOnUiThread(() => CodexEnvironmentStatus = status);
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => CodexEnvironmentStatus = new CodexEnvironmentStatus
            {
                Stage = CodexSetupStage.Error,
                ConfiguredExecutablePath = Settings.CodexExecutablePath ?? string.Empty,
                AuthFilePath = _codexEnvironmentService.GetAuthFilePath(),
                ErrorDetail = ex.Message
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
            OnPropertyChanged(nameof(HasDetectedPromptSkills));
            OnPropertyChanged(nameof(HasRemoteSkills));
            OnPropertyChanged(nameof(VisibleSkills));
            OnPropertyChanged(nameof(VisibleRemoteSkills));
        });
    }

    private async Task RefreshModelOptionsAsync()
    {
        if (!IsCodexReady)
        {
            return;
        }

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
            AppendOutput("[server] " + ex.Message + Environment.NewLine);
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

        try
        {
            var conversation = await _codexProcessService.LoadThreadConversationAsync(Settings, threadId, CancellationToken.None).ConfigureAwait(false);
            if (conversation is null)
            {
                return;
            }

            Settings.CurrentThreadId = conversation.Thread.ThreadId;
            Settings.LastThreadWorkingDirectory = Settings.WorkingDirectory;
            SaveSettings();

            RunOnUiThread(() =>
            {
                _currentAssistantMessage = null;
                Messages.Clear();
                foreach (var message in conversation.Messages)
                {
                    Messages.Add(CreateDisplayMessage(message.IsUser, message.Text, message.IsEvent, message.Title, message.Detail));
                }

                Output = string.Empty;
                CloseSidebar();
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

    private ChatMessage CreateDisplayMessage(bool isUser, string text, bool isEvent = false, string? title = null, string? detail = null)
    {
        var message = new ChatMessage(isUser, text, isEvent, title, detail);
        DecorateUserMessageDisplay(message);
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
            if (_currentTransientStatusMessage is null)
            {
                _currentTransientStatusMessage = CreateDisplayMessage(message.IsUser, message.Text, message.IsEvent, message.Title, message.Detail);
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
            OnPropertyChanged(nameof(ContextWindowDetail));
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
            OnPropertyChanged(nameof(ContextWindowDetail));
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
