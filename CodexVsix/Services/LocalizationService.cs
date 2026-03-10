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
        ["SettingsTitle"] = "Configuration and details",
        ["ExecutableLabel"] = "Executable",
        ["WorkingDirectoryLabel"] = "Working directory",
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
    public string SettingsTitle => Get("SettingsTitle");
    public string ExecutableLabel => Get("ExecutableLabel");
    public string WorkingDirectoryLabel => Get("WorkingDirectoryLabel");
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
