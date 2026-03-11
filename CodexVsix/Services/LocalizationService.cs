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
        ["UseButton"] = "Use",
        ["NewTopicButton"] = "New topic",
        ["RenameTopicButton"] = "Rename",
        ["RenameTopicPlaceholder"] = "Rename selected topic",
        ["PlanModeLabel"] = "Plan mode",
        ["QuestionModeLabel"] = "Question mode",
        ["AgentModeLabel"] = "Agent mode",
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
        ["SkillNameLabel"] = "Skill name",
        ["SkillDescriptionLabel"] = "Initial description",
        ["CreateSkillButton"] = "Create skill",
        ["OpenSkillsFolderButton"] = "Open skills folder",
        ["OpenConfigButton"] = "Open config",
        ["SkillOpenButton"] = "Open",
        ["RefreshStatusButton"] = "Refresh",
        ["EnabledLabel"] = "Enabled",
        ["NoSkillsDetected"] = "No skills detected yet.",
        ["NoManagedMcpServers"] = "No managed MCP server configured.",
        ["VerbosityLabel"] = "Verbosity",
        ["ApprovalPolicyLabel"] = "Approval policy",
        ["RawOutputLabel"] = "Raw output",
        ["InsertButton"] = "Insert",
        ["ComposerPlaceholder"] = "Ask for additional changes",
        ["AddAttachmentTooltip"] = "Attach image or file",
        ["PasteImageTooltip"] = "Paste image from clipboard",
        ["SendTooltip"] = "Send prompt",
        ["HistoryTooltip"] = "Open topics",
        ["SettingsTooltip"] = "Open settings",
        ["SetupCheckingTitle"] = "Checking Codex environment",
        ["SetupCheckingSummary"] = "Validating executable, authentication, and local configuration.",
        ["SetupMissingExecutableTitle"] = "Codex CLI not found",
        ["SetupMissingExecutableSummary"] = "Install Codex CLI and confirm the executable path before using the extension.",
        ["SetupMissingAuthTitle"] = "Codex found, but login is missing",
        ["SetupMissingAuthSummary"] = "Authenticate with Codex CLI or provide an OPENAI_API_KEY before starting a session.",
        ["SetupReadyTitle"] = "Codex is ready",
        ["SetupReadySummary"] = "The extension can start sessions with the local Codex CLI.",
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
        ["SetupMissingAuthDetail"] = "Run `codex login` in a terminal or configure an API key.",
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
        ["ReasoningMinimal"] = "Minimal"
    };

    private static readonly IReadOnlyDictionary<string, string> PortugueseStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Tópicos",
        ["HistoryTitle"] = "Histórico",
        ["UseButton"] = "Usar",
        ["NewTopicButton"] = "Novo tópico",
        ["RenameTopicButton"] = "Renomear",
        ["RenameTopicPlaceholder"] = "Renomear tópico selecionado",
        ["PlanModeLabel"] = "Modo planejamento",
        ["QuestionModeLabel"] = "Modo pergunta",
        ["AgentModeLabel"] = "Modo agente",
        ["AppsTitle"] = "Apps",
        ["McpServersTitle"] = "Servidores MCP",
        ["SettingsTitle"] = "Configuração e detalhes",
        ["ExecutableLabel"] = "Executável",
        ["WorkingDirectoryLabel"] = "Diretório de trabalho",
        ["VerbosityLabel"] = "Verbosidade",
        ["ApprovalPolicyLabel"] = "Política de aprovação",
        ["RawOutputLabel"] = "Saída bruta",
        ["InsertButton"] = "Inserir",
        ["ComposerPlaceholder"] = "Pedir alterações adicionais",
        ["AddAttachmentTooltip"] = "Anexar imagem ou arquivo",
        ["PasteImageTooltip"] = "Colar imagem da área de transferência",
        ["SendTooltip"] = "Enviar prompt",
        ["HistoryTooltip"] = "Abrir tópicos",
        ["SettingsTooltip"] = "Abrir configurações",
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
        ["ReasoningMinimal"] = "Mínima"
    };

    private static readonly IReadOnlyDictionary<string, string> SpanishStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Temas",
        ["HistoryTitle"] = "Historial",
        ["UseButton"] = "Usar",
        ["NewTopicButton"] = "Nuevo tema",
        ["RenameTopicButton"] = "Renombrar",
        ["RenameTopicPlaceholder"] = "Renombrar tema seleccionado",
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
        ["ComposerPlaceholder"] = "Pedir cambios adicionales",
        ["AddAttachmentTooltip"] = "Adjuntar imagen o archivo",
        ["PasteImageTooltip"] = "Pegar imagen del portapapeles",
        ["SendTooltip"] = "Enviar prompt",
        ["HistoryTooltip"] = "Abrir temas",
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
        ["ReasoningMinimal"] = "Mínima"
    };

    private static readonly IReadOnlyDictionary<string, string> FrenchStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Sujets",
        ["HistoryTitle"] = "Historique",
        ["UseButton"] = "Utiliser",
        ["NewTopicButton"] = "Nouveau sujet",
        ["RenameTopicButton"] = "Renommer",
        ["RenameTopicPlaceholder"] = "Renommer le sujet sélectionné",
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
        ["ComposerPlaceholder"] = "Demander des modifications supplémentaires",
        ["AddAttachmentTooltip"] = "Joindre une image ou un fichier",
        ["PasteImageTooltip"] = "Coller l'image du presse-papiers",
        ["SendTooltip"] = "Envoyer le prompt",
        ["HistoryTooltip"] = "Ouvrir les sujets",
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
        ["ReasoningMinimal"] = "Minimale"
    };

    private static readonly IReadOnlyDictionary<string, string> GermanStrings = new Dictionary<string, string>
    {
        ["TopicsTitle"] = "Themen",
        ["HistoryTitle"] = "Verlauf",
        ["UseButton"] = "Verwenden",
        ["NewTopicButton"] = "Neues Thema",
        ["RenameTopicButton"] = "Umbenennen",
        ["RenameTopicPlaceholder"] = "Ausgewähltes Thema umbenennen",
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
        ["ComposerPlaceholder"] = "Zusätzliche Änderungen anfordern",
        ["AddAttachmentTooltip"] = "Bild oder Datei anhängen",
        ["PasteImageTooltip"] = "Bild aus der Zwischenablage einfügen",
        ["SendTooltip"] = "Prompt senden",
        ["HistoryTooltip"] = "Themen öffnen",
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
        ["ReasoningMinimal"] = "Minimal"
    };

    private readonly IReadOnlyDictionary<string, string> _strings;

    public LocalizationService()
    {
        var preferredCulture = !string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name)
            ? CultureInfo.CurrentUICulture
            : !string.IsNullOrWhiteSpace(CultureInfo.CurrentCulture.Name)
                ? CultureInfo.CurrentCulture
                : CultureInfo.InstalledUICulture;

        Culture = ResolveSupportedCulture(preferredCulture);
        LanguageTag = Culture.Name;
        _strings = GetLanguageStrings(Culture);
    }

    public CultureInfo Culture { get; }

    public string LanguageTag { get; }

    public string TopicsTitle => Get("TopicsTitle");
    public string HistoryTitle => Get("HistoryTitle");
    public string UseButton => Get("UseButton");
    public string NewTopicButton => Get("NewTopicButton");
    public string RenameTopicButton => Get("RenameTopicButton");
    public string RenameTopicPlaceholder => Get("RenameTopicPlaceholder");
    public string PlanModeLabel => Get("PlanModeLabel");
    public string QuestionModeLabel => Get("QuestionModeLabel");
    public string AgentModeLabel => Get("AgentModeLabel");
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
    public string SkillNameLabel => GetLocalizedString("SkillNameLabel", "Nome da skill");
    public string SkillDescriptionLabel => GetLocalizedString("SkillDescriptionLabel", "Descrição inicial");
    public string CreateSkillButton => GetLocalizedString("CreateSkillButton", "Criar skill");
    public string OpenSkillsFolderButton => GetLocalizedString("OpenSkillsFolderButton", "Abrir pasta de skills");
    public string OpenConfigButton => GetLocalizedString("OpenConfigButton", "Abrir config");
    public string SkillOpenButton => GetLocalizedString("SkillOpenButton", "Abrir");
    public string RefreshStatusButton => GetLocalizedString("RefreshStatusButton", "Atualizar");
    public string EnabledLabel => GetLocalizedString("EnabledLabel", "Habilitado");
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
    public string HistoryTooltip => Get("HistoryTooltip");
    public string SettingsTooltip => Get("SettingsTooltip");
    public string SetupCheckingTitle => GetLocalizedString("SetupCheckingTitle", "Verificando ambiente do Codex");
    public string SetupCheckingSummary => GetLocalizedString("SetupCheckingSummary", "Validando executável, autenticação e configuração local.");
    public string SetupMissingExecutableTitle => GetLocalizedString("SetupMissingExecutableTitle", "Codex CLI não encontrado");
    public string SetupMissingExecutableSummary => GetLocalizedString("SetupMissingExecutableSummary", "Instale o Codex CLI e confirme o caminho do executável antes de usar a extensão.");
    public string SetupMissingAuthTitle => GetLocalizedString("SetupMissingAuthTitle", "Codex encontrado, mas sem login");
    public string SetupMissingAuthSummary => GetLocalizedString("SetupMissingAuthSummary", "Faça login no Codex CLI ou forneça um OPENAI_API_KEY antes de iniciar uma sessão.");
    public string SetupReadyTitle => GetLocalizedString("SetupReadyTitle", "Codex pronto para uso");
    public string SetupReadySummary => GetLocalizedString("SetupReadySummary", "A extensão já consegue iniciar sessões usando o Codex CLI local.");
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
    public string SetupMissingAuthDetail => GetLocalizedString("SetupMissingAuthDetail", "Execute `codex login` em um terminal ou configure uma API key.");
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
