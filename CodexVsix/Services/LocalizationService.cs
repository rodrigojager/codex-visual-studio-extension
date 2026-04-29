using System.Collections.Generic;
using System.Globalization;
using CodexVsix.Models;

namespace CodexVsix.Services;

public sealed class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> EnglishStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Topics",
        ["HistoryTitle"] = "History",
        ["HistoryTopicsTitle"] = "Previous topics",
        ["UseButton"] = "Use",
        ["NewTopicButton"] = "New topic",
        ["RenameTopicButton"] = "Rename",
        ["RenameTopicPlaceholder"] = "Rename selected topic",
        ["CloseButton"] = "Close",
        ["AccountTitle"] = "Account",
        ["AccountSubtitle"] = "Manage the Codex account used by this extension.",
        ["SignedInAsLabel"] = "Signed in as",
        ["NotSignedInLabel"] = "Not signed in",
        ["LogOutAndLogInButton"] = "Log out and log in again",
        ["CodexSettingsNav"] = "Codex settings",
        ["IdeSettingsNav"] = "IDE settings",
        ["McpSettingsNav"] = "MCP settings",
        ["SkillsSettingsNav"] = "Skills settings",
        ["LanguageNav"] = "Language",
        ["HistoryNav"] = "History",
        ["LanguageTitle"] = "Language",
        ["LanguageSubtitle"] = "Override automatic language detection for the extension UI.",
        ["LanguageAutoOption"] = "Automatic",
        ["CurrentLanguageLabel"] = "Current language",
        ["HistorySearchPlaceholder"] = "Search conversation history",
        ["AllTasksLabel"] = "All history",
        ["ViewAllTasksLabel"] = "View all history",
        ["TasksTitle"] = "History",
        ["RecentTasksTitle"] = "Recent history",
        ["NoTasksDetected"] = "No conversation history yet.",
        ["PersonalAccountLabel"] = "Personal account",
        ["OpenAgentSettingsLabel"] = "Open Agent settings",
        ["ReadDocsLabel"] = "Read docs",
        ["OpenConfigTomlLabel"] = "Open config.toml",
        ["SearchLanguagesPlaceholder"] = "Search language",
        ["NoLanguagesFound"] = "No languages found.",
        ["KeyboardShortcutsLabel"] = "Keyboard shortcuts",
        ["LogOutLabel"] = "Log out",
        ["NoTopicsAvailable"] = "No previous topics yet.",
        ["PlanModeLabel"] = "Plan mode",
        ["QuestionModeLabel"] = "Question mode",
        ["AgentModeLabel"] = "Agent mode",
        ["ReasoningEffortLabel"] = "Reasoning effort",
        ["AppsTitle"] = "Apps",
        ["McpServersTitle"] = "MCP servers",
        ["DetectedSkillsTitle"] = "Detected skills",
        ["SettingsTitle"] = "Configuration and details",
        ["ExecutableLabel"] = "Executable",
        ["WorkingDirectoryLabel"] = "Working directory",
        ["CodexConfigLabel"] = "Codex config",
        ["CodexSkillsLabel"] = "Skills folder",
        ["ManagedMcpTitle"] = "Managed MCP servers",
        ["ManagedMcpDescription"] = "Configure MCP entries for this extension without editing TOML manually.",
        ["ManagedMcpNameLabel"] = "Server name",
        ["ManagedMcpTransportLabel"] = "Transport",
        ["ManagedMcpCommandLabel"] = "Command",
        ["ManagedMcpArgsLabel"] = "Arguments, one per line",
        ["ManagedMcpUrlLabel"] = "URL",
        ["ManagedMcpStdioOption"] = "Command (stdio)",
        ["ManagedMcpUrlOption"] = "URL",
        ["ManagedMcpAddStdioButton"] = "Add stdio",
        ["ManagedMcpAddUrlButton"] = "Add URL",
        ["ManagedMcpRemoveButton"] = "Remove",
        ["ManagedMcpApplyButton"] = "Apply and refresh",
        ["ManagedMcpHint"] = "Only enabled and valid entries are applied. Valid names use letters, numbers, '-' or '_'.",
        ["SkillsTitle"] = "Skills",
        ["SkillsDescription"] = "Create and open global skills from the same place you manage the Codex integration.",
        ["SkillsLearnMore"] = "Give Codex superpowers.",
        ["InstalledSkillsTitle"] = "Installed",
        ["RecommendedSkillsTitle"] = "Recommended",
        ["SearchSkillsPlaceholder"] = "Search skills",
        ["InstallSkillButton"] = "Install",
        ["NoRecommendedSkills"] = "No recommended skills available.",
        ["IncludeIdeContextLabel"] = "Include IDE context",
        ["AddPhotosFilesMenu"] = "Add photos & files",
        ["McpShortcutsTitle"] = "MCP shortcuts",
        ["PermissionsTitle"] = "Permissions",
        ["DefaultPermissionsLabel"] = "Default permissions",
        ["ContextWindowTitle"] = "Context window",
        ["ContinueInTitle"] = "Continue in",
        ["LocalProjectLabel"] = "Local project",
        ["RateLimitsTitle"] = "Rate limits remaining",
        ["RateLimitsUnavailable"] = "Rate limits unavailable.",
        ["PlanLabelShort"] = "Plan",
        ["PrimaryWindowLabel"] = "Primary window",
        ["SecondaryWindowLabel"] = "Secondary window",
        ["CreditsLabel"] = "Credits",
        ["RateLimitRemainingSuffix"] = "left",
        ["RateLimitResetsPrefix"] = "resets",
        ["RateLimitUnlimitedLabel"] = "Unlimited",
        ["RateLimitWeeklyLabel"] = "Weekly",
        ["PreferredMcpTitle"] = "Preferred MCPs",
        ["SkillNameLabel"] = "Skill name",
        ["SkillDescriptionLabel"] = "Initial description",
        ["CreateSkillButton"] = "Create skill",
        ["OpenSkillsFolderButton"] = "Open skills folder",
        ["OpenConfigButton"] = "Open config",
        ["SkillOpenButton"] = "Open",
        ["RefreshStatusButton"] = "Refresh",
        ["EnabledLabel"] = "Enabled",
        ["OpenPanelButton"] = "Open",
        ["NoSkillsDetected"] = "No skills detected yet.",
        ["NoManagedMcpServers"] = "No managed MCP server configured.",
        ["VerbosityLabel"] = "Verbosity",
        ["ApprovalPolicyLabel"] = "Approval policy",
        ["RawOutputLabel"] = "Raw output",
        ["InsertButton"] = "Insert",
        ["ComposerPlaceholder"] = "Ask Codex anything",
        ["AddAttachmentTooltip"] = "Attach image or file",
        ["PasteImageTooltip"] = "Paste image from clipboard",
        ["SendTooltip"] = "Send prompt",
        ["HistoryTooltip"] = "Open history",
        ["SettingsTooltip"] = "Open settings",
        ["StopTooltip"] = "Stop response",
        ["StoppingTooltip"] = "Stopping response",
        ["SetupCheckingTitle"] = "Checking Codex environment",
        ["SetupCheckingSummary"] = "Validating the Codex executable, authentication, and local configuration.",
        ["SetupMissingExecutableTitle"] = "Codex runtime not found",
        ["SetupMissingExecutableSummary"] = "No compatible Codex executable could be resolved from the configured path or the local environment.",
        ["SetupMissingAuthTitle"] = "Codex is available, but OpenAI authentication is missing",
        ["SetupMissingAuthSummary"] = "Sign in to Codex or provide an OPENAI_API_KEY before starting a session.",
        ["SetupMissingProviderAuthTitle"] = "Codex provider credentials are missing",
        ["SetupMissingProviderAuthSummary"] = "The active provider is configured in config.toml, but its required credentials are not available.",
        ["SetupReadyTitle"] = "Codex is ready",
        ["SetupReadySummary"] = "The extension can start sessions with the resolved Codex executable.",
        ["SetupErrorTitle"] = "Codex setup needs attention",
        ["SetupErrorSummary"] = "The extension found Codex, but could not validate the environment.",
        ["SetupInstallButton"] = "Copy install command",
        ["SetupLoginButton"] = "Open login",
        ["SetupRefreshButton"] = "Refresh status",
        ["SetupSettingsButton"] = "Open settings",
        ["SetupInstallHint"] = "Install command",
        ["SetupExecutableHint"] = "Executable",
        ["SetupAuthHint"] = "Authentication",
        ["SetupVersionHint"] = "Version",
        ["SetupAuthFileLabel"] = "Using ~/.codex/auth.json",
        ["SetupApiKeyLabel"] = "Using OPENAI_API_KEY",
        ["SetupManagedLoginLabel"] = "Using Codex login",
        ["SetupConfigProviderLabel"] = "Using config.toml provider",
        ["SetupConfigProfileLabelFormat"] = "Using profile {0} from config.toml",
        ["SetupMissingAuthDetail"] = "Run `codex login` or configure an OpenAI API key.",
        ["SetupMissingProviderAuthDetail"] = "Update config.toml or set the provider environment variable before starting a session.",
        ["SetupInstallDetail"] = "Recommended install: `npm install -g @openai/codex`",
        ["LocalButton"] = "Local",
        ["RemoveAttachmentHoverLabel"] = "Remove",
        ["ApprovalCommandTitle"] = "Command approval required",
        ["ApprovalFileChangeTitle"] = "File change approval required",
        ["UserInputTitle"] = "Additional information required",
        ["ApprovalReasonLabel"] = "Reason",
        ["ApprovalCommandLabel"] = "Command",
        ["ApprovalWorkingDirectoryLabel"] = "Working directory",
        ["ApprovalGrantRootLabel"] = "Write access root",
        ["ApprovalAccept"] = "Allow",
        ["ApprovalAcceptForSession"] = "Allow for session",
        ["ApprovalAcceptWithExecpolicyAmendment"] = "Always allow similar command",
        ["ApprovalApplyNetworkPolicyAmendment"] = "Apply network rule",
        ["ApprovalDecline"] = "Deny",
        ["ApprovalCancel"] = "Stop turn",
        ["AllFilesFilter"] = "All files|*.*",
        ["CodexNoResponse"] = "Could not get a response from Codex.",
        ["ExecutionCanceled"] = "Execution canceled.",
        ["ExecutionError"] = "Error while running Codex.",
        ["ProcessingStatus"] = "Thinking...",
        ["ImagePasteErrorPrefix"] = "[image] error while pasting: ",
        ["LoadTopicsErrorPrefix"] = "[threads] error while loading: ",
        ["LoadModelsErrorPrefix"] = "[models] error while loading: ",
        ["ApprovalDefault"] = "Default",
        ["ApprovalRequest"] = "Request",
        ["ApprovalFailure"] = "Failure",
        ["ApprovalNever"] = "Never",
        ["ApprovalUntrusted"] = "Untrusted",
        ["SandboxReadOnly"] = "Read only",
        ["SandboxWorkspace"] = "Workspace",
        ["SandboxFullAccess"] = "Full access",
        ["ReasoningLow"] = "Low",
        ["ReasoningMedium"] = "Medium",
        ["ReasoningHigh"] = "High",
        ["ReasoningMax"] = "Maximum",
        ["ReasoningMinimal"] = "Minimal",
        ["SpeedLabel"] = "Speed",
        ["SpeedDefault"] = "Standard",
        ["SpeedFast"] = "Fast",
        ["SpeedFlex"] = "Flex",
        ["DeleteHistoryTooltip"] = "Delete from history",
        ["CopyButton"] = "Copy",
        ["SelectAllButton"] = "Select all",
        ["MarkdownTextModeLabel"] = "Text",
        ["MarkdownRenderedModeLabel"] = "Rendered",
        ["RunningStatus"] = "Running",
        ["ReadyStatus"] = "Ready",
        ["ContextWindowDetailFormat"] = "{0} used ({1} left)",
        ["IdeContextPrefix"] = "Current IDE context:",
        ["MermaidDiagramLabel"] = "Diagram",
        ["MermaidCodeLabel"] = "Code",
        ["MermaidLoadingPreview"] = "Loading Mermaid preview...",
        ["MermaidInitFailed"] = "Could not start Mermaid preview on this machine.",
        ["MermaidLoadFailedFormat"] = "Could not load Mermaid preview: {0}.",
        ["MermaidRenderFailed"] = "Could not render Mermaid preview.",
        ["MermaidRenderFailedFormat"] = "Could not render Mermaid preview: {0}.",
        ["MermaidFreezeFailed"] = "Could not freeze Mermaid preview.",
        ["MermaidLoadTimeout"] = "Mermaid preview could not finish loading.",
        ["MermaidPreviewFallback"] = "Could not build a preview for this Mermaid. Use the toggle to switch between diagram and code.",
        ["MermaidPreviewScriptError"] = "Could not load Mermaid preview.",
        ["ToolWindowErrorMessage"] = "The Codex window encountered an error during initialization.",
        ["SettingsToolWindowErrorMessage"] = "The Codex settings window encountered an error during initialization.",
        ["OpenWindowFailedMessage"] = "The Codex window failed to open.",
        ["ExecutionCanceledTag"] = "canceled",
        ["ExecutionErrorTag"] = "error",
        ["ExtensionContextPrefix"] = "Extension context: the current working directory of the open project is \"",
        ["PreferredMcpPrefix"] = "Preferred MCP shortcuts for this request:",
        ["IdeContextSolutionLabel"] = "Solution:",
        ["IdeContextActiveDocumentLabel"] = "Active document:",
        ["IdeContextSelectedItemsLabel"] = "Selected items:",
        ["IdeContextOpenFilesLabel"] = "Open files:",
        ["IdeContextSelectionLabel"] = "Active selection:",
        ["InvalidSkillNameMessage"] = "Invalid skill name.",
        ["SkillTemplateSummary"] = "Describe here when to use this skill and what problem it solves.",
        ["SkillTemplateWhenToUseHeading"] = "## When to use",
        ["SkillTemplateWhenToUseBullet"] = "- Explain which requests should trigger this skill.",
        ["SkillTemplateFlowHeading"] = "## Flow",
        ["SkillTemplateFlowStep1"] = "1. Describe the first step.",
        ["SkillTemplateFlowStep2"] = "2. List validations or caveats.",
        ["SkillTemplateFlowStep3"] = "3. Finish with the expected result.",
        ["CodexDetectedLabel"] = "Codex detected",
        ["EventPlanTitle"] = "Plan",
        ["EventPlanUpdated"] = "Plan updated",
        ["EventReasoningTitle"] = "Reasoning",
        ["EventReasoningUpdated"] = "Reasoning updated",
        ["EventCommandTitle"] = "Command",
        ["EventWorkingDirectoryLabel"] = "Working directory",
        ["EventOutputLabel"] = "Output",
        ["EventFileChangesTitle"] = "File changes",
        ["EventUpdatedFiles"] = "updated files",
        ["EventFileUpdated"] = "file updated",
        ["EventMcpToolTitle"] = "MCP tool",
        ["EventArgumentsLabel"] = "Arguments",
        ["EventErrorLabel"] = "Error",
        ["EventResultLabel"] = "Result",
        ["EventToolTitle"] = "Tool",
        ["EventAgentToolTitle"] = "Agent tool",
        ["EventAgentToolUsed"] = "Agent tool used",
        ["EventPromptLabel"] = "Prompt",
        ["EventWebSearchTitle"] = "Web search",
        ["EventImageViewTitle"] = "Image view",
        ["EventImageViewed"] = "Image viewed",
        ["EventImageGenerationTitle"] = "Image generation",
        ["EventImageGenerated"] = "Image generated",
        ["EventReviewModeTitle"] = "Review mode",
        ["EventEnteredReviewMode"] = "Entered review mode",
        ["EventExitedReviewMode"] = "Exited review mode",
        ["EventContextTitle"] = "Context",
        ["EventConversationContextCompacted"] = "Conversation context compacted",
        ["EventToolCall"] = "Tool call",
        ["EventCommandExecuted"] = "Command executed",
        ["EventMoreFormat"] = "+{0} more",
        ["EventMoreFilesFormat"] = "+{0} more files",
        ["EventPendingStatus"] = "pending",
        ["EventInProgressStatus"] = "in progress",
        ["EventCompletedStatus"] = "completed",
        ["EventFailedStatus"] = "failed",
        ["ToolWindowXamlLoadLogMessage"] = "Failed to load the Codex tool window XAML.",
        ["ToolWindowViewModelCreateLogMessage"] = "Failed to create the Codex tool window view model.",
        ["SettingsToolWindowXamlLoadLogMessage"] = "Failed to load the Codex settings window XAML.",
        ["SettingsToolWindowViewModelCreateLogMessage"] = "Failed to create the Codex settings window view model.",
        ["ToolWindowInitializeLogMessage"] = "Failed to initialize the Codex tool window content.",
        ["SettingsToolWindowInitializeLogMessage"] = "Failed to initialize the Codex settings window.",
        ["SettingsToolWindowOpenLogMessage"] = "Failed to open the Codex settings window.",
        ["ToolWindowOpenLogMessage"] = "Failed to open the Codex window.",
        ["AsyncPanelInitializeLogMessage"] = "Failed during asynchronous initialization of the Codex panel.",
        ["StartTurnFailedMessage"] = "Failed to start turn.",
        ["AppServerValidationFailed"] = "The installed Codex CLI could not be validated for app-server support.",
        ["AppServerUnsupported"] = "The installed Codex CLI does not appear to support `app-server`. Update Codex CLI and try again.",
        ["AppServerClosedUnexpectedly"] = "Codex app server was closed unexpectedly.",
        ["AppServerRequestFailed"] = "App server request failed.",
        ["AppServerUnavailable"] = "Codex app server is not available.",
        ["EventCommentaryTitle"] = "Commentary",
        ["EventMcpProgressTitle"] = "MCP progress",
        ["OutputTagSetup"] = "setup",
        ["OutputTagAuth"] = "auth",
        ["OutputTagSkills"] = "skills",
        ["OutputTagRemoteSkills"] = "skills-remote",
        ["OutputTagInit"] = "init",
        ["OutputTagServer"] = "server",
        ["OutputTagStderr"] = "stderr",
        ["OutputTagAppServer"] = "app-server",
        ["OutputTagApproval"] = "approval",
        ["OutputTagUserInput"] = "user-input",
        ["ExitCodeLabel"] = "exit code",
        ["ManagedMcpDefaultName"] = "new-mcp",
        ["ManagedMcpDefaultUrlName"] = "new-mcp-url",
        ["MermaidBundleNotFoundFormat"] = "Resource '{0}' was not found."
    };

    private static readonly IReadOnlyDictionary<string, string> PortugueseStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Tópicos",
        ["HistoryTitle"] = "Histórico",
        ["HistoryTopicsTitle"] = "Conversas anteriores",
        ["UseButton"] = "Usar",
        ["NewTopicButton"] = "Novo tópico",
        ["RenameTopicButton"] = "Renomear",
        ["RenameTopicPlaceholder"] = "Renomear tópico selecionado",
        ["CloseButton"] = "Fechar",
        ["TasksTitle"] = "Historico",
        ["RecentTasksTitle"] = "Historico recente",
        ["HistorySearchPlaceholder"] = "Procurar no historico",
        ["AllTasksLabel"] = "Todo o historico",
        ["ViewAllTasksLabel"] = "Ver historico completo",
        ["NoTasksDetected"] = "Nenhum historico ainda.",
        ["PersonalAccountLabel"] = "Conta pessoal",
        ["OpenAgentSettingsLabel"] = "Abrir configurações do agente",
        ["ReadDocsLabel"] = "Ler documentação",
        ["OpenConfigTomlLabel"] = "Abrir config.toml",
        ["SearchLanguagesPlaceholder"] = "Procurar idioma",
        ["NoLanguagesFound"] = "Nenhum idioma encontrado.",
        ["KeyboardShortcutsLabel"] = "Atalhos do teclado",
        ["LogOutLabel"] = "Sair",
        ["PlanModeLabel"] = "Modo planejamento",
        ["QuestionModeLabel"] = "Modo pergunta",
        ["AgentModeLabel"] = "Modo agente",
        ["ReasoningEffortLabel"] = "Esforço de raciocínio",
        ["AppsTitle"] = "Apps",
        ["McpServersTitle"] = "Servidores MCP",
        ["SettingsTitle"] = "Configuração e detalhes",
        ["ExecutableLabel"] = "Executável",
        ["WorkingDirectoryLabel"] = "Diretório de trabalho",
        ["VerbosityLabel"] = "Verbosidade",
        ["ApprovalPolicyLabel"] = "Política de aprovação",
        ["RawOutputLabel"] = "Saída bruta",
        ["InsertButton"] = "Inserir",
        ["ComposerPlaceholder"] = "Pergunte qualquer coisa ao Codex",
        ["AddAttachmentTooltip"] = "Anexar imagem ou arquivo",
        ["PasteImageTooltip"] = "Colar imagem da área de transferência",
        ["SendTooltip"] = "Enviar prompt",
        ["HistoryTooltip"] = "Abrir historico",
        ["SettingsTooltip"] = "Abrir configurações",
        ["StopTooltip"] = "Parar resposta",
        ["StoppingTooltip"] = "Parando resposta",
        ["LocalButton"] = "Local",
        ["RemoveAttachmentHoverLabel"] = "Remover",
        ["ApprovalCommandTitle"] = "Aprovação necessária para executar comando",
        ["ApprovalFileChangeTitle"] = "Aprovação necessária para alterar arquivos",
        ["UserInputTitle"] = "Informações adicionais necessárias",
        ["ApprovalReasonLabel"] = "Motivo",
        ["ApprovalCommandLabel"] = "Comando",
        ["ApprovalWorkingDirectoryLabel"] = "Diretório de trabalho",
        ["ApprovalGrantRootLabel"] = "Raiz com permissão de escrita",
        ["ApprovalAccept"] = "Permitir",
        ["ApprovalAcceptForSession"] = "Permitir na sessão",
        ["ApprovalAcceptWithExecpolicyAmendment"] = "Permitir comandos semelhantes",
        ["ApprovalApplyNetworkPolicyAmendment"] = "Aplicar regra de rede",
        ["ApprovalDecline"] = "Negar",
        ["ApprovalCancel"] = "Interromper turno",
        ["AllFilesFilter"] = "Todos os arquivos|*.*",
        ["CodexNoResponse"] = "Não foi possível obter resposta do Codex.",
        ["ExecutionCanceled"] = "Execução cancelada.",
        ["ExecutionError"] = "Erro ao executar o Codex.",
        ["ProcessingStatus"] = "Pensando...",
        ["ImagePasteErrorPrefix"] = "[imagem] erro ao colar: ",
        ["LoadTopicsErrorPrefix"] = "[tópicos] erro ao carregar: ",
        ["LoadModelsErrorPrefix"] = "[modelos] erro ao carregar: ",
        ["ApprovalDefault"] = "Padrão",
        ["ApprovalRequest"] = "Solicitar",
        ["ApprovalFailure"] = "Falha",
        ["ApprovalNever"] = "Nunca",
        ["ApprovalUntrusted"] = "Não confiável",
        ["SandboxReadOnly"] = "Somente leitura",
        ["SandboxWorkspace"] = "Workspace",
        ["SandboxFullAccess"] = "Acesso completo",
        ["ReasoningLow"] = "Baixa",
        ["ReasoningMedium"] = "Média",
        ["ReasoningHigh"] = "Alta",
        ["ReasoningMax"] = "Máxima",
        ["ReasoningMinimal"] = "Mínima",
        ["SpeedLabel"] = "Velocidade",
        ["SpeedDefault"] = "Padrão",
        ["SpeedFast"] = "Rápida",
        ["SpeedFlex"] = "Flex",
        ["AccountTitle"] = "Conta",
        ["AccountSubtitle"] = "Gerencie a conta do Codex usada por esta extensão.",
        ["SignedInAsLabel"] = "Conectado como",
        ["NotSignedInLabel"] = "Sem login",
        ["LogOutAndLogInButton"] = "Sair e fazer login novamente",
        ["CodexSettingsNav"] = "Configurações do Codex",
        ["IdeSettingsNav"] = "Configurações da IDE",
        ["McpSettingsNav"] = "Configurações de MCP",
        ["SkillsSettingsNav"] = "Configurações de skills",
        ["LanguageNav"] = "Idioma",
        ["HistoryNav"] = "Histórico",
        ["LanguageTitle"] = "Idioma",
        ["LanguageSubtitle"] = "Substitua a detecção automática de idioma da interface.",
        ["LanguageAutoOption"] = "Automático",
        ["CurrentLanguageLabel"] = "Idioma atual",
        ["NoTopicsAvailable"] = "Nenhuma conversa anterior ainda.",
        ["DetectedSkillsTitle"] = "Skills detectadas",
        ["CodexConfigLabel"] = "Configuração do Codex",
        ["CodexSkillsLabel"] = "Pasta de skills",
        ["ManagedMcpTitle"] = "MCPs gerenciados",
        ["ManagedMcpDescription"] = "Configure entradas de MCP pela interface da extensão, sem editar TOML manualmente.",
        ["ManagedMcpNameLabel"] = "Nome do servidor",
        ["ManagedMcpTransportLabel"] = "Transporte",
        ["ManagedMcpCommandLabel"] = "Comando",
        ["ManagedMcpArgsLabel"] = "Argumentos, um por linha",
        ["ManagedMcpUrlLabel"] = "URL",
        ["ManagedMcpStdioOption"] = "Comando (stdio)",
        ["ManagedMcpUrlOption"] = "URL",
        ["ManagedMcpAddStdioButton"] = "Adicionar stdio",
        ["ManagedMcpAddUrlButton"] = "Adicionar URL",
        ["ManagedMcpRemoveButton"] = "Remover",
        ["ManagedMcpApplyButton"] = "Aplicar e atualizar",
        ["ManagedMcpHint"] = "Somente entradas válidas e habilitadas são aplicadas. Use apenas letras, números, '-' ou '_' no nome.",
        ["SkillsTitle"] = "Skills",
        ["SkillsDescription"] = "Crie e abra skills globais no mesmo lugar em que você configura a integração do Codex.",
        ["SkillsLearnMore"] = "Dê superpoderes ao Codex.",
        ["InstalledSkillsTitle"] = "Instaladas",
        ["RecommendedSkillsTitle"] = "Recomendadas",
        ["SearchSkillsPlaceholder"] = "Buscar skills",
        ["InstallSkillButton"] = "Instalar",
        ["NoRecommendedSkills"] = "Nenhuma skill recomendada disponível.",
        ["IncludeIdeContextLabel"] = "Incluir contexto da IDE",
        ["AddPhotosFilesMenu"] = "Adicionar fotos e arquivos",
        ["McpShortcutsTitle"] = "Atalhos MCP",
        ["PermissionsTitle"] = "Permissões",
        ["DefaultPermissionsLabel"] = "Permissões padrão",
        ["ContextWindowTitle"] = "Janela de contexto",
        ["ContinueInTitle"] = "Continuar em",
        ["LocalProjectLabel"] = "Projeto local",
        ["RateLimitsTitle"] = "Rate limits restantes",
        ["RateLimitsUnavailable"] = "Rate limits indisponíveis.",
        ["PlanLabelShort"] = "Plano",
        ["PrimaryWindowLabel"] = "Janela principal",
        ["SecondaryWindowLabel"] = "Janela secundária",
        ["CreditsLabel"] = "Créditos",
        ["RateLimitRemainingSuffix"] = "restantes",
        ["RateLimitResetsPrefix"] = "reinicia",
        ["RateLimitUnlimitedLabel"] = "Ilimitado",
        ["RateLimitWeeklyLabel"] = "Semanal",
        ["PreferredMcpTitle"] = "MCPs preferidos",
        ["SkillNameLabel"] = "Nome da skill",
        ["SkillDescriptionLabel"] = "Descrição inicial",
        ["CreateSkillButton"] = "Criar skill",
        ["OpenSkillsFolderButton"] = "Abrir pasta de skills",
        ["OpenConfigButton"] = "Abrir config",
        ["SkillOpenButton"] = "Abrir",
        ["RefreshStatusButton"] = "Atualizar",
        ["EnabledLabel"] = "Habilitado",
        ["OpenPanelButton"] = "Abrir",
        ["NoSkillsDetected"] = "Nenhuma skill detectada ainda.",
        ["NoManagedMcpServers"] = "Nenhum servidor MCP gerenciado foi configurado.",
        ["SetupCheckingTitle"] = "Verificando ambiente do Codex",
        ["SetupCheckingSummary"] = "Validando executável do Codex, autenticação e configuração local.",
        ["SetupMissingExecutableTitle"] = "Runtime do Codex não encontrado",
        ["SetupMissingExecutableSummary"] = "Nenhum executável compatível do Codex pôde ser resolvido a partir do caminho configurado ou do ambiente local.",
        ["SetupMissingAuthTitle"] = "Codex disponível, mas sem autenticação OpenAI",
        ["SetupMissingAuthSummary"] = "Faça login no Codex ou forneça um OPENAI_API_KEY antes de iniciar uma sessão.",
        ["SetupMissingProviderAuthTitle"] = "Faltam credenciais do provider do Codex",
        ["SetupMissingProviderAuthSummary"] = "O provider ativo está configurado no config.toml, mas as credenciais exigidas não estão disponíveis.",
        ["SetupReadyTitle"] = "Codex pronto para uso",
        ["SetupReadySummary"] = "A extensão já consegue iniciar sessões usando o executável resolvido do Codex.",
        ["SetupErrorTitle"] = "A configuração do Codex precisa de atenção",
        ["SetupErrorSummary"] = "A extensão encontrou o Codex, mas não conseguiu validar o ambiente.",
        ["SetupInstallButton"] = "Copiar comando de instalação",
        ["SetupLoginButton"] = "Abrir login",
        ["SetupRefreshButton"] = "Atualizar status",
        ["SetupSettingsButton"] = "Abrir configurações",
        ["SetupInstallHint"] = "Comando de instalação",
        ["SetupExecutableHint"] = "Executável",
        ["SetupAuthHint"] = "Autenticação",
        ["SetupVersionHint"] = "Versão",
        ["SetupAuthFileLabel"] = "Usando ~/.codex/auth.json",
        ["SetupApiKeyLabel"] = "Usando OPENAI_API_KEY",
        ["SetupManagedLoginLabel"] = "Usando login do Codex",
        ["SetupConfigProviderLabel"] = "Usando provider do config.toml",
        ["SetupConfigProfileLabelFormat"] = "Usando perfil {0} do config.toml",
        ["SetupMissingAuthDetail"] = "Execute `codex login` ou configure uma API key da OpenAI.",
        ["SetupMissingProviderAuthDetail"] = "Atualize o config.toml ou defina a variável de ambiente do provider antes de iniciar uma sessão.",
        ["SetupInstallDetail"] = "Instalação recomendada: `npm install -g @openai/codex`",
        ["DeleteHistoryTooltip"] = "Excluir do histórico",
        ["CopyButton"] = "Copiar",
        ["SelectAllButton"] = "Selecionar tudo",
        ["MarkdownTextModeLabel"] = "Texto",
        ["MarkdownRenderedModeLabel"] = "Renderizado",
        ["RunningStatus"] = "Executando",
        ["ReadyStatus"] = "Pronto",
        ["ContextWindowDetailFormat"] = "{0} usados ({1} restantes)",
        ["IdeContextPrefix"] = "Contexto atual da IDE:",
        ["MermaidDiagramLabel"] = "Diagrama",
        ["MermaidCodeLabel"] = "Código",
        ["MermaidLoadingPreview"] = "Carregando preview do Mermaid...",
        ["MermaidInitFailed"] = "Não foi possível iniciar o preview do Mermaid nesta máquina.",
        ["MermaidLoadFailedFormat"] = "Não foi possível carregar o preview do Mermaid: {0}.",
        ["MermaidRenderFailed"] = "Não foi possível renderizar o preview do Mermaid.",
        ["MermaidRenderFailedFormat"] = "Não foi possível renderizar o preview do Mermaid: {0}.",
        ["MermaidFreezeFailed"] = "Não foi possível congelar o preview do Mermaid.",
        ["MermaidLoadTimeout"] = "Não foi possível concluir o carregamento do preview do Mermaid.",
        ["MermaidPreviewFallback"] = "Não foi possível montar a prévia deste Mermaid. Use o toggle para alternar entre diagrama e código.",
        ["MermaidPreviewScriptError"] = "Erro ao carregar o preview do Mermaid.",
        ["ToolWindowErrorMessage"] = "A janela do Codex encontrou um erro durante a inicialização.",
        ["SettingsToolWindowErrorMessage"] = "A janela de configurações do Codex encontrou um erro durante a inicialização.",
        ["OpenWindowFailedMessage"] = "A janela do Codex falhou ao abrir.",
        ["ExecutionCanceledTag"] = "cancelado",
        ["ExecutionErrorTag"] = "erro",
        ["ExtensionContextPrefix"] = "Contexto da extensão: o diretório de trabalho atual do projeto aberto é \"",
        ["PreferredMcpPrefix"] = "Atalhos MCP preferidos para este pedido:",
        ["IdeContextSolutionLabel"] = "Solução:",
        ["IdeContextActiveDocumentLabel"] = "Documento ativo:",
        ["IdeContextSelectedItemsLabel"] = "Itens selecionados:",
        ["IdeContextOpenFilesLabel"] = "Arquivos abertos:",
        ["IdeContextSelectionLabel"] = "Seleção ativa:",
        ["InvalidSkillNameMessage"] = "Nome de skill inválido.",
        ["SkillTemplateSummary"] = "Descreva aqui quando usar esta skill e qual problema ela resolve.",
        ["SkillTemplateWhenToUseHeading"] = "## Quando usar",
        ["SkillTemplateWhenToUseBullet"] = "- Explique em quais pedidos essa skill deve ser acionada.",
        ["SkillTemplateFlowHeading"] = "## Fluxo",
        ["SkillTemplateFlowStep1"] = "1. Descreva o passo inicial.",
        ["SkillTemplateFlowStep2"] = "2. Liste as validações ou cuidados.",
        ["SkillTemplateFlowStep3"] = "3. Finalize com o resultado esperado.",
        ["CodexDetectedLabel"] = "Codex detectado",
        ["EventPlanTitle"] = "Plano",
        ["EventPlanUpdated"] = "Plano atualizado",
        ["EventReasoningTitle"] = "Raciocínio",
        ["EventReasoningUpdated"] = "Raciocínio atualizado",
        ["EventCommandTitle"] = "Comando",
        ["EventWorkingDirectoryLabel"] = "Diretório de trabalho",
        ["EventOutputLabel"] = "Saída",
        ["EventFileChangesTitle"] = "Alterações em arquivos",
        ["EventUpdatedFiles"] = "arquivos atualizados",
        ["EventFileUpdated"] = "arquivo atualizado",
        ["EventMcpToolTitle"] = "Ferramenta MCP",
        ["EventArgumentsLabel"] = "Argumentos",
        ["EventErrorLabel"] = "Erro",
        ["EventResultLabel"] = "Resultado",
        ["EventToolTitle"] = "Ferramenta",
        ["EventAgentToolTitle"] = "Ferramenta de agente",
        ["EventAgentToolUsed"] = "Ferramenta de agente usada",
        ["EventPromptLabel"] = "Prompt",
        ["EventWebSearchTitle"] = "Busca na web",
        ["EventImageViewTitle"] = "Visualização de imagem",
        ["EventImageViewed"] = "Imagem visualizada",
        ["EventImageGenerationTitle"] = "Geração de imagem",
        ["EventImageGenerated"] = "Imagem gerada",
        ["EventReviewModeTitle"] = "Modo revisão",
        ["EventEnteredReviewMode"] = "Entrou no modo revisão",
        ["EventExitedReviewMode"] = "Saiu do modo revisão",
        ["EventContextTitle"] = "Contexto",
        ["EventConversationContextCompacted"] = "Contexto da conversa compactado",
        ["EventToolCall"] = "Chamada de ferramenta",
        ["EventCommandExecuted"] = "Comando executado",
        ["EventMoreFormat"] = "+{0} mais",
        ["EventMoreFilesFormat"] = "+{0} arquivos a mais",
        ["EventPendingStatus"] = "pendente",
        ["EventInProgressStatus"] = "em andamento",
        ["EventCompletedStatus"] = "concluído",
        ["EventFailedStatus"] = "falhou",
        ["ToolWindowXamlLoadLogMessage"] = "Falha ao carregar o XAML da janela do Codex.",
        ["ToolWindowViewModelCreateLogMessage"] = "Falha ao criar o view model da janela do Codex.",
        ["SettingsToolWindowXamlLoadLogMessage"] = "Falha ao carregar o XAML da janela de configurações do Codex.",
        ["SettingsToolWindowViewModelCreateLogMessage"] = "Falha ao criar o view model da janela de configurações do Codex.",
        ["ToolWindowInitializeLogMessage"] = "Falha ao inicializar o conteúdo da janela do Codex.",
        ["SettingsToolWindowInitializeLogMessage"] = "Falha ao inicializar a janela de configurações do Codex.",
        ["SettingsToolWindowOpenLogMessage"] = "Falha ao abrir a janela de configurações do Codex.",
        ["ToolWindowOpenLogMessage"] = "Falha ao abrir a janela do Codex.",
        ["AsyncPanelInitializeLogMessage"] = "Falha durante a inicialização assíncrona do painel do Codex.",
        ["StartTurnFailedMessage"] = "Falha ao iniciar o turno.",
        ["AppServerValidationFailed"] = "Não foi possível validar se o Codex CLI instalado oferece suporte a app-server.",
        ["AppServerUnsupported"] = "O Codex CLI instalado não parece oferecer suporte a `app-server`. Atualize o Codex CLI e tente novamente.",
        ["AppServerClosedUnexpectedly"] = "O app server do Codex foi encerrado inesperadamente.",
        ["AppServerRequestFailed"] = "A solicitação ao app server falhou.",
        ["AppServerUnavailable"] = "O app server do Codex não está disponível.",
        ["EventCommentaryTitle"] = "Comentário",
        ["EventMcpProgressTitle"] = "Progresso do MCP",
        ["OutputTagSetup"] = "config",
        ["OutputTagAuth"] = "autenticação",
        ["OutputTagSkills"] = "skills",
        ["OutputTagRemoteSkills"] = "skills-remotas",
        ["OutputTagInit"] = "inicialização",
        ["OutputTagServer"] = "servidor",
        ["OutputTagStderr"] = "stderr",
        ["OutputTagAppServer"] = "app-server",
        ["OutputTagApproval"] = "aprovação",
        ["OutputTagUserInput"] = "entrada-usuário",
        ["ExitCodeLabel"] = "código de saída",
        ["ManagedMcpDefaultName"] = "novo-mcp",
        ["ManagedMcpDefaultUrlName"] = "novo-mcp-url",
        ["MermaidBundleNotFoundFormat"] = "O recurso '{0}' não foi encontrado."
    };

    private static readonly IReadOnlyDictionary<string, string> SpanishStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Temas",
        ["HistoryTitle"] = "Historial",
        ["UseButton"] = "Usar",
        ["NewTopicButton"] = "Nuevo tema",
        ["RenameTopicButton"] = "Renombrar",
        ["RenameTopicPlaceholder"] = "Renombrar tema seleccionado",
        ["CloseButton"] = "Cerrar",
        ["PlanModeLabel"] = "Modo planificación",
        ["QuestionModeLabel"] = "Modo pregunta",
        ["AgentModeLabel"] = "Modo agente",
        ["AppsTitle"] = "Apps",
        ["McpServersTitle"] = "Servidores MCP",
        ["SettingsTitle"] = "Configuración y detalles",
        ["ExecutableLabel"] = "Ejecutable",
        ["WorkingDirectoryLabel"] = "Directorio de trabajo",
        ["VerbosityLabel"] = "Verbosidad",
        ["ApprovalPolicyLabel"] = "Política de aprobación",
        ["RawOutputLabel"] = "Salida sin procesar",
        ["InsertButton"] = "Insertar",
        ["ComposerPlaceholder"] = "Pregúntale cualquier cosa a Codex",
        ["AddAttachmentTooltip"] = "Adjuntar imagen o archivo",
        ["PasteImageTooltip"] = "Pegar imagen del portapapeles",
        ["SendTooltip"] = "Enviar prompt",
        ["HistoryTooltip"] = "Abrir historial",
        ["StopTooltip"] = "Detener respuesta",
        ["StoppingTooltip"] = "Deteniendo respuesta",
        ["SettingsTooltip"] = "Abrir configuración",
        ["LocalButton"] = "Local",
        ["RemoveAttachmentHoverLabel"] = "Quitar",
        ["ApprovalCommandTitle"] = "Se requiere aprobación para ejecutar el comando",
        ["ApprovalFileChangeTitle"] = "Se requiere aprobación para cambiar archivos",
        ["UserInputTitle"] = "Se requiere información adicional",
        ["ApprovalReasonLabel"] = "Motivo",
        ["ApprovalCommandLabel"] = "Comando",
        ["ApprovalWorkingDirectoryLabel"] = "Directorio de trabajo",
        ["ApprovalGrantRootLabel"] = "Raíz con permiso de escritura",
        ["ApprovalAccept"] = "Permitir",
        ["ApprovalAcceptForSession"] = "Permitir en la sesión",
        ["ApprovalAcceptWithExecpolicyAmendment"] = "Permitir comandos similares",
        ["ApprovalApplyNetworkPolicyAmendment"] = "Aplicar regla de red",
        ["ApprovalDecline"] = "Denegar",
        ["ApprovalCancel"] = "Detener turno",
        ["AllFilesFilter"] = "Todos los archivos|*.*",
        ["CodexNoResponse"] = "No fue posible obtener respuesta de Codex.",
        ["ExecutionCanceled"] = "Ejecución cancelada.",
        ["ExecutionError"] = "Error al ejecutar Codex.",
        ["ProcessingStatus"] = "Pensando...",
        ["ImagePasteErrorPrefix"] = "[imagen] error al pegar: ",
        ["LoadTopicsErrorPrefix"] = "[temas] error al cargar: ",
        ["LoadModelsErrorPrefix"] = "[modelos] error al cargar: ",
        ["ApprovalDefault"] = "Predeterminado",
        ["ApprovalRequest"] = "Solicitar",
        ["ApprovalFailure"] = "Fallo",
        ["ApprovalNever"] = "Nunca",
        ["ApprovalUntrusted"] = "No confiable",
        ["SandboxReadOnly"] = "Solo lectura",
        ["SandboxWorkspace"] = "Workspace",
        ["SandboxFullAccess"] = "Acceso completo",
        ["ReasoningLow"] = "Baja",
        ["ReasoningMedium"] = "Media",
        ["ReasoningHigh"] = "Alta",
        ["ReasoningMax"] = "Máxima",
        ["ReasoningMinimal"] = "Mínima",
        ["AccountTitle"] = "Cuenta",
        ["AccountSubtitle"] = "Administra la cuenta de Codex usada por esta extensión.",
        ["SignedInAsLabel"] = "Conectado como",
        ["NotSignedInLabel"] = "Sin iniciar sesión",
        ["LogOutAndLogInButton"] = "Cerrar sesión e iniciar sesión de nuevo",
        ["CodexSettingsNav"] = "Configuración de Codex",
        ["IdeSettingsNav"] = "Configuración del IDE",
        ["McpSettingsNav"] = "Configuración de MCP",
        ["SkillsSettingsNav"] = "Configuración de skills",
        ["LanguageNav"] = "Idioma",
        ["HistoryNav"] = "Historial",
        ["LanguageTitle"] = "Idioma",
        ["LanguageSubtitle"] = "Anula la detección automática del idioma para la interfaz de la extensión.",
        ["LanguageAutoOption"] = "Automático",
        ["CurrentLanguageLabel"] = "Idioma actual",
        ["HistoryTopicsTitle"] = "Conversaciones anteriores",
        ["HistorySearchPlaceholder"] = "Buscar en el historial",
        ["AllTasksLabel"] = "Todo el historial",
        ["ViewAllTasksLabel"] = "Ver todo el historial",
        ["TasksTitle"] = "Historial",
        ["RecentTasksTitle"] = "Historial reciente",
        ["NoTasksDetected"] = "Todavía no hay historial de conversaciones.",
        ["PersonalAccountLabel"] = "Cuenta personal",
        ["OpenAgentSettingsLabel"] = "Abrir configuración del agente",
        ["ReadDocsLabel"] = "Leer documentación",
        ["OpenConfigTomlLabel"] = "Abrir config.toml",
        ["SearchLanguagesPlaceholder"] = "Buscar idioma",
        ["NoLanguagesFound"] = "No se encontraron idiomas.",
        ["KeyboardShortcutsLabel"] = "Atajos de teclado",
        ["LogOutLabel"] = "Cerrar sesión",
        ["NoTopicsAvailable"] = "Todavía no hay temas anteriores.",
        ["ReasoningEffortLabel"] = "Esfuerzo de razonamiento",
        ["DetectedSkillsTitle"] = "Skills detectadas",
        ["CodexConfigLabel"] = "Configuración de Codex",
        ["CodexSkillsLabel"] = "Carpeta de skills",
        ["ManagedMcpTitle"] = "Servidores MCP gestionados",
        ["ManagedMcpDescription"] = "Configura entradas MCP desde la extensión sin editar el TOML manualmente.",
        ["ManagedMcpNameLabel"] = "Nombre del servidor",
        ["ManagedMcpTransportLabel"] = "Transporte",
        ["ManagedMcpCommandLabel"] = "Comando",
        ["ManagedMcpArgsLabel"] = "Argumentos, uno por línea",
        ["ManagedMcpUrlLabel"] = "URL",
        ["ManagedMcpStdioOption"] = "Comando (stdio)",
        ["ManagedMcpUrlOption"] = "URL",
        ["ManagedMcpAddStdioButton"] = "Añadir stdio",
        ["ManagedMcpAddUrlButton"] = "Añadir URL",
        ["ManagedMcpRemoveButton"] = "Quitar",
        ["ManagedMcpApplyButton"] = "Aplicar y actualizar",
        ["ManagedMcpHint"] = "Solo se aplican las entradas válidas y habilitadas. Los nombres válidos usan letras, números, '-' o '_'.",
        ["SkillsTitle"] = "Skills",
        ["SkillsDescription"] = "Crea y abre skills globales desde el mismo lugar donde administras la integración de Codex.",
        ["SkillsLearnMore"] = "Dale superpoderes a Codex.",
        ["InstalledSkillsTitle"] = "Instaladas",
        ["RecommendedSkillsTitle"] = "Recomendadas",
        ["SearchSkillsPlaceholder"] = "Buscar skills",
        ["InstallSkillButton"] = "Instalar",
        ["NoRecommendedSkills"] = "No hay skills recomendadas disponibles.",
        ["IncludeIdeContextLabel"] = "Incluir contexto del IDE",
        ["SpeedLabel"] = "Velocidad",
        ["SpeedDefault"] = "Estándar",
        ["SpeedFast"] = "Rápida",
        ["SpeedFlex"] = "Flex",
        ["AddPhotosFilesMenu"] = "Añadir fotos y archivos",
        ["McpShortcutsTitle"] = "Atajos MCP",
        ["PermissionsTitle"] = "Permisos",
        ["DefaultPermissionsLabel"] = "Permisos predeterminados",
        ["ContextWindowTitle"] = "Ventana de contexto",
        ["ContinueInTitle"] = "Continuar en",
        ["LocalProjectLabel"] = "Proyecto local",
        ["RateLimitsTitle"] = "Límites restantes",
        ["RateLimitsUnavailable"] = "Límites no disponibles.",
        ["PlanLabelShort"] = "Plan",
        ["PrimaryWindowLabel"] = "Ventana principal",
        ["SecondaryWindowLabel"] = "Ventana secundaria",
        ["CreditsLabel"] = "Créditos",
        ["RateLimitRemainingSuffix"] = "restantes",
        ["RateLimitResetsPrefix"] = "reinicia",
        ["RateLimitUnlimitedLabel"] = "Ilimitado",
        ["RateLimitWeeklyLabel"] = "Semanal",
        ["PreferredMcpTitle"] = "MCP preferidos",
        ["SkillNameLabel"] = "Nombre de la skill",
        ["SkillDescriptionLabel"] = "Descripción inicial",
        ["CreateSkillButton"] = "Crear skill",
        ["OpenSkillsFolderButton"] = "Abrir carpeta de skills",
        ["OpenConfigButton"] = "Abrir config",
        ["SkillOpenButton"] = "Abrir",
        ["RefreshStatusButton"] = "Actualizar",
        ["EnabledLabel"] = "Habilitado",
        ["OpenPanelButton"] = "Abrir",
        ["NoSkillsDetected"] = "Todavía no se detectaron skills.",
        ["NoManagedMcpServers"] = "No hay ningún servidor MCP gestionado configurado.",
        ["SetupCheckingTitle"] = "Comprobando el entorno de Codex",
        ["SetupCheckingSummary"] = "Validando el ejecutable de Codex, la autenticación y la configuración local.",
        ["SetupMissingExecutableTitle"] = "No se encontró el runtime de Codex",
        ["SetupMissingExecutableSummary"] = "No se pudo resolver ningún ejecutable compatible de Codex a partir de la ruta configurada o del entorno local.",
        ["SetupMissingAuthTitle"] = "Codex está disponible, pero falta la autenticación de OpenAI",
        ["SetupMissingAuthSummary"] = "Autentícate con Codex o proporciona un OPENAI_API_KEY antes de iniciar una sesión.",
        ["SetupMissingProviderAuthTitle"] = "Faltan las credenciales del proveedor de Codex",
        ["SetupMissingProviderAuthSummary"] = "El proveedor activo está configurado en config.toml, pero sus credenciales requeridas no están disponibles.",
        ["SetupReadyTitle"] = "Codex está listo",
        ["SetupReadySummary"] = "La extensión puede iniciar sesiones con el ejecutable resuelto de Codex.",
        ["SetupErrorTitle"] = "La configuración de Codex requiere atención",
        ["SetupErrorSummary"] = "La extensión encontró Codex, pero no pudo validar el entorno.",
        ["SetupInstallButton"] = "Copiar comando de instalación",
        ["SetupLoginButton"] = "Abrir inicio de sesión",
        ["SetupRefreshButton"] = "Actualizar estado",
        ["SetupSettingsButton"] = "Abrir configuración",
        ["SetupInstallHint"] = "Comando de instalación",
        ["SetupExecutableHint"] = "Ejecutable",
        ["SetupAuthHint"] = "Autenticación",
        ["SetupVersionHint"] = "Versión",
        ["SetupAuthFileLabel"] = "Usando ~/.codex/auth.json",
        ["SetupApiKeyLabel"] = "Usando OPENAI_API_KEY",
        ["SetupManagedLoginLabel"] = "Usando el inicio de sesión de Codex",
        ["SetupConfigProviderLabel"] = "Usando el proveedor de config.toml",
        ["SetupConfigProfileLabelFormat"] = "Usando el perfil {0} de config.toml",
        ["SetupMissingAuthDetail"] = "Ejecuta `codex login` o configura una API key de OpenAI.",
        ["SetupMissingProviderAuthDetail"] = "Actualiza config.toml o define la variable de entorno del proveedor antes de iniciar una sesión.",
        ["SetupInstallDetail"] = "Instalación recomendada: `npm install -g @openai/codex`",
        ["DeleteHistoryTooltip"] = "Eliminar del historial",
        ["CopyButton"] = "Copiar",
        ["SelectAllButton"] = "Seleccionar todo",
        ["MarkdownTextModeLabel"] = "Texto",
        ["MarkdownRenderedModeLabel"] = "Renderizado",
        ["RunningStatus"] = "Ejecutando",
        ["ReadyStatus"] = "Listo",
        ["ContextWindowDetailFormat"] = "{0} usados ({1} restantes)",
        ["IdeContextPrefix"] = "Contexto actual del IDE:",
        ["MermaidDiagramLabel"] = "Diagrama",
        ["MermaidCodeLabel"] = "Código",
        ["MermaidLoadingPreview"] = "Cargando vista previa de Mermaid...",
        ["MermaidInitFailed"] = "No se pudo iniciar la vista previa de Mermaid en este equipo.",
        ["MermaidLoadFailedFormat"] = "No se pudo cargar la vista previa de Mermaid: {0}.",
        ["MermaidRenderFailed"] = "No se pudo renderizar la vista previa de Mermaid.",
        ["MermaidRenderFailedFormat"] = "No se pudo renderizar la vista previa de Mermaid: {0}.",
        ["MermaidFreezeFailed"] = "No se pudo congelar la vista previa de Mermaid.",
        ["MermaidLoadTimeout"] = "La vista previa de Mermaid no pudo terminar de cargarse.",
        ["MermaidPreviewFallback"] = "No se pudo crear una vista previa de este Mermaid. Usa el interruptor para alternar entre diagrama y código.",
        ["MermaidPreviewScriptError"] = "Error al cargar la vista previa de Mermaid.",
        ["ToolWindowErrorMessage"] = "La ventana de Codex encontró un error durante la inicialización.",
        ["SettingsToolWindowErrorMessage"] = "La ventana de configuración de Codex encontró un error durante la inicialización.",
        ["OpenWindowFailedMessage"] = "La ventana de Codex no pudo abrirse.",
        ["ExecutionCanceledTag"] = "cancelado",
        ["ExecutionErrorTag"] = "error",
        ["ExtensionContextPrefix"] = "Contexto de la extensión: el directorio de trabajo actual del proyecto abierto es \"",
        ["PreferredMcpPrefix"] = "Atajos MCP preferidos para esta solicitud:",
        ["IdeContextSolutionLabel"] = "Solución:",
        ["IdeContextActiveDocumentLabel"] = "Documento activo:",
        ["IdeContextSelectedItemsLabel"] = "Elementos seleccionados:",
        ["IdeContextOpenFilesLabel"] = "Archivos abiertos:",
        ["IdeContextSelectionLabel"] = "Selección activa:",
        ["InvalidSkillNameMessage"] = "Nombre de skill no válido.",
        ["SkillTemplateSummary"] = "Describe aquí cuándo usar esta skill y qué problema resuelve.",
        ["SkillTemplateWhenToUseHeading"] = "## Cuándo usar",
        ["SkillTemplateWhenToUseBullet"] = "- Explica qué solicitudes deben activar esta skill.",
        ["SkillTemplateFlowHeading"] = "## Flujo",
        ["SkillTemplateFlowStep1"] = "1. Describe el primer paso.",
        ["SkillTemplateFlowStep2"] = "2. Enumera las validaciones o cuidados.",
        ["SkillTemplateFlowStep3"] = "3. Finaliza con el resultado esperado.",
        ["CodexDetectedLabel"] = "Codex detectado",
        ["EventPlanTitle"] = "Plan",
        ["EventPlanUpdated"] = "Plan actualizado",
        ["EventReasoningTitle"] = "Razonamiento",
        ["EventReasoningUpdated"] = "Razonamiento actualizado",
        ["EventCommandTitle"] = "Comando",
        ["EventWorkingDirectoryLabel"] = "Directorio de trabajo",
        ["EventOutputLabel"] = "Salida",
        ["EventFileChangesTitle"] = "Cambios de archivos",
        ["EventUpdatedFiles"] = "archivos actualizados",
        ["EventFileUpdated"] = "archivo actualizado",
        ["EventMcpToolTitle"] = "Herramienta MCP",
        ["EventArgumentsLabel"] = "Argumentos",
        ["EventErrorLabel"] = "Error",
        ["EventResultLabel"] = "Resultado",
        ["EventToolTitle"] = "Herramienta",
        ["EventAgentToolTitle"] = "Herramienta de agente",
        ["EventAgentToolUsed"] = "Herramienta de agente usada",
        ["EventPromptLabel"] = "Prompt",
        ["EventWebSearchTitle"] = "Búsqueda web",
        ["EventImageViewTitle"] = "Vista de imagen",
        ["EventImageViewed"] = "Imagen vista",
        ["EventImageGenerationTitle"] = "Generación de imagen",
        ["EventImageGenerated"] = "Imagen generada",
        ["EventReviewModeTitle"] = "Modo de revisión",
        ["EventEnteredReviewMode"] = "Entró en modo de revisión",
        ["EventExitedReviewMode"] = "Salió del modo de revisión",
        ["EventContextTitle"] = "Contexto",
        ["EventConversationContextCompacted"] = "Contexto de la conversación compactado",
        ["EventToolCall"] = "Llamada de herramienta",
        ["EventCommandExecuted"] = "Comando ejecutado",
        ["EventMoreFormat"] = "+{0} más",
        ["EventMoreFilesFormat"] = "+{0} archivos más",
        ["EventPendingStatus"] = "pendiente",
        ["EventInProgressStatus"] = "en curso",
        ["EventCompletedStatus"] = "completado",
        ["EventFailedStatus"] = "falló",
        ["ToolWindowXamlLoadLogMessage"] = "No se pudo cargar el XAML de la ventana de Codex.",
        ["ToolWindowViewModelCreateLogMessage"] = "No se pudo crear el view model de la ventana de Codex.",
        ["SettingsToolWindowXamlLoadLogMessage"] = "No se pudo cargar el XAML de la ventana de configuración de Codex.",
        ["SettingsToolWindowViewModelCreateLogMessage"] = "No se pudo crear el view model de la ventana de configuración de Codex.",
        ["ToolWindowInitializeLogMessage"] = "No se pudo inicializar el contenido de la ventana de Codex.",
        ["SettingsToolWindowInitializeLogMessage"] = "No se pudo inicializar la ventana de configuración de Codex.",
        ["SettingsToolWindowOpenLogMessage"] = "No se pudo abrir la ventana de configuración de Codex.",
        ["ToolWindowOpenLogMessage"] = "No se pudo abrir la ventana de Codex.",
        ["AsyncPanelInitializeLogMessage"] = "Se produjo un error durante la inicialización asíncrona del panel de Codex.",
        ["StartTurnFailedMessage"] = "No se pudo iniciar el turno.",
        ["AppServerValidationFailed"] = "No se pudo validar la compatibilidad con app-server del Codex CLI instalado.",
        ["AppServerUnsupported"] = "El Codex CLI instalado no parece ser compatible con `app-server`. Actualiza Codex CLI e inténtalo de nuevo.",
        ["AppServerClosedUnexpectedly"] = "El app server de Codex se cerró inesperadamente.",
        ["AppServerRequestFailed"] = "La solicitud al app server falló.",
        ["AppServerUnavailable"] = "El app server de Codex no está disponible.",
        ["EventCommentaryTitle"] = "Comentario",
        ["EventMcpProgressTitle"] = "Progreso de MCP",
        ["OutputTagSetup"] = "configuración",
        ["OutputTagAuth"] = "autenticación",
        ["OutputTagSkills"] = "skills",
        ["OutputTagRemoteSkills"] = "skills-remotas",
        ["OutputTagInit"] = "inicio",
        ["OutputTagServer"] = "servidor",
        ["OutputTagStderr"] = "stderr",
        ["OutputTagAppServer"] = "app-server",
        ["OutputTagApproval"] = "aprobación",
        ["OutputTagUserInput"] = "entrada-usuario",
        ["ExitCodeLabel"] = "código de salida",
        ["ManagedMcpDefaultName"] = "nuevo-mcp",
        ["ManagedMcpDefaultUrlName"] = "nuevo-mcp-url",
        ["MermaidBundleNotFoundFormat"] = "No se encontró el recurso '{0}'."
    };

    private static readonly IReadOnlyDictionary<string, string> FrenchStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Sujets",
        ["HistoryTitle"] = "Historique",
        ["UseButton"] = "Utiliser",
        ["NewTopicButton"] = "Nouveau sujet",
        ["RenameTopicButton"] = "Renommer",
        ["RenameTopicPlaceholder"] = "Renommer le sujet sélectionné",
        ["CloseButton"] = "Fermer",
        ["PlanModeLabel"] = "Mode planification",
        ["QuestionModeLabel"] = "Mode question",
        ["AgentModeLabel"] = "Mode agent",
        ["AppsTitle"] = "Apps",
        ["McpServersTitle"] = "Serveurs MCP",
        ["SettingsTitle"] = "Configuration et détails",
        ["ExecutableLabel"] = "Exécutable",
        ["WorkingDirectoryLabel"] = "Répertoire de travail",
        ["VerbosityLabel"] = "Verbosité",
        ["ApprovalPolicyLabel"] = "Politique d'approbation",
        ["RawOutputLabel"] = "Sortie brute",
        ["InsertButton"] = "Insérer",
        ["ComposerPlaceholder"] = "Demandez n'importe quoi à Codex",
        ["AddAttachmentTooltip"] = "Joindre une image ou un fichier",
        ["PasteImageTooltip"] = "Coller l'image du presse-papiers",
        ["SendTooltip"] = "Envoyer le prompt",
        ["HistoryTooltip"] = "Ouvrir l'historique",
        ["StopTooltip"] = "Arreter la reponse",
        ["StoppingTooltip"] = "Arret en cours",
        ["SettingsTooltip"] = "Ouvrir la configuration",
        ["LocalButton"] = "Local",
        ["RemoveAttachmentHoverLabel"] = "Retirer",
        ["ApprovalCommandTitle"] = "Autorisation requise pour exécuter la commande",
        ["ApprovalFileChangeTitle"] = "Autorisation requise pour modifier des fichiers",
        ["UserInputTitle"] = "Informations supplémentaires requises",
        ["ApprovalReasonLabel"] = "Motif",
        ["ApprovalCommandLabel"] = "Commande",
        ["ApprovalWorkingDirectoryLabel"] = "Répertoire de travail",
        ["ApprovalGrantRootLabel"] = "Racine avec accès en écriture",
        ["ApprovalAccept"] = "Autoriser",
        ["ApprovalAcceptForSession"] = "Autoriser pour la session",
        ["ApprovalAcceptWithExecpolicyAmendment"] = "Toujours autoriser les commandes similaires",
        ["ApprovalApplyNetworkPolicyAmendment"] = "Appliquer la règle réseau",
        ["ApprovalDecline"] = "Refuser",
        ["ApprovalCancel"] = "Interrompre le tour",
        ["AllFilesFilter"] = "Tous les fichiers|*.*",
        ["CodexNoResponse"] = "Impossible d'obtenir une réponse de Codex.",
        ["ExecutionCanceled"] = "Exécution annulée.",
        ["ExecutionError"] = "Erreur lors de l'exécution de Codex.",
        ["ProcessingStatus"] = "Réflexion en cours...",
        ["ImagePasteErrorPrefix"] = "[image] erreur lors du collage : ",
        ["LoadTopicsErrorPrefix"] = "[sujets] erreur lors du chargement : ",
        ["LoadModelsErrorPrefix"] = "[modèles] erreur lors du chargement : ",
        ["ApprovalDefault"] = "Par défaut",
        ["ApprovalRequest"] = "Demander",
        ["ApprovalFailure"] = "Échec",
        ["ApprovalNever"] = "Jamais",
        ["ApprovalUntrusted"] = "Non fiable",
        ["SandboxReadOnly"] = "Lecture seule",
        ["SandboxWorkspace"] = "Workspace",
        ["SandboxFullAccess"] = "Accès complet",
        ["ReasoningLow"] = "Faible",
        ["ReasoningMedium"] = "Moyenne",
        ["ReasoningHigh"] = "Élevée",
        ["ReasoningMax"] = "Maximale",
        ["ReasoningMinimal"] = "Minimale",
        ["AccountTitle"] = "Compte",
        ["AccountSubtitle"] = "Gérez le compte Codex utilisé par cette extension.",
        ["SignedInAsLabel"] = "Connecté en tant que",
        ["NotSignedInLabel"] = "Non connecté",
        ["LogOutAndLogInButton"] = "Se déconnecter et se reconnecter",
        ["CodexSettingsNav"] = "Paramètres Codex",
        ["IdeSettingsNav"] = "Paramètres de l'IDE",
        ["McpSettingsNav"] = "Paramètres MCP",
        ["SkillsSettingsNav"] = "Paramètres des skills",
        ["LanguageNav"] = "Langue",
        ["HistoryNav"] = "Historique",
        ["LanguageTitle"] = "Langue",
        ["LanguageSubtitle"] = "Remplace la détection automatique de la langue pour l'interface de l'extension.",
        ["LanguageAutoOption"] = "Automatique",
        ["CurrentLanguageLabel"] = "Langue actuelle",
        ["HistoryTopicsTitle"] = "Conversations précédentes",
        ["HistorySearchPlaceholder"] = "Rechercher dans l'historique",
        ["AllTasksLabel"] = "Tout l'historique",
        ["ViewAllTasksLabel"] = "Voir tout l'historique",
        ["TasksTitle"] = "Historique",
        ["RecentTasksTitle"] = "Historique récent",
        ["NoTasksDetected"] = "Aucun historique de conversation pour le moment.",
        ["PersonalAccountLabel"] = "Compte personnel",
        ["OpenAgentSettingsLabel"] = "Ouvrir les paramètres de l'agent",
        ["ReadDocsLabel"] = "Lire la documentation",
        ["OpenConfigTomlLabel"] = "Ouvrir config.toml",
        ["SearchLanguagesPlaceholder"] = "Rechercher une langue",
        ["NoLanguagesFound"] = "Aucune langue trouvée.",
        ["KeyboardShortcutsLabel"] = "Raccourcis clavier",
        ["LogOutLabel"] = "Se déconnecter",
        ["NoTopicsAvailable"] = "Aucun sujet précédent pour le moment.",
        ["ReasoningEffortLabel"] = "Effort de raisonnement",
        ["DetectedSkillsTitle"] = "Skills détectées",
        ["CodexConfigLabel"] = "Configuration Codex",
        ["CodexSkillsLabel"] = "Dossier des skills",
        ["ManagedMcpTitle"] = "Serveurs MCP gérés",
        ["ManagedMcpDescription"] = "Configurez des entrées MCP depuis l'extension sans modifier le TOML manuellement.",
        ["ManagedMcpNameLabel"] = "Nom du serveur",
        ["ManagedMcpTransportLabel"] = "Transport",
        ["ManagedMcpCommandLabel"] = "Commande",
        ["ManagedMcpArgsLabel"] = "Arguments, un par ligne",
        ["ManagedMcpUrlLabel"] = "URL",
        ["ManagedMcpStdioOption"] = "Commande (stdio)",
        ["ManagedMcpUrlOption"] = "URL",
        ["ManagedMcpAddStdioButton"] = "Ajouter stdio",
        ["ManagedMcpAddUrlButton"] = "Ajouter une URL",
        ["ManagedMcpRemoveButton"] = "Retirer",
        ["ManagedMcpApplyButton"] = "Appliquer et actualiser",
        ["ManagedMcpHint"] = "Seules les entrées valides et activées sont appliquées. Les noms valides utilisent des lettres, des chiffres, '-' ou '_'.",
        ["SkillsTitle"] = "Skills",
        ["SkillsDescription"] = "Créez et ouvrez des skills globales au même endroit où vous gérez l'intégration Codex.",
        ["SkillsLearnMore"] = "Donnez des superpouvoirs à Codex.",
        ["InstalledSkillsTitle"] = "Installées",
        ["RecommendedSkillsTitle"] = "Recommandées",
        ["SearchSkillsPlaceholder"] = "Rechercher des skills",
        ["InstallSkillButton"] = "Installer",
        ["NoRecommendedSkills"] = "Aucune skill recommandée disponible.",
        ["IncludeIdeContextLabel"] = "Inclure le contexte de l'IDE",
        ["SpeedLabel"] = "Vitesse",
        ["SpeedDefault"] = "Standard",
        ["SpeedFast"] = "Rapide",
        ["SpeedFlex"] = "Flex",
        ["AddPhotosFilesMenu"] = "Ajouter des photos et des fichiers",
        ["McpShortcutsTitle"] = "Raccourcis MCP",
        ["PermissionsTitle"] = "Autorisations",
        ["DefaultPermissionsLabel"] = "Autorisations par défaut",
        ["ContextWindowTitle"] = "Fenêtre de contexte",
        ["ContinueInTitle"] = "Continuer dans",
        ["LocalProjectLabel"] = "Projet local",
        ["RateLimitsTitle"] = "Limites restantes",
        ["RateLimitsUnavailable"] = "Limites non disponibles.",
        ["PlanLabelShort"] = "Plan",
        ["PrimaryWindowLabel"] = "Fenêtre principale",
        ["SecondaryWindowLabel"] = "Fenêtre secondaire",
        ["CreditsLabel"] = "Crédits",
        ["RateLimitRemainingSuffix"] = "restants",
        ["RateLimitResetsPrefix"] = "reinitialise",
        ["RateLimitUnlimitedLabel"] = "Illimite",
        ["RateLimitWeeklyLabel"] = "Hebdomadaire",
        ["PreferredMcpTitle"] = "MCP préférés",
        ["SkillNameLabel"] = "Nom de la skill",
        ["SkillDescriptionLabel"] = "Description initiale",
        ["CreateSkillButton"] = "Créer une skill",
        ["OpenSkillsFolderButton"] = "Ouvrir le dossier des skills",
        ["OpenConfigButton"] = "Ouvrir la config",
        ["SkillOpenButton"] = "Ouvrir",
        ["RefreshStatusButton"] = "Actualiser",
        ["EnabledLabel"] = "Activé",
        ["OpenPanelButton"] = "Ouvrir",
        ["NoSkillsDetected"] = "Aucune skill détectée pour le moment.",
        ["NoManagedMcpServers"] = "Aucun serveur MCP géré n'est configuré.",
        ["SetupCheckingTitle"] = "Vérification de l'environnement Codex",
        ["SetupCheckingSummary"] = "Validation de l'exécutable Codex, de l'authentification et de la configuration locale.",
        ["SetupMissingExecutableTitle"] = "Runtime Codex introuvable",
        ["SetupMissingExecutableSummary"] = "Aucun exécutable Codex compatible n'a pu être résolu à partir du chemin configuré ou de l'environnement local.",
        ["SetupMissingAuthTitle"] = "Codex est disponible, mais l'authentification OpenAI manque",
        ["SetupMissingAuthSummary"] = "Authentifiez-vous avec Codex ou fournissez un OPENAI_API_KEY avant de démarrer une session.",
        ["SetupMissingProviderAuthTitle"] = "Les identifiants du fournisseur Codex sont manquants",
        ["SetupMissingProviderAuthSummary"] = "Le fournisseur actif est configuré dans config.toml, mais les identifiants requis ne sont pas disponibles.",
        ["SetupReadyTitle"] = "Codex est prêt",
        ["SetupReadySummary"] = "L'extension peut démarrer des sessions avec l'exécutable Codex résolu.",
        ["SetupErrorTitle"] = "La configuration de Codex nécessite votre attention",
        ["SetupErrorSummary"] = "L'extension a trouvé Codex, mais n'a pas pu valider l'environnement.",
        ["SetupInstallButton"] = "Copier la commande d'installation",
        ["SetupLoginButton"] = "Ouvrir la connexion",
        ["SetupRefreshButton"] = "Actualiser l'état",
        ["SetupSettingsButton"] = "Ouvrir les paramètres",
        ["SetupInstallHint"] = "Commande d'installation",
        ["SetupExecutableHint"] = "Exécutable",
        ["SetupAuthHint"] = "Authentification",
        ["SetupVersionHint"] = "Version",
        ["SetupAuthFileLabel"] = "Utilisation de ~/.codex/auth.json",
        ["SetupApiKeyLabel"] = "Utilisation de OPENAI_API_KEY",
        ["SetupManagedLoginLabel"] = "Utilisation de la connexion Codex",
        ["SetupConfigProviderLabel"] = "Utilisation du fournisseur config.toml",
        ["SetupConfigProfileLabelFormat"] = "Utilisation du profil {0} depuis config.toml",
        ["SetupMissingAuthDetail"] = "Exécutez `codex login` ou configurez une clé API OpenAI.",
        ["SetupMissingProviderAuthDetail"] = "Mettez à jour config.toml ou définissez la variable d'environnement du fournisseur avant de démarrer une session.",
        ["SetupInstallDetail"] = "Installation recommandée : `npm install -g @openai/codex`",
        ["DeleteHistoryTooltip"] = "Supprimer de l'historique",
        ["CopyButton"] = "Copier",
        ["SelectAllButton"] = "Tout sélectionner",
        ["MarkdownTextModeLabel"] = "Texte",
        ["MarkdownRenderedModeLabel"] = "Rendu",
        ["RunningStatus"] = "Exécution en cours",
        ["ReadyStatus"] = "Prêt",
        ["ContextWindowDetailFormat"] = "{0} utilisés ({1} restants)",
        ["IdeContextPrefix"] = "Contexte actuel de l'IDE :",
        ["MermaidDiagramLabel"] = "Diagramme",
        ["MermaidCodeLabel"] = "Code",
        ["MermaidLoadingPreview"] = "Chargement de l'aperçu Mermaid...",
        ["MermaidInitFailed"] = "Impossible de démarrer l'aperçu Mermaid sur cette machine.",
        ["MermaidLoadFailedFormat"] = "Impossible de charger l'aperçu Mermaid : {0}.",
        ["MermaidRenderFailed"] = "Impossible de générer l'aperçu Mermaid.",
        ["MermaidRenderFailedFormat"] = "Impossible de générer l'aperçu Mermaid : {0}.",
        ["MermaidFreezeFailed"] = "Impossible de figer l'aperçu Mermaid.",
        ["MermaidLoadTimeout"] = "L'aperçu Mermaid n'a pas pu terminer son chargement.",
        ["MermaidPreviewFallback"] = "Impossible de créer un aperçu pour ce Mermaid. Utilisez le basculeur pour alterner entre le diagramme et le code.",
        ["MermaidPreviewScriptError"] = "Erreur lors du chargement de l'aperçu Mermaid.",
        ["ToolWindowErrorMessage"] = "La fenêtre Codex a rencontré une erreur pendant l'initialisation.",
        ["SettingsToolWindowErrorMessage"] = "La fenêtre des paramètres Codex a rencontré une erreur pendant l'initialisation.",
        ["OpenWindowFailedMessage"] = "La fenêtre Codex n'a pas pu s'ouvrir.",
        ["ExecutionCanceledTag"] = "annulé",
        ["ExecutionErrorTag"] = "erreur",
        ["ExtensionContextPrefix"] = "Contexte de l'extension : le répertoire de travail actuel du projet ouvert est \"",
        ["PreferredMcpPrefix"] = "Raccourcis MCP préférés pour cette demande :",
        ["IdeContextSolutionLabel"] = "Solution :",
        ["IdeContextActiveDocumentLabel"] = "Document actif :",
        ["IdeContextSelectedItemsLabel"] = "Éléments sélectionnés :",
        ["IdeContextOpenFilesLabel"] = "Fichiers ouverts :",
        ["IdeContextSelectionLabel"] = "Sélection active :",
        ["InvalidSkillNameMessage"] = "Nom de skill invalide.",
        ["SkillTemplateSummary"] = "Décrivez ici quand utiliser cette skill et quel problème elle résout.",
        ["SkillTemplateWhenToUseHeading"] = "## Quand utiliser",
        ["SkillTemplateWhenToUseBullet"] = "- Expliquez quelles demandes doivent déclencher cette skill.",
        ["SkillTemplateFlowHeading"] = "## Flux",
        ["SkillTemplateFlowStep1"] = "1. Décrivez la première étape.",
        ["SkillTemplateFlowStep2"] = "2. Listez les validations ou précautions.",
        ["SkillTemplateFlowStep3"] = "3. Terminez par le résultat attendu.",
        ["CodexDetectedLabel"] = "Codex détecté",
        ["EventPlanTitle"] = "Plan",
        ["EventPlanUpdated"] = "Plan mis à jour",
        ["EventReasoningTitle"] = "Raisonnement",
        ["EventReasoningUpdated"] = "Raisonnement mis à jour",
        ["EventCommandTitle"] = "Commande",
        ["EventWorkingDirectoryLabel"] = "Répertoire de travail",
        ["EventOutputLabel"] = "Sortie",
        ["EventFileChangesTitle"] = "Modifications de fichiers",
        ["EventUpdatedFiles"] = "fichiers mis à jour",
        ["EventFileUpdated"] = "fichier mis à jour",
        ["EventMcpToolTitle"] = "Outil MCP",
        ["EventArgumentsLabel"] = "Arguments",
        ["EventErrorLabel"] = "Erreur",
        ["EventResultLabel"] = "Résultat",
        ["EventToolTitle"] = "Outil",
        ["EventAgentToolTitle"] = "Outil agent",
        ["EventAgentToolUsed"] = "Outil agent utilisé",
        ["EventPromptLabel"] = "Prompt",
        ["EventWebSearchTitle"] = "Recherche web",
        ["EventImageViewTitle"] = "Aperçu d'image",
        ["EventImageViewed"] = "Image consultée",
        ["EventImageGenerationTitle"] = "Génération d'image",
        ["EventImageGenerated"] = "Image générée",
        ["EventReviewModeTitle"] = "Mode révision",
        ["EventEnteredReviewMode"] = "Entrée en mode révision",
        ["EventExitedReviewMode"] = "Sortie du mode révision",
        ["EventContextTitle"] = "Contexte",
        ["EventConversationContextCompacted"] = "Contexte de conversation compacté",
        ["EventToolCall"] = "Appel d'outil",
        ["EventCommandExecuted"] = "Commande exécutée",
        ["EventMoreFormat"] = "+{0} de plus",
        ["EventMoreFilesFormat"] = "+{0} fichiers de plus",
        ["EventPendingStatus"] = "en attente",
        ["EventInProgressStatus"] = "en cours",
        ["EventCompletedStatus"] = "terminé",
        ["EventFailedStatus"] = "échoué",
        ["ToolWindowXamlLoadLogMessage"] = "Impossible de charger le XAML de la fenêtre Codex.",
        ["ToolWindowViewModelCreateLogMessage"] = "Impossible de créer le view model de la fenêtre Codex.",
        ["SettingsToolWindowXamlLoadLogMessage"] = "Impossible de charger le XAML de la fenêtre des paramètres Codex.",
        ["SettingsToolWindowViewModelCreateLogMessage"] = "Impossible de créer le view model de la fenêtre des paramètres Codex.",
        ["ToolWindowInitializeLogMessage"] = "Impossible d'initialiser le contenu de la fenêtre Codex.",
        ["SettingsToolWindowInitializeLogMessage"] = "Impossible d'initialiser la fenêtre des paramètres Codex.",
        ["SettingsToolWindowOpenLogMessage"] = "Impossible d'ouvrir la fenêtre des paramètres Codex.",
        ["ToolWindowOpenLogMessage"] = "Impossible d'ouvrir la fenêtre Codex.",
        ["AsyncPanelInitializeLogMessage"] = "Échec de l'initialisation asynchrone du panneau Codex.",
        ["StartTurnFailedMessage"] = "Impossible de démarrer le tour.",
        ["AppServerValidationFailed"] = "Impossible de valider la prise en charge de app-server par le Codex CLI installé.",
        ["AppServerUnsupported"] = "Le Codex CLI installé ne semble pas prendre en charge `app-server`. Mettez à jour Codex CLI et réessayez.",
        ["AppServerClosedUnexpectedly"] = "Le serveur d'application Codex s'est fermé de façon inattendue.",
        ["AppServerRequestFailed"] = "La requête au serveur d'application a échoué.",
        ["AppServerUnavailable"] = "Le serveur d'application Codex n'est pas disponible.",
        ["EventCommentaryTitle"] = "Commentaire",
        ["EventMcpProgressTitle"] = "Progression MCP",
        ["OutputTagSetup"] = "configuration",
        ["OutputTagAuth"] = "authentification",
        ["OutputTagSkills"] = "skills",
        ["OutputTagRemoteSkills"] = "skills-distantes",
        ["OutputTagInit"] = "initialisation",
        ["OutputTagServer"] = "serveur",
        ["OutputTagStderr"] = "stderr",
        ["OutputTagAppServer"] = "app-server",
        ["OutputTagApproval"] = "approbation",
        ["OutputTagUserInput"] = "saisie-utilisateur",
        ["ExitCodeLabel"] = "code de sortie",
        ["ManagedMcpDefaultName"] = "nouveau-mcp",
        ["ManagedMcpDefaultUrlName"] = "nouveau-mcp-url",
        ["MermaidBundleNotFoundFormat"] = "La ressource '{0}' est introuvable."
    };

    private static readonly IReadOnlyDictionary<string, string> GermanStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Themen",
        ["HistoryTitle"] = "Verlauf",
        ["UseButton"] = "Verwenden",
        ["NewTopicButton"] = "Neues Thema",
        ["RenameTopicButton"] = "Umbenennen",
        ["RenameTopicPlaceholder"] = "Ausgewähltes Thema umbenennen",
        ["CloseButton"] = "Schließen",
        ["PlanModeLabel"] = "Planungsmodus",
        ["QuestionModeLabel"] = "Fragemodus",
        ["AgentModeLabel"] = "Agentmodus",
        ["AppsTitle"] = "Apps",
        ["McpServersTitle"] = "MCP-Server",
        ["SettingsTitle"] = "Konfiguration und Details",
        ["ExecutableLabel"] = "Ausführbare Datei",
        ["WorkingDirectoryLabel"] = "Arbeitsverzeichnis",
        ["VerbosityLabel"] = "Ausführlichkeit",
        ["ApprovalPolicyLabel"] = "Genehmigungsrichtlinie",
        ["RawOutputLabel"] = "Rohausgabe",
        ["InsertButton"] = "Einfügen",
        ["ComposerPlaceholder"] = "Fragen Sie Codex alles",
        ["AddAttachmentTooltip"] = "Bild oder Datei anhängen",
        ["PasteImageTooltip"] = "Bild aus der Zwischenablage einfügen",
        ["SendTooltip"] = "Prompt senden",
        ["HistoryTooltip"] = "Verlauf offnen",
        ["StopTooltip"] = "Antwort stoppen",
        ["StoppingTooltip"] = "Antwort wird gestoppt",
        ["SettingsTooltip"] = "Einstellungen öffnen",
        ["LocalButton"] = "Lokal",
        ["RemoveAttachmentHoverLabel"] = "Entfernen",
        ["ApprovalCommandTitle"] = "Genehmigung zum Ausführen des Befehls erforderlich",
        ["ApprovalFileChangeTitle"] = "Genehmigung zum Ändern von Dateien erforderlich",
        ["UserInputTitle"] = "Zusätzliche Informationen erforderlich",
        ["ApprovalReasonLabel"] = "Grund",
        ["ApprovalCommandLabel"] = "Befehl",
        ["ApprovalWorkingDirectoryLabel"] = "Arbeitsverzeichnis",
        ["ApprovalGrantRootLabel"] = "Stammordner mit Schreibzugriff",
        ["ApprovalAccept"] = "Erlauben",
        ["ApprovalAcceptForSession"] = "Für diese Sitzung erlauben",
        ["ApprovalAcceptWithExecpolicyAmendment"] = "Ähnliche Befehle immer erlauben",
        ["ApprovalApplyNetworkPolicyAmendment"] = "Netzwerkregel anwenden",
        ["ApprovalDecline"] = "Ablehnen",
        ["ApprovalCancel"] = "Turn stoppen",
        ["AllFilesFilter"] = "Alle Dateien|*.*",
        ["CodexNoResponse"] = "Es konnte keine Antwort von Codex abgerufen werden.",
        ["ExecutionCanceled"] = "Ausführung abgebrochen.",
        ["ExecutionError"] = "Fehler beim Ausführen von Codex.",
        ["ProcessingStatus"] = "Wird verarbeitet...",
        ["ImagePasteErrorPrefix"] = "[bild] Fehler beim Einfügen: ",
        ["LoadTopicsErrorPrefix"] = "[themen] Fehler beim Laden: ",
        ["LoadModelsErrorPrefix"] = "[modelle] Fehler beim Laden: ",
        ["ApprovalDefault"] = "Standard",
        ["ApprovalRequest"] = "Anfragen",
        ["ApprovalFailure"] = "Fehlschlag",
        ["ApprovalNever"] = "Nie",
        ["ApprovalUntrusted"] = "Nicht vertrauenswürdig",
        ["SandboxReadOnly"] = "Schreibgeschützt",
        ["SandboxWorkspace"] = "Workspace",
        ["SandboxFullAccess"] = "Vollzugriff",
        ["ReasoningLow"] = "Niedrig",
        ["ReasoningMedium"] = "Mittel",
        ["ReasoningHigh"] = "Hoch",
        ["ReasoningMax"] = "Maximal",
        ["ReasoningMinimal"] = "Minimal",
        ["AccountTitle"] = "Konto",
        ["AccountSubtitle"] = "Verwalten Sie das Codex-Konto, das von dieser Erweiterung verwendet wird.",
        ["SignedInAsLabel"] = "Angemeldet als",
        ["NotSignedInLabel"] = "Nicht angemeldet",
        ["LogOutAndLogInButton"] = "Abmelden und erneut anmelden",
        ["CodexSettingsNav"] = "Codex-Einstellungen",
        ["IdeSettingsNav"] = "IDE-Einstellungen",
        ["McpSettingsNav"] = "MCP-Einstellungen",
        ["SkillsSettingsNav"] = "Skill-Einstellungen",
        ["LanguageNav"] = "Sprache",
        ["HistoryNav"] = "Verlauf",
        ["LanguageTitle"] = "Sprache",
        ["LanguageSubtitle"] = "Überschreibt die automatische Spracherkennung für die Oberfläche der Erweiterung.",
        ["LanguageAutoOption"] = "Automatisch",
        ["CurrentLanguageLabel"] = "Aktuelle Sprache",
        ["HistoryTopicsTitle"] = "Vorherige Themen",
        ["HistorySearchPlaceholder"] = "Verlauf durchsuchen",
        ["AllTasksLabel"] = "Gesamter Verlauf",
        ["ViewAllTasksLabel"] = "Gesamten Verlauf anzeigen",
        ["TasksTitle"] = "Verlauf",
        ["RecentTasksTitle"] = "Letzter Verlauf",
        ["NoTasksDetected"] = "Noch kein Gesprächsverlauf.",
        ["PersonalAccountLabel"] = "Persönliches Konto",
        ["OpenAgentSettingsLabel"] = "Agent-Einstellungen öffnen",
        ["ReadDocsLabel"] = "Dokumentation lesen",
        ["OpenConfigTomlLabel"] = "config.toml öffnen",
        ["SearchLanguagesPlaceholder"] = "Sprache suchen",
        ["NoLanguagesFound"] = "Keine Sprachen gefunden.",
        ["KeyboardShortcutsLabel"] = "Tastenkombinationen",
        ["LogOutLabel"] = "Abmelden",
        ["NoTopicsAvailable"] = "Noch keine vorherigen Themen.",
        ["ReasoningEffortLabel"] = "Denkaufwand",
        ["DetectedSkillsTitle"] = "Erkannte Skills",
        ["CodexConfigLabel"] = "Codex-Konfiguration",
        ["CodexSkillsLabel"] = "Skills-Ordner",
        ["ManagedMcpTitle"] = "Verwaltete MCP-Server",
        ["ManagedMcpDescription"] = "Konfigurieren Sie MCP-Einträge in dieser Erweiterung, ohne TOML manuell zu bearbeiten.",
        ["ManagedMcpNameLabel"] = "Servername",
        ["ManagedMcpTransportLabel"] = "Transport",
        ["ManagedMcpCommandLabel"] = "Befehl",
        ["ManagedMcpArgsLabel"] = "Argumente, eine Zeile pro Eintrag",
        ["ManagedMcpUrlLabel"] = "URL",
        ["ManagedMcpStdioOption"] = "Befehl (stdio)",
        ["ManagedMcpUrlOption"] = "URL",
        ["ManagedMcpAddStdioButton"] = "Stdio hinzufügen",
        ["ManagedMcpAddUrlButton"] = "URL hinzufügen",
        ["ManagedMcpRemoveButton"] = "Entfernen",
        ["ManagedMcpApplyButton"] = "Anwenden und aktualisieren",
        ["ManagedMcpHint"] = "Nur aktivierte und gültige Einträge werden angewendet. Gültige Namen verwenden Buchstaben, Zahlen, '-' oder '_'.",
        ["SkillsTitle"] = "Skills",
        ["SkillsDescription"] = "Erstellen und öffnen Sie globale Skills an derselben Stelle, an der Sie die Codex-Integration verwalten.",
        ["SkillsLearnMore"] = "Geben Sie Codex Superkräfte.",
        ["InstalledSkillsTitle"] = "Installiert",
        ["RecommendedSkillsTitle"] = "Empfohlen",
        ["SearchSkillsPlaceholder"] = "Skills suchen",
        ["InstallSkillButton"] = "Installieren",
        ["NoRecommendedSkills"] = "Keine empfohlenen Skills verfügbar.",
        ["IncludeIdeContextLabel"] = "IDE-Kontext einbeziehen",
        ["SpeedLabel"] = "Geschwindigkeit",
        ["SpeedDefault"] = "Standard",
        ["SpeedFast"] = "Schnell",
        ["SpeedFlex"] = "Flex",
        ["AddPhotosFilesMenu"] = "Fotos und Dateien hinzufügen",
        ["McpShortcutsTitle"] = "MCP-Kurzbefehle",
        ["PermissionsTitle"] = "Berechtigungen",
        ["DefaultPermissionsLabel"] = "Standardberechtigungen",
        ["ContextWindowTitle"] = "Kontextfenster",
        ["ContinueInTitle"] = "Fortfahren in",
        ["LocalProjectLabel"] = "Lokales Projekt",
        ["RateLimitsTitle"] = "Verbleibende Limits",
        ["RateLimitsUnavailable"] = "Limits nicht verfügbar.",
        ["PlanLabelShort"] = "Plan",
        ["PrimaryWindowLabel"] = "Primäres Fenster",
        ["SecondaryWindowLabel"] = "Sekundäres Fenster",
        ["CreditsLabel"] = "Credits",
        ["RateLimitRemainingSuffix"] = "verbleibend",
        ["RateLimitResetsPrefix"] = "Reset",
        ["RateLimitUnlimitedLabel"] = "Unbegrenzt",
        ["RateLimitWeeklyLabel"] = "Wochentlich",
        ["PreferredMcpTitle"] = "Bevorzugte MCPs",
        ["SkillNameLabel"] = "Skill-Name",
        ["SkillDescriptionLabel"] = "Anfangsbeschreibung",
        ["CreateSkillButton"] = "Skill erstellen",
        ["OpenSkillsFolderButton"] = "Skills-Ordner öffnen",
        ["OpenConfigButton"] = "Config öffnen",
        ["SkillOpenButton"] = "Öffnen",
        ["RefreshStatusButton"] = "Aktualisieren",
        ["EnabledLabel"] = "Aktiviert",
        ["OpenPanelButton"] = "Öffnen",
        ["NoSkillsDetected"] = "Noch keine Skills erkannt.",
        ["NoManagedMcpServers"] = "Kein verwalteter MCP-Server konfiguriert.",
        ["SetupCheckingTitle"] = "Codex-Umgebung wird überprüft",
        ["SetupCheckingSummary"] = "Codex-Datei, Authentifizierung und lokale Konfiguration werden geprüft.",
        ["SetupMissingExecutableTitle"] = "Codex-Runtime nicht gefunden",
        ["SetupMissingExecutableSummary"] = "Es konnte keine kompatible Codex-Datei aus dem konfigurierten Pfad oder der lokalen Umgebung aufgelöst werden.",
        ["SetupMissingAuthTitle"] = "Codex ist verfügbar, aber die OpenAI-Authentifizierung fehlt",
        ["SetupMissingAuthSummary"] = "Melden Sie sich mit Codex an oder geben Sie einen OPENAI_API_KEY an, bevor Sie eine Sitzung starten.",
        ["SetupMissingProviderAuthTitle"] = "Die Anmeldedaten des Codex-Providers fehlen",
        ["SetupMissingProviderAuthSummary"] = "Der aktive Provider ist in config.toml konfiguriert, aber die erforderlichen Zugangsdaten sind nicht verfügbar.",
        ["SetupReadyTitle"] = "Codex ist bereit",
        ["SetupReadySummary"] = "Die Erweiterung kann Sitzungen mit der aufgelösten Codex-Datei starten.",
        ["SetupErrorTitle"] = "Die Codex-Konfiguration benötigt Aufmerksamkeit",
        ["SetupErrorSummary"] = "Die Erweiterung hat Codex gefunden, konnte die Umgebung aber nicht validieren.",
        ["SetupInstallButton"] = "Installationsbefehl kopieren",
        ["SetupLoginButton"] = "Anmeldung öffnen",
        ["SetupRefreshButton"] = "Status aktualisieren",
        ["SetupSettingsButton"] = "Einstellungen öffnen",
        ["SetupInstallHint"] = "Installationsbefehl",
        ["SetupExecutableHint"] = "Ausführbare Datei",
        ["SetupAuthHint"] = "Authentifizierung",
        ["SetupVersionHint"] = "Version",
        ["SetupAuthFileLabel"] = "Verwendung von ~/.codex/auth.json",
        ["SetupApiKeyLabel"] = "Verwendung von OPENAI_API_KEY",
        ["SetupManagedLoginLabel"] = "Verwendung der Codex-Anmeldung",
        ["SetupConfigProviderLabel"] = "Verwendung des Providers aus config.toml",
        ["SetupConfigProfileLabelFormat"] = "Verwendung des Profils {0} aus config.toml",
        ["SetupMissingAuthDetail"] = "Führen Sie `codex login` aus oder konfigurieren Sie einen OpenAI-API-Schlüssel.",
        ["SetupMissingProviderAuthDetail"] = "Aktualisieren Sie config.toml oder setzen Sie die Umgebungsvariable des Providers, bevor Sie eine Sitzung starten.",
        ["SetupInstallDetail"] = "Empfohlene Installation: `npm install -g @openai/codex`",
        ["DeleteHistoryTooltip"] = "Aus Verlauf löschen",
        ["CopyButton"] = "Kopieren",
        ["SelectAllButton"] = "Alles auswählen",
        ["MarkdownTextModeLabel"] = "Text",
        ["MarkdownRenderedModeLabel"] = "Gerendert",
        ["RunningStatus"] = "Wird ausgeführt",
        ["ReadyStatus"] = "Bereit",
        ["ContextWindowDetailFormat"] = "{0} verwendet ({1} übrig)",
        ["IdeContextPrefix"] = "Aktueller IDE-Kontext:",
        ["MermaidDiagramLabel"] = "Diagramm",
        ["MermaidCodeLabel"] = "Code",
        ["MermaidLoadingPreview"] = "Mermaid-Vorschau wird geladen...",
        ["MermaidInitFailed"] = "Die Mermaid-Vorschau konnte auf diesem Gerät nicht gestartet werden.",
        ["MermaidLoadFailedFormat"] = "Die Mermaid-Vorschau konnte nicht geladen werden: {0}.",
        ["MermaidRenderFailed"] = "Die Mermaid-Vorschau konnte nicht gerendert werden.",
        ["MermaidRenderFailedFormat"] = "Die Mermaid-Vorschau konnte nicht gerendert werden: {0}.",
        ["MermaidFreezeFailed"] = "Die Mermaid-Vorschau konnte nicht eingefroren werden.",
        ["MermaidLoadTimeout"] = "Die Mermaid-Vorschau konnte nicht vollständig geladen werden.",
        ["MermaidPreviewFallback"] = "Für dieses Mermaid konnte keine Vorschau erstellt werden. Verwenden Sie den Schalter, um zwischen Diagramm und Code zu wechseln.",
        ["MermaidPreviewScriptError"] = "Fehler beim Laden der Mermaid-Vorschau.",
        ["ToolWindowErrorMessage"] = "Das Codex-Fenster ist bei der Initialisierung auf einen Fehler gestoßen.",
        ["SettingsToolWindowErrorMessage"] = "Das Codex-Einstellungsfenster ist bei der Initialisierung auf einen Fehler gestoßen.",
        ["OpenWindowFailedMessage"] = "Das Codex-Fenster konnte nicht geöffnet werden.",
        ["ExecutionCanceledTag"] = "abgebrochen",
        ["ExecutionErrorTag"] = "fehler",
        ["ExtensionContextPrefix"] = "Erweiterungskontext: Das aktuelle Arbeitsverzeichnis des geöffneten Projekts ist \"",
        ["PreferredMcpPrefix"] = "Bevorzugte MCP-Kurzbefehle für diese Anfrage:",
        ["IdeContextSolutionLabel"] = "Lösung:",
        ["IdeContextActiveDocumentLabel"] = "Aktives Dokument:",
        ["IdeContextSelectedItemsLabel"] = "Ausgewählte Elemente:",
        ["IdeContextOpenFilesLabel"] = "Geöffnete Dateien:",
        ["IdeContextSelectionLabel"] = "Aktive Auswahl:",
        ["InvalidSkillNameMessage"] = "Ungültiger Skill-Name.",
        ["SkillTemplateSummary"] = "Beschreiben Sie hier, wann diese Skill verwendet werden soll und welches Problem sie löst.",
        ["SkillTemplateWhenToUseHeading"] = "## Wann verwenden",
        ["SkillTemplateWhenToUseBullet"] = "- Erklären Sie, welche Anfragen diese Skill auslösen sollen.",
        ["SkillTemplateFlowHeading"] = "## Ablauf",
        ["SkillTemplateFlowStep1"] = "1. Beschreiben Sie den ersten Schritt.",
        ["SkillTemplateFlowStep2"] = "2. Listen Sie Prüfungen oder Hinweise auf.",
        ["SkillTemplateFlowStep3"] = "3. Schließen Sie mit dem erwarteten Ergebnis ab.",
        ["CodexDetectedLabel"] = "Codex erkannt",
        ["EventPlanTitle"] = "Plan",
        ["EventPlanUpdated"] = "Plan aktualisiert",
        ["EventReasoningTitle"] = "Begründung",
        ["EventReasoningUpdated"] = "Begründung aktualisiert",
        ["EventCommandTitle"] = "Befehl",
        ["EventWorkingDirectoryLabel"] = "Arbeitsverzeichnis",
        ["EventOutputLabel"] = "Ausgabe",
        ["EventFileChangesTitle"] = "Dateiänderungen",
        ["EventUpdatedFiles"] = "aktualisierte Dateien",
        ["EventFileUpdated"] = "Datei aktualisiert",
        ["EventMcpToolTitle"] = "MCP-Werkzeug",
        ["EventArgumentsLabel"] = "Argumente",
        ["EventErrorLabel"] = "Fehler",
        ["EventResultLabel"] = "Ergebnis",
        ["EventToolTitle"] = "Werkzeug",
        ["EventAgentToolTitle"] = "Agentenwerkzeug",
        ["EventAgentToolUsed"] = "Agentenwerkzeug verwendet",
        ["EventPromptLabel"] = "Prompt",
        ["EventWebSearchTitle"] = "Websuche",
        ["EventImageViewTitle"] = "Bildansicht",
        ["EventImageViewed"] = "Bild angesehen",
        ["EventImageGenerationTitle"] = "Bildgenerierung",
        ["EventImageGenerated"] = "Bild generiert",
        ["EventReviewModeTitle"] = "Prüfmodus",
        ["EventEnteredReviewMode"] = "Prüfmodus betreten",
        ["EventExitedReviewMode"] = "Prüfmodus verlassen",
        ["EventContextTitle"] = "Kontext",
        ["EventConversationContextCompacted"] = "Gesprächskontext komprimiert",
        ["EventToolCall"] = "Werkzeugaufruf",
        ["EventCommandExecuted"] = "Befehl ausgeführt",
        ["EventMoreFormat"] = "+{0} mehr",
        ["EventMoreFilesFormat"] = "+{0} weitere Dateien",
        ["EventPendingStatus"] = "ausstehend",
        ["EventInProgressStatus"] = "in Bearbeitung",
        ["EventCompletedStatus"] = "abgeschlossen",
        ["EventFailedStatus"] = "fehlgeschlagen",
        ["ToolWindowXamlLoadLogMessage"] = "Das XAML des Codex-Fensters konnte nicht geladen werden.",
        ["ToolWindowViewModelCreateLogMessage"] = "Das ViewModel des Codex-Fensters konnte nicht erstellt werden.",
        ["SettingsToolWindowXamlLoadLogMessage"] = "Das XAML des Codex-Einstellungsfensters konnte nicht geladen werden.",
        ["SettingsToolWindowViewModelCreateLogMessage"] = "Das ViewModel des Codex-Einstellungsfensters konnte nicht erstellt werden.",
        ["ToolWindowInitializeLogMessage"] = "Der Inhalt des Codex-Fensters konnte nicht initialisiert werden.",
        ["SettingsToolWindowInitializeLogMessage"] = "Das Codex-Einstellungsfenster konnte nicht initialisiert werden.",
        ["SettingsToolWindowOpenLogMessage"] = "Das Codex-Einstellungsfenster konnte nicht geöffnet werden.",
        ["ToolWindowOpenLogMessage"] = "Das Codex-Fenster konnte nicht geöffnet werden.",
        ["AsyncPanelInitializeLogMessage"] = "Bei der asynchronen Initialisierung des Codex-Bereichs ist ein Fehler aufgetreten.",
        ["StartTurnFailedMessage"] = "Der Turn konnte nicht gestartet werden.",
        ["AppServerValidationFailed"] = "Die Unterstützung für `app-server` im installierten Codex CLI konnte nicht überprüft werden.",
        ["AppServerUnsupported"] = "Das installierte Codex CLI scheint `app-server` nicht zu unterstützen. Aktualisieren Sie Codex CLI und versuchen Sie es erneut.",
        ["AppServerClosedUnexpectedly"] = "Der Codex-App-Server wurde unerwartet beendet.",
        ["AppServerRequestFailed"] = "Die Anfrage an den App-Server ist fehlgeschlagen.",
        ["AppServerUnavailable"] = "Der Codex-App-Server ist nicht verfügbar.",
        ["EventCommentaryTitle"] = "Kommentar",
        ["EventMcpProgressTitle"] = "MCP-Fortschritt",
        ["OutputTagSetup"] = "einrichtung",
        ["OutputTagAuth"] = "authentifizierung",
        ["OutputTagSkills"] = "skills",
        ["OutputTagRemoteSkills"] = "skills-remote",
        ["OutputTagInit"] = "initialisierung",
        ["OutputTagServer"] = "server",
        ["OutputTagStderr"] = "stderr",
        ["OutputTagAppServer"] = "app-server",
        ["OutputTagApproval"] = "genehmigung",
        ["OutputTagUserInput"] = "benutzereingabe",
        ["ExitCodeLabel"] = "Exit-Code",
        ["ManagedMcpDefaultName"] = "neues-mcp",
        ["ManagedMcpDefaultUrlName"] = "neues-mcp-url",
        ["MermaidBundleNotFoundFormat"] = "Die Ressource '{0}' wurde nicht gefunden."
    };

    private readonly IReadOnlyDictionary<string, string> _strings;

    public LocalizationService(string? languageOverride = null)
    {
        var preferredCulture = ResolvePreferredCulture(languageOverride);

        Culture = ResolveSupportedCulture(preferredCulture);
        LanguageTag = Culture.Name;
        _strings = GetLanguageStrings(Culture);
    }

    public CultureInfo Culture { get; }

    public string LanguageTag { get; }

    public string TopicsTitle => Get("TopicsTitle");
    public string HistoryTitle => Get("HistoryTitle");
    public string HistoryTopicsTitle => GetLocalizedString("HistoryTopicsTitle", "Conversas anteriores");
    public string UseButton => Get("UseButton");
    public string NewTopicButton => Get("NewTopicButton");
    public string RenameTopicButton => Get("RenameTopicButton");
    public string RenameTopicPlaceholder => Get("RenameTopicPlaceholder");
    public string CloseButton => GetLocalizedString("CloseButton", "Fechar");
    public string AccountTitle => GetLocalizedString("AccountTitle", "Conta");
    public string AccountSubtitle => GetLocalizedString("AccountSubtitle", "Gerencie a conta do Codex usada por esta extensão.");
    public string SignedInAsLabel => GetLocalizedString("SignedInAsLabel", "Conectado como");
    public string NotSignedInLabel => GetLocalizedString("NotSignedInLabel", "Sem login");
    public string LogOutAndLogInButton => GetLocalizedString("LogOutAndLogInButton", "Sair e fazer login novamente");
    public string CodexSettingsNav => GetLocalizedString("CodexSettingsNav", "Configurações do Codex");
    public string IdeSettingsNav => GetLocalizedString("IdeSettingsNav", "Configurações da IDE");
    public string McpSettingsNav => GetLocalizedString("McpSettingsNav", "Configurações de MCP");
    public string SkillsSettingsNav => GetLocalizedString("SkillsSettingsNav", "Configurações de skills");
    public string LanguageNav => GetLocalizedString("LanguageNav", "Idioma");
    public string HistoryNav => GetLocalizedString("HistoryNav", "Histórico");
    public string LanguageTitle => GetLocalizedString("LanguageTitle", "Idioma");
    public string LanguageSubtitle => GetLocalizedString("LanguageSubtitle", "Substitua a detecção automática de idioma da interface.");
    public string LanguageAutoOption => GetLocalizedString("LanguageAutoOption", "Automático");
    public string CurrentLanguageLabel => GetLocalizedString("CurrentLanguageLabel", "Idioma atual");
    public string HistorySearchPlaceholder => GetLocalizedString("HistorySearchPlaceholder", "Procurar no historico");
    public string AllTasksLabel => GetLocalizedString("AllTasksLabel", "Todo o historico");
    public string ViewAllTasksLabel => GetLocalizedString("ViewAllTasksLabel", "Ver historico completo");
    public string TasksTitle => GetLocalizedString("TasksTitle", "Historico");
    public string RecentTasksTitle => GetLocalizedString("RecentTasksTitle", "Historico recente");
    public string NoTasksDetected => GetLocalizedString("NoTasksDetected", "Nenhum historico ainda.");
    public string PersonalAccountLabel => AccountTitle;
    public string OpenAgentSettingsLabel => GetLocalizedString("OpenAgentSettingsLabel", "Abrir configurações do agente");
    public string ReadDocsLabel => GetLocalizedString("ReadDocsLabel", "Ler documentação");
    public string OpenConfigTomlLabel => GetLocalizedString("OpenConfigTomlLabel", "Abrir config.toml");
    public string SearchLanguagesPlaceholder => GetLocalizedString("SearchLanguagesPlaceholder", "Procurar idioma");
    public string NoLanguagesFound => GetLocalizedString("NoLanguagesFound", "Nenhum idioma encontrado.");
    public string KeyboardShortcutsLabel => GetLocalizedString("KeyboardShortcutsLabel", "Atalhos do teclado");
    public string LogOutLabel => GetLocalizedString("LogOutLabel", "Sair");
    public string NoTopicsAvailable => GetLocalizedString("NoTopicsAvailable", "Nenhuma conversa anterior ainda.");
    public string PlanModeLabel => Get("PlanModeLabel");
    public string QuestionModeLabel => Get("QuestionModeLabel");
    public string AgentModeLabel => Get("AgentModeLabel");
    public string ReasoningEffortLabel => GetLocalizedString("ReasoningEffortLabel", "Esforço de raciocínio");
    public string AppsTitle => Get("AppsTitle");
    public string McpServersTitle => Get("McpServersTitle");
    public string DetectedSkillsTitle => GetLocalizedString("DetectedSkillsTitle", "Skills detectadas");
    public string SettingsTitle => Get("SettingsTitle");
    public string ExecutableLabel => Get("ExecutableLabel");
    public string WorkingDirectoryLabel => Get("WorkingDirectoryLabel");
    public string CodexConfigLabel => GetLocalizedString("CodexConfigLabel", "Configuração do Codex");
    public string CodexSkillsLabel => GetLocalizedString("CodexSkillsLabel", "Pasta de skills");
    public string ManagedMcpTitle => GetLocalizedString("ManagedMcpTitle", "MCPs gerenciados");
    public string ManagedMcpDescription => GetLocalizedString("ManagedMcpDescription", "Configure entradas de MCP pela interface da extensão, sem editar TOML manualmente.");
    public string ManagedMcpNameLabel => GetLocalizedString("ManagedMcpNameLabel", "Nome do servidor");
    public string ManagedMcpTransportLabel => GetLocalizedString("ManagedMcpTransportLabel", "Transporte");
    public string ManagedMcpCommandLabel => GetLocalizedString("ManagedMcpCommandLabel", "Comando");
    public string ManagedMcpArgsLabel => GetLocalizedString("ManagedMcpArgsLabel", "Argumentos, um por linha");
    public string ManagedMcpUrlLabel => GetLocalizedString("ManagedMcpUrlLabel", "URL");
    public string ManagedMcpStdioOption => GetLocalizedString("ManagedMcpStdioOption", "Comando (stdio)");
    public string ManagedMcpUrlOption => Get("ManagedMcpUrlOption");
    public string ManagedMcpAddStdioButton => GetLocalizedString("ManagedMcpAddStdioButton", "Adicionar stdio");
    public string ManagedMcpAddUrlButton => GetLocalizedString("ManagedMcpAddUrlButton", "Adicionar URL");
    public string ManagedMcpRemoveButton => GetLocalizedString("ManagedMcpRemoveButton", "Remover");
    public string ManagedMcpApplyButton => GetLocalizedString("ManagedMcpApplyButton", "Aplicar e atualizar");
    public string ManagedMcpHint => GetLocalizedString("ManagedMcpHint", "Somente entradas válidas e habilitadas são aplicadas. Use apenas letras, números, '-' ou '_' no nome.");
    public string SkillsTitle => Get("SkillsTitle");
    public string SkillsDescription => GetLocalizedString("SkillsDescription", "Crie e abra skills globais no mesmo lugar em que você configura a integração do Codex.");
    public string SkillsLearnMore => GetLocalizedString("SkillsLearnMore", "Dê superpoderes ao Codex.");
    public string InstalledSkillsTitle => GetLocalizedString("InstalledSkillsTitle", "Instaladas");
    public string RecommendedSkillsTitle => GetLocalizedString("RecommendedSkillsTitle", "Recomendadas");
    public string SearchSkillsPlaceholder => GetLocalizedString("SearchSkillsPlaceholder", "Buscar skills");
    public string InstallSkillButton => GetLocalizedString("InstallSkillButton", "Instalar");
    public string NoRecommendedSkills => GetLocalizedString("NoRecommendedSkills", "Nenhuma skill recomendada disponível.");
    public string IncludeIdeContextLabel => GetLocalizedString("IncludeIdeContextLabel", "Incluir contexto da IDE");
    public string SpeedLabel => GetLocalizedString("SpeedLabel", "Velocidade");
    public string AddPhotosFilesMenu => GetLocalizedString("AddPhotosFilesMenu", "Adicionar fotos e arquivos");
    public string McpShortcutsTitle => GetLocalizedString("McpShortcutsTitle", "Atalhos MCP");
    public string PermissionsTitle => GetLocalizedString("PermissionsTitle", "Permissões");
    public string DefaultPermissionsLabel => GetLocalizedString("DefaultPermissionsLabel", "Permissões padrão");
    public string ContextWindowTitle => GetLocalizedString("ContextWindowTitle", "Janela de contexto");
    public string ContinueInTitle => GetLocalizedString("ContinueInTitle", "Continuar em");
    public string LocalProjectLabel => GetLocalizedString("LocalProjectLabel", "Projeto local");
    public string RateLimitsTitle => GetLocalizedString("RateLimitsTitle", "Rate limits restantes");
    public string RateLimitsUnavailable => GetLocalizedString("RateLimitsUnavailable", "Rate limits indisponíveis.");
    public string PlanLabelShort => GetLocalizedString("PlanLabelShort", "Plano");
    public string PrimaryWindowLabel => GetLocalizedString("PrimaryWindowLabel", "Janela principal");
    public string SecondaryWindowLabel => GetLocalizedString("SecondaryWindowLabel", "Janela secundária");
    public string CreditsLabel => GetLocalizedString("CreditsLabel", "Créditos");
    public string RateLimitRemainingSuffix => GetLocalizedString("RateLimitRemainingSuffix", "restantes");
    public string RateLimitResetsPrefix => GetLocalizedString("RateLimitResetsPrefix", "reinicia");
    public string RateLimitUnlimitedLabel => GetLocalizedString("RateLimitUnlimitedLabel", "Ilimitado");
    public string RateLimitWeeklyLabel => GetLocalizedString("RateLimitWeeklyLabel", "Semanal");
    public string PreferredMcpTitle => GetLocalizedString("PreferredMcpTitle", "MCPs preferidos");
    public string SkillNameLabel => GetLocalizedString("SkillNameLabel", "Nome da skill");
    public string SkillDescriptionLabel => GetLocalizedString("SkillDescriptionLabel", "Descrição inicial");
    public string CreateSkillButton => GetLocalizedString("CreateSkillButton", "Criar skill");
    public string OpenSkillsFolderButton => GetLocalizedString("OpenSkillsFolderButton", "Abrir pasta de skills");
    public string OpenConfigButton => GetLocalizedString("OpenConfigButton", "Abrir config");
    public string SkillOpenButton => GetLocalizedString("SkillOpenButton", "Abrir");
    public string RefreshStatusButton => GetLocalizedString("RefreshStatusButton", "Atualizar");
    public string EnabledLabel => GetLocalizedString("EnabledLabel", "Habilitado");
    public string OpenPanelButton => GetLocalizedString("OpenPanelButton", "Abrir");
    public string NoSkillsDetected => GetLocalizedString("NoSkillsDetected", "Nenhuma skill detectada ainda.");
    public string NoManagedMcpServers => GetLocalizedString("NoManagedMcpServers", "Nenhum servidor MCP gerenciado foi configurado.");
    public string VerbosityLabel => Get("VerbosityLabel");
    public string ApprovalPolicyLabel => Get("ApprovalPolicyLabel");
    public string RawOutputLabel => Get("RawOutputLabel");
    public string InsertButton => Get("InsertButton");
    public string ComposerPlaceholder => Get("ComposerPlaceholder");
    public string AddAttachmentTooltip => Get("AddAttachmentTooltip");
    public string PasteImageTooltip => Get("PasteImageTooltip");
    public string SendTooltip => Get("SendTooltip");
    public string StopTooltip => GetLocalizedString("StopTooltip", "Parar resposta");
    public string StoppingTooltip => GetLocalizedString("StoppingTooltip", "Parando resposta");
    public string HistoryTooltip => Get("HistoryTooltip");
    public string SettingsTooltip => Get("SettingsTooltip");
    public string SetupCheckingTitle => GetLocalizedString("SetupCheckingTitle", "Verificando ambiente do Codex");
    public string SetupCheckingSummary => GetLocalizedString("SetupCheckingSummary", "Validando executável do Codex, autenticação e configuração local.");
    public string SetupMissingExecutableTitle => GetLocalizedString("SetupMissingExecutableTitle", "Runtime do Codex não encontrado");
    public string SetupMissingExecutableSummary => GetLocalizedString("SetupMissingExecutableSummary", "Nenhum executável compatível do Codex pôde ser resolvido a partir do caminho configurado ou do ambiente local.");
    public string SetupMissingAuthTitle => GetLocalizedString("SetupMissingAuthTitle", "Codex disponível, mas sem autenticação OpenAI");
    public string SetupMissingAuthSummary => GetLocalizedString("SetupMissingAuthSummary", "Faça login no Codex ou forneça um OPENAI_API_KEY antes de iniciar uma sessão.");
    public string SetupMissingProviderAuthTitle => GetLocalizedString("SetupMissingProviderAuthTitle", "Faltam credenciais do provider do Codex");
    public string SetupMissingProviderAuthSummary => GetLocalizedString("SetupMissingProviderAuthSummary", "O provider ativo está configurado no config.toml, mas as credenciais exigidas não estão disponíveis.");
    public string SetupReadyTitle => GetLocalizedString("SetupReadyTitle", "Codex pronto para uso");
    public string SetupReadySummary => GetLocalizedString("SetupReadySummary", "A extensão já consegue iniciar sessões usando o executável resolvido do Codex.");
    public string SetupErrorTitle => GetLocalizedString("SetupErrorTitle", "A configuração do Codex precisa de atenção");
    public string SetupErrorSummary => GetLocalizedString("SetupErrorSummary", "A extensão encontrou o Codex, mas não conseguiu validar o ambiente.");
    public string SetupInstallButton => GetLocalizedString("SetupInstallButton", "Copiar comando de instalação");
    public string SetupLoginButton => GetLocalizedString("SetupLoginButton", "Abrir login");
    public string SetupRefreshButton => GetLocalizedString("SetupRefreshButton", "Atualizar status");
    public string SetupSettingsButton => GetLocalizedString("SetupSettingsButton", "Abrir configurações");
    public string SetupInstallHint => GetLocalizedString("SetupInstallHint", "Comando de instalação");
    public string SetupExecutableHint => GetLocalizedString("SetupExecutableHint", "Executável");
    public string SetupAuthHint => GetLocalizedString("SetupAuthHint", "Autenticação");
    public string SetupVersionHint => GetLocalizedString("SetupVersionHint", "Versão");
    public string SetupAuthFileLabel => GetLocalizedString("SetupAuthFileLabel", "Usando ~/.codex/auth.json");
    public string SetupApiKeyLabel => GetLocalizedString("SetupApiKeyLabel", "Usando OPENAI_API_KEY");
    public string SetupManagedLoginLabel => GetLocalizedString("SetupManagedLoginLabel", "Usando login do Codex");
    public string SetupConfigProviderLabel => GetLocalizedString("SetupConfigProviderLabel", "Usando provider do config.toml");
    public string SetupConfigProfileLabelFormat => GetLocalizedString("SetupConfigProfileLabelFormat", "Usando perfil {0} do config.toml");
    public string SetupMissingAuthDetail => GetLocalizedString("SetupMissingAuthDetail", "Execute `codex login` ou configure uma API key da OpenAI.");
    public string SetupMissingProviderAuthDetail => GetLocalizedString("SetupMissingProviderAuthDetail", "Atualize o config.toml ou defina a variável de ambiente do provider antes de iniciar uma sessão.");
    public string SetupInstallDetail => GetLocalizedString("SetupInstallDetail", "Instalação recomendada: `npm install -g @openai/codex`");
    public string LocalButton => Get("LocalButton");
    public string RemoveAttachmentHoverLabel => Get("RemoveAttachmentHoverLabel");
    public string ApprovalCommandTitle => Get("ApprovalCommandTitle");
    public string ApprovalFileChangeTitle => Get("ApprovalFileChangeTitle");
    public string UserInputTitle => Get("UserInputTitle");
    public string ApprovalReasonLabel => Get("ApprovalReasonLabel");
    public string ApprovalCommandLabel => Get("ApprovalCommandLabel");
    public string ApprovalWorkingDirectoryLabel => Get("ApprovalWorkingDirectoryLabel");
    public string ApprovalGrantRootLabel => Get("ApprovalGrantRootLabel");
    public string ApprovalAccept => Get("ApprovalAccept");
    public string ApprovalAcceptForSession => Get("ApprovalAcceptForSession");
    public string ApprovalAcceptWithExecpolicyAmendment => Get("ApprovalAcceptWithExecpolicyAmendment");
    public string ApprovalApplyNetworkPolicyAmendment => Get("ApprovalApplyNetworkPolicyAmendment");
    public string ApprovalDecline => Get("ApprovalDecline");
    public string ApprovalCancel => Get("ApprovalCancel");
    public string AllFilesFilter => Get("AllFilesFilter");
    public string CodexNoResponse => Get("CodexNoResponse");
    public string ExecutionCanceled => Get("ExecutionCanceled");
    public string ExecutionError => Get("ExecutionError");
    public string ProcessingStatus => Get("ProcessingStatus");
    public string ImagePasteErrorPrefix => Get("ImagePasteErrorPrefix");
    public string LoadTopicsErrorPrefix => Get("LoadTopicsErrorPrefix");
    public string LoadModelsErrorPrefix => Get("LoadModelsErrorPrefix");
    public string DeleteHistoryTooltip => Get("DeleteHistoryTooltip");
    public string CopyButton => Get("CopyButton");
    public string SelectAllButton => Get("SelectAllButton");
    public string MarkdownTextModeLabel => GetLocalizedString("MarkdownTextModeLabel", "Texto");
    public string MarkdownRenderedModeLabel => GetLocalizedString("MarkdownRenderedModeLabel", "Renderizado");
    public string RunningStatus => Get("RunningStatus");
    public string ReadyStatus => Get("ReadyStatus");
    public string ContextWindowDetailFormat => Get("ContextWindowDetailFormat");
    public string IdeContextPrefix => Get("IdeContextPrefix");
    public string MermaidDiagramLabel => Get("MermaidDiagramLabel");
    public string MermaidCodeLabel => Get("MermaidCodeLabel");
    public string MermaidLoadingPreview => Get("MermaidLoadingPreview");
    public string MermaidInitFailed => Get("MermaidInitFailed");
    public string MermaidLoadFailedFormat => Get("MermaidLoadFailedFormat");
    public string MermaidRenderFailed => Get("MermaidRenderFailed");
    public string MermaidRenderFailedFormat => Get("MermaidRenderFailedFormat");
    public string MermaidFreezeFailed => Get("MermaidFreezeFailed");
    public string MermaidLoadTimeout => Get("MermaidLoadTimeout");
    public string MermaidPreviewFallback => Get("MermaidPreviewFallback");
    public string MermaidPreviewScriptError => Get("MermaidPreviewScriptError");
    public string ToolWindowErrorMessage => Get("ToolWindowErrorMessage");
    public string SettingsToolWindowErrorMessage => Get("SettingsToolWindowErrorMessage");
    public string OpenWindowFailedMessage => Get("OpenWindowFailedMessage");
    public string ExecutionCanceledTag => Get("ExecutionCanceledTag");
    public string ExecutionErrorTag => Get("ExecutionErrorTag");
    public string ExtensionContextPrefix => Get("ExtensionContextPrefix");
    public string PreferredMcpPrefix => Get("PreferredMcpPrefix");
    public string IdeContextSolutionLabel => Get("IdeContextSolutionLabel");
    public string IdeContextActiveDocumentLabel => Get("IdeContextActiveDocumentLabel");
    public string IdeContextSelectedItemsLabel => Get("IdeContextSelectedItemsLabel");
    public string IdeContextOpenFilesLabel => Get("IdeContextOpenFilesLabel");
    public string IdeContextSelectionLabel => Get("IdeContextSelectionLabel");
    public string InvalidSkillNameMessage => Get("InvalidSkillNameMessage");
    public string SkillTemplateSummary => Get("SkillTemplateSummary");
    public string SkillTemplateWhenToUseHeading => Get("SkillTemplateWhenToUseHeading");
    public string SkillTemplateWhenToUseBullet => Get("SkillTemplateWhenToUseBullet");
    public string SkillTemplateFlowHeading => Get("SkillTemplateFlowHeading");
    public string SkillTemplateFlowStep1 => Get("SkillTemplateFlowStep1");
    public string SkillTemplateFlowStep2 => Get("SkillTemplateFlowStep2");
    public string SkillTemplateFlowStep3 => Get("SkillTemplateFlowStep3");
    public string CodexDetectedLabel => Get("CodexDetectedLabel");
    public string EventPlanTitle => Get("EventPlanTitle");
    public string EventPlanUpdated => Get("EventPlanUpdated");
    public string EventReasoningTitle => Get("EventReasoningTitle");
    public string EventReasoningUpdated => Get("EventReasoningUpdated");
    public string EventCommandTitle => Get("EventCommandTitle");
    public string EventWorkingDirectoryLabel => Get("EventWorkingDirectoryLabel");
    public string EventOutputLabel => Get("EventOutputLabel");
    public string EventFileChangesTitle => Get("EventFileChangesTitle");
    public string EventUpdatedFiles => Get("EventUpdatedFiles");
    public string EventFileUpdated => Get("EventFileUpdated");
    public string EventMcpToolTitle => Get("EventMcpToolTitle");
    public string EventArgumentsLabel => Get("EventArgumentsLabel");
    public string EventErrorLabel => Get("EventErrorLabel");
    public string EventResultLabel => Get("EventResultLabel");
    public string EventToolTitle => Get("EventToolTitle");
    public string EventAgentToolTitle => Get("EventAgentToolTitle");
    public string EventAgentToolUsed => Get("EventAgentToolUsed");
    public string EventPromptLabel => Get("EventPromptLabel");
    public string EventWebSearchTitle => Get("EventWebSearchTitle");
    public string EventImageViewTitle => Get("EventImageViewTitle");
    public string EventImageViewed => Get("EventImageViewed");
    public string EventImageGenerationTitle => Get("EventImageGenerationTitle");
    public string EventImageGenerated => Get("EventImageGenerated");
    public string EventReviewModeTitle => Get("EventReviewModeTitle");
    public string EventEnteredReviewMode => Get("EventEnteredReviewMode");
    public string EventExitedReviewMode => Get("EventExitedReviewMode");
    public string EventContextTitle => Get("EventContextTitle");
    public string EventConversationContextCompacted => Get("EventConversationContextCompacted");
    public string EventToolCall => Get("EventToolCall");
    public string EventCommandExecuted => Get("EventCommandExecuted");
    public string EventMoreFormat => Get("EventMoreFormat");
    public string EventMoreFilesFormat => Get("EventMoreFilesFormat");
    public string EventPendingStatus => GetLocalizedString("EventPendingStatus", "pendente");
    public string EventInProgressStatus => GetLocalizedString("EventInProgressStatus", "em andamento");
    public string EventCompletedStatus => Get("EventCompletedStatus");
    public string EventFailedStatus => Get("EventFailedStatus");
    public string ToolWindowXamlLoadLogMessage => Get("ToolWindowXamlLoadLogMessage");
    public string ToolWindowViewModelCreateLogMessage => Get("ToolWindowViewModelCreateLogMessage");
    public string SettingsToolWindowXamlLoadLogMessage => Get("SettingsToolWindowXamlLoadLogMessage");
    public string SettingsToolWindowViewModelCreateLogMessage => Get("SettingsToolWindowViewModelCreateLogMessage");
    public string ToolWindowInitializeLogMessage => Get("ToolWindowInitializeLogMessage");
    public string SettingsToolWindowInitializeLogMessage => Get("SettingsToolWindowInitializeLogMessage");
    public string SettingsToolWindowOpenLogMessage => Get("SettingsToolWindowOpenLogMessage");
    public string ToolWindowOpenLogMessage => Get("ToolWindowOpenLogMessage");
    public string AsyncPanelInitializeLogMessage => Get("AsyncPanelInitializeLogMessage");
    public string StartTurnFailedMessage => Get("StartTurnFailedMessage");
    public string AppServerValidationFailed => Get("AppServerValidationFailed");
    public string AppServerUnsupported => Get("AppServerUnsupported");
    public string AppServerClosedUnexpectedly => Get("AppServerClosedUnexpectedly");
    public string AppServerRequestFailed => Get("AppServerRequestFailed");
    public string AppServerUnavailable => Get("AppServerUnavailable");
    public string EventCommentaryTitle => Get("EventCommentaryTitle");
    public string EventMcpProgressTitle => Get("EventMcpProgressTitle");
    public string OutputTagSetup => Get("OutputTagSetup");
    public string OutputTagAuth => Get("OutputTagAuth");
    public string OutputTagSkills => Get("OutputTagSkills");
    public string OutputTagRemoteSkills => Get("OutputTagRemoteSkills");
    public string OutputTagInit => Get("OutputTagInit");
    public string OutputTagServer => Get("OutputTagServer");
    public string OutputTagStderr => Get("OutputTagStderr");
    public string OutputTagAppServer => Get("OutputTagAppServer");
    public string OutputTagApproval => Get("OutputTagApproval");
    public string OutputTagUserInput => Get("OutputTagUserInput");
    public string ExitCodeLabel => Get("ExitCodeLabel");
    public string ManagedMcpDefaultName => Get("ManagedMcpDefaultName");
    public string ManagedMcpDefaultUrlName => Get("ManagedMcpDefaultUrlName");
    public string MermaidBundleNotFoundFormat => Get("MermaidBundleNotFoundFormat");

    public SelectionOption[] CreateReasoningOptions()
    {
        return new[]
        {
            new SelectionOption(Get("ReasoningMinimal"), "minimal"),
            new SelectionOption(Get("ReasoningLow"), "low"),
            new SelectionOption(Get("ReasoningMedium"), "medium"),
            new SelectionOption(Get("ReasoningHigh"), "high"),
            new SelectionOption(Get("ReasoningMax"), "xhigh")
        };
    }

    public SelectionOption[] CreateVerbosityOptions()
    {
        return new[]
        {
            new SelectionOption(Get("ReasoningLow"), "low"),
            new SelectionOption(Get("ReasoningMedium"), "medium"),
            new SelectionOption(Get("ReasoningHigh"), "high")
        };
    }

    public SelectionOption[] CreateServiceTierOptions()
    {
        return new[]
        {
            new SelectionOption(GetLocalizedString("SpeedDefault", "Padrão"), string.Empty),
            new SelectionOption(GetLocalizedString("SpeedFast", "Rápida"), "fast"),
            new SelectionOption(GetLocalizedString("SpeedFlex", "Flex"), "flex")
        };
    }

    public SelectionOption[] CreateApprovalPolicyOptions()
    {
        return new[]
        {
            new SelectionOption(Get("ApprovalDefault"), string.Empty),
            new SelectionOption(Get("ApprovalRequest"), "on-request"),
            new SelectionOption(Get("ApprovalFailure"), "on-failure"),
            new SelectionOption(Get("ApprovalNever"), "never"),
            new SelectionOption(Get("ApprovalUntrusted"), "untrusted")
        };
    }

    public SelectionOption[] CreateSandboxModeOptions()
    {
        return new[]
        {
            new SelectionOption(Get("SandboxReadOnly"), "read-only"),
            new SelectionOption(Get("SandboxWorkspace"), "workspace-write"),
            new SelectionOption(Get("SandboxFullAccess"), "danger-full-access")
        };
    }

    public SelectionOption[] CreateLanguageOptions()
    {
        return new[]
        {
            new SelectionOption(LanguageAutoOption, string.Empty),
            new SelectionOption("Português (Brasil)", "pt-BR"),
            new SelectionOption("English", "en"),
            new SelectionOption("Español", "es"),
            new SelectionOption("Français", "fr"),
            new SelectionOption("Deutsch", "de")
        };
    }

    public string GetApprovalOptionLabel(string key)
    {
        switch (key)
        {
            case "accept":
                return ApprovalAccept;
            case "acceptForSession":
                return ApprovalAcceptForSession;
            case "acceptWithExecpolicyAmendment":
                return ApprovalAcceptWithExecpolicyAmendment;
            case "applyNetworkPolicyAmendment":
                return ApprovalApplyNetworkPolicyAmendment;
            case "cancel":
                return ApprovalCancel;
            default:
                return ApprovalDecline;
        }
    }

    private string Get(string key)
    {
        string value;
        return _strings.TryGetValue(key, out value) ? value : EnglishStrings[key];
    }

    private string GetLocalizedString(string key, string portugueseValue)
    {
        return Culture.TwoLetterISOLanguageName == "pt"
            ? portugueseValue
            : Get(key);
    }

    private static CultureInfo ResolvePreferredCulture(string? languageOverride)
    {
        if (!string.IsNullOrWhiteSpace(languageOverride))
        {
            try
            {
                return CultureInfo.GetCultureInfo(languageOverride.Trim());
            }
            catch (CultureNotFoundException)
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name))
        {
            return CultureInfo.CurrentUICulture;
        }

        if (!string.IsNullOrWhiteSpace(CultureInfo.CurrentCulture.Name))
        {
            return CultureInfo.CurrentCulture;
        }

        return CultureInfo.InstalledUICulture;
    }

    private static CultureInfo ResolveSupportedCulture(CultureInfo culture)
    {
        switch (culture.TwoLetterISOLanguageName)
        {
            case "pt":
                return new CultureInfo("pt-BR");
            case "es":
                return new CultureInfo("es");
            case "fr":
                return new CultureInfo("fr");
            case "de":
                return new CultureInfo("de");
            default:
                return new CultureInfo("en");
        }
    }

    private static IReadOnlyDictionary<string, string> GetLanguageStrings(CultureInfo culture)
    {
        switch (culture.TwoLetterISOLanguageName)
        {
            case "pt":
                return PortugueseStrings;
            case "es":
                return SpanishStrings;
            case "fr":
                return FrenchStrings;
            case "de":
                return GermanStrings;
            default:
                return EnglishStrings;
        }
    }
}
