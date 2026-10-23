using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

public sealed class CodexProcessService : IDisposable
{
    internal const string IdeContextHeading = "# Context from my IDE setup:";
    internal const string PromptRequestBegin = "## My request for Codex:";
    private const int MaxSessionLinesToParse = 1200;
    private const int MaxInitialThreadTurnsToLoad = 120;
    private const int MaxPromptHistoryPromptsPerThread = 120;
    private const int MaxPromptHistoryFallbackEntryLength = 12000;
    private const int MaxSummarySourceLength = 4000;

    private static readonly Regex MentionRegex = new("(?<!\\S)@(?:\\\"(?<quoted>[^\\\"]+)\\\"|(?<value>\\S+))", RegexOptions.Compiled);
    private static readonly Regex SkillRegex = new(@"(?<!\S)\$(?<value>[A-Za-z0-9][A-Za-z0-9._-]*)", RegexOptions.Compiled);
    private static readonly Regex RateLimitDurationRegex = new(@"(?<value>\d+(?:\.\d+)?)\s*(?<unit>weeks?|w|days?|d|hours?|hrs?|h|minutes?|mins?|m)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingPromptAfterPathRegex = new(@"(?:\.[A-Za-z0-9]{1,8})(?<prompt>[\p{L}\p{N}@#\$""'(\[].*)$", RegexOptions.Compiled);
    private static readonly string[] ExtensionContextPrefixes = CreateLocalizedSet(localization => localization.ExtensionContextPrefix);
    private static readonly string[] PreferredMcpPrefixes = CreateLocalizedSet(localization => localization.PreferredMcpPrefix);
    private static readonly string[] IdeContextPrefixes = CreateLocalizedSet(localization => localization.IdeContextPrefix);
    private static readonly string[] SyntheticUserContextPrefixes =
    {
        IdeContextHeading,
        "# AGENTS.md instructions",
        "<environment_context>",
        "<permissions instructions>",
        "<apps_instructions>",
        "<skills_instructions>",
        "<plugins_instructions>",
        "<collaboration_mode>",
        "<turn_aborted>"
    };

    private static readonly string[] IdeContextLinePrefixes = CreateLocalizedSet(
        localization => localization.IdeContextSolutionLabel,
        localization => localization.IdeContextActiveDocumentLabel,
        localization => localization.IdeContextSelectedItemsLabel,
        localization => localization.IdeContextOpenFilesLabel,
        localization => localization.IdeContextSelectionLabel);

    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly object _writeLock = new();
    private readonly Dictionary<long, PendingRequest> _pendingRequests = new();
    private readonly Dictionary<string, string> _skillsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly CodexDiagnosticLogger _diagnostics;

    private Process? _serverProcess;
    private StreamWriter? _serverInput;
    private TaskCompletionSource<bool>? _initializedTcs;
    private ActiveTurnState? _activeTurn;
    private string? _threadId;
    private string? _threadConfigKey;
    private string? _serverConfigKey;
    private string? _skillsCacheKey;
    private string? _languageOverride;
    private bool _threadLoaded;
    private long _nextRequestId;
    private long _serverGeneration;
    private string _lastServerError = string.Empty;

    public Func<CodexApprovalRequest, Task<JToken?>>? ApprovalRequestHandler { get; set; }
    public Func<CodexUserInputRequest, Task<JObject?>>? UserInputRequestHandler { get; set; }
    internal Func<JObject, Task<JObject?>>? AppServerRequestHandler { get; set; }
    internal Func<CodexExtensionSettings, bool, CancellationToken, Task<bool>>? MissingExecutableHandler { get; set; }
    public event Action<string, JToken?>? AppServerNotificationReceived;
    public event Action? ThreadCatalogChanged;
    public event Action<CodexRateLimitSummary>? RateLimitsUpdated;
    public event Action? AccountUpdated;

    public CodexProcessService()
        : this(CodexDiagnosticLogger.Shared)
    {
    }

    internal CodexProcessService(CodexDiagnosticLogger diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public string? CurrentThreadId
    {
        get
        {
            lock (_syncRoot)
            {
                return _threadId;
            }
        }
    }

    internal async Task<JToken?> InvokeAppServerRequestAsync(
        CodexExtensionSettings settings,
        string method,
        JToken? parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("An app-server method is required.", nameof(method));
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        if (string.Equals(method, "account/read", StringComparison.Ordinal)
            && MissingExecutableHandler is not null
            && await IsExecutableMissingAsync(settings, cancellationToken).ConfigureAwait(false))
        {
            // Lets the webview render its sign-in page; the install prompt fires when the user starts sign-in.
            return new JObject { ["account"] = JValue.CreateNull(), ["requiresOpenaiAuth"] = true };
        }

        if (string.Equals(method, "account/login/start", StringComparison.Ordinal))
        {
            await EnsureExecutableAvailableAsync(settings, userInitiated: true, cancellationToken).ConfigureAwait(false);
        }

        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (string.Equals(method, "account/read", StringComparison.Ordinal))
        {
            return await CodexProviderAuthResolver.ReadAccountAsync(
                (requestMethod, values, token) => SendRequestAsync(requestMethod, values, token),
                parameters, settings.Profile, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(method, "model/list", StringComparison.Ordinal))
        {
            return await CodexModelCatalog.ReadAllPagesAsync(
                (values, token) => SendRequestAsync("model/list", values, token),
                parameters, cancellationToken).ConfigureAwait(false);
        }

        var inputDirectory = (parameters as JObject)?["cwd"]?.Value<string>();
        parameters = CodexImageInputInliner.InlineImages(method, parameters,
            string.IsNullOrWhiteSpace(inputDirectory) ? workingDirectory : ResolveWorkingDirectory(inputDirectory!));
        return await SendRequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "The completion source uses RunContinuationsAsynchronously and never depends on the Visual Studio UI context.")]
    public async Task<int> ExecuteAsync(
        string prompt,
        CodexExtensionSettings settings,
        IEnumerable<string> imagePaths,
        string ideContextSummary,
        Action<string> onOutput,
        Action<string> onError,
        Action<ChatMessage>? onEventMessage,
        Action<long, long?>? onTokenUsage,
        CancellationToken cancellationToken)
    {
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
            await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);
            await EnsureThreadReadyAsync(settings, workingDirectory, settings.CurrentThreadId, cancellationToken).ConfigureAwait(false);
            await RefreshSkillsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            var turnState = new ActiveTurnState(onOutput, onError, onEventMessage, onTokenUsage);
            lock (_syncRoot)
            {
                _activeTurn = turnState;
            }

            using (cancellationToken.Register(() => _ = InterruptActiveTurnAsync()))
            {
                try
                {
                    var turnResult = await StartTurnOrReviewAsync(
                        turnState,
                        _threadId!,
                        prompt,
                        settings,
                        workingDirectory,
                        imagePaths,
                        ideContextSummary,
                        cancellationToken).ConfigureAwait(false);

                    turnState.TurnId = turnResult?["turn"]?["id"]?.Value<string>();
                    var exitCode = await turnState.Completion.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return exitCode;
                }
                finally
                {
                    lock (_syncRoot)
                    {
                        if (ReferenceEquals(_activeTurn, turnState))
                        {
                            _activeTurn = null;
                        }
                    }
                }
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _activeTurn?.TrySetResult(1);
            _activeTurn = null;
        }

        RestartServer(clearConfig: true);
        _executionGate.Dispose();
        _lifecycleGate.Dispose();
    }

    public void ResetThread()
    {
        lock (_syncRoot)
        {
            _threadId = null;
            _threadConfigKey = null;
            _threadLoaded = false;
        }
    }

    public void CancelActiveTurn()
    {
        _ = CancelActiveTurnAsync();
    }

    public async Task CancelActiveTurnAsync()
    {
        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
        }

        if (turnState is null)
        {
            return;
        }

        var interrupted = await InterruptActiveTurnAsync().ConfigureAwait(false);
        if (!interrupted)
        {
            RestartServer(clearConfig: false);
        }

        turnState.TrySetResult(1);
    }

    public async Task<bool> SteerActiveTurnAsync(
        string prompt,
        CodexExtensionSettings settings,
        IEnumerable<string> imagePaths,
        string ideContextSummary,
        CancellationToken cancellationToken)
    {
        ActiveTurnState? turnState;
        string? threadId;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
            threadId = _threadId;
        }

        if (turnState is null
            || string.IsNullOrWhiteSpace(turnState.TurnId)
            || string.IsNullOrWhiteSpace(threadId))
        {
            return false;
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        var response = await SendRequestAsync(
            "turn/steer",
            new
            {
                threadId,
                expectedTurnId = turnState.TurnId,
                input = BuildUserInput(prompt, settings, workingDirectory, imagePaths, ideContextSummary)
            },
            cancellationToken).ConfigureAwait(false);

        var returnedTurnId = response?["turnId"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(returnedTurnId))
        {
            turnState.TurnId = returnedTurnId;
        }

        return true;
    }

    public async Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var currentThreadId = CurrentThreadId ?? settings.CurrentThreadId;
        var items = await RequestThreadListItemsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        var threads = new List<CodexThreadSummary>();
        if (items is null)
        {
            return threads;
        }

        foreach (var item in items)
        {
            var summary = ParseThreadSummary(item, currentThreadId);
            if (summary is not null)
            {
                threads.Add(summary);
            }
        }

        return threads;
    }

    private async Task<JArray?> RequestThreadListItemsAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        foreach (var candidate in GetThreadListWorkingDirectoryCandidates(workingDirectory))
        {
            var items = await SendThreadListRequestAsync(candidate, 200, cancellationToken).ConfigureAwait(false);
            if (items is { Count: > 0 })
            {
                return items;
            }
        }

        var allItems = await SendThreadListRequestAsync(null, 500, cancellationToken).ConfigureAwait(false);
        if (allItems is null || allItems.Count == 0)
        {
            return allItems;
        }

        var filteredItems = new JArray();
        foreach (var item in allItems)
        {
            if (ThreadMatchesWorkingDirectory(item, workingDirectory))
            {
                filteredItems.Add(item.DeepClone());
            }
        }

        return filteredItems;
    }

    private async Task<JArray?> SendThreadListRequestAsync(string? workingDirectory, int limit, CancellationToken cancellationToken)
    {
        var parameters = new JObject
        {
            ["limit"] = limit,
            ["archived"] = false,
            ["sortKey"] = "updated_at"
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            parameters["cwd"] = workingDirectory;
        }

        var response = await SendRequestAsync("thread/list", parameters, cancellationToken).ConfigureAwait(false);
        return response?["data"] as JArray;
    }

    private static IEnumerable<string> GetThreadListWorkingDirectoryCandidates(string workingDirectory)
    {
        var normalized = NormalizeComparablePath(workingDirectory);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        yield return normalized;

        var devicePath = ToWindowsDevicePath(normalized);
        if (!string.IsNullOrWhiteSpace(devicePath) && !string.Equals(devicePath, normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return devicePath;
        }
    }

    private static bool ThreadMatchesWorkingDirectory(JToken item, string workingDirectory)
    {
        var threadWorkingDirectory = NormalizeComparablePath(item?["cwd"]?.Value<string>());
        var normalizedWorkingDirectory = NormalizeComparablePath(workingDirectory);
        return !string.IsNullOrWhiteSpace(threadWorkingDirectory)
            && !string.IsNullOrWhiteSpace(normalizedWorkingDirectory)
            && string.Equals(threadWorkingDirectory, normalizedWorkingDirectory, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CodexThreadConversation?> LoadThreadConversationAsync(CodexExtensionSettings settings, string threadId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var resumed = await ResumeThreadForConversationLoadAsync(settings, threadId, workingDirectory, cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            _threadId = resumed?["thread"]?["id"]?.Value<string>() ?? threadId;
            _threadConfigKey = BuildThreadConfigKey(settings, workingDirectory);
            _threadLoaded = true;
        }

        var thread = BuildThreadWithInitialTurnsPage(resumed?["thread"], resumed?["initialTurnsPage"]);
        if (thread is null)
        {
            return null;
        }

        var summary = ParseThreadSummary(thread, threadId) ?? new CodexThreadSummary { ThreadId = threadId };
        var sessionPath = thread["path"]?.Value<string>() ?? FindSessionPathForThread(summary.ThreadId);
        return new CodexThreadConversation
        {
            Thread = summary,
            Messages = ParseThreadMessages(thread, summary.ThreadId, sessionPath)
        };
    }

    private async Task<JToken?> ResumeThreadForConversationLoadAsync(
        CodexExtensionSettings settings,
        string threadId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendRequestAsync(
                "thread/resume",
                BuildThreadResumeParams(threadId, settings, workingDirectory, includeInitialTurnsPage: true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CodexAppServerException ex) when (ex.IsCompatibilityError)
        {
            return await SendRequestAsync(
                "thread/resume",
                BuildThreadResumeParams(threadId, settings, workingDirectory),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<CodexModelOption>> ListModelsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken, bool includeHidden = false)
    {
        var response = await InvokeAppServerRequestAsync(settings, "model/list",
            new JObject { ["includeHidden"] = includeHidden }, cancellationToken).ConfigureAwait(false);
        return CodexModelCatalog.ParseModelListResponse(response, includeHidden);
    }

    public async Task RenameThreadAsync(CodexExtensionSettings settings, string threadId, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);
        await SendRequestAsync(
            "thread/name/set",
            new
            {
                threadId,
                name = name.Trim()
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ArchiveThreadAsync(CodexExtensionSettings settings, string threadId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);
        await SendRequestAsync(
            "thread/archive",
            new
            {
                threadId
            },
            cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            if (string.Equals(_threadId, threadId, StringComparison.Ordinal))
            {
                _threadId = null;
                _threadConfigKey = null;
                _threadLoaded = false;
            }
        }
    }

    public async Task<IReadOnlyList<CodexAppSummary>> ListAppsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync(
            "app/list",
            new
            {
                limit = 20,
                forceRefetch = false,
                threadId = CurrentThreadId
            },
            cancellationToken).ConfigureAwait(false);

        var apps = new List<CodexAppSummary>();
        var items = response?["data"] as JArray;
        if (items is null)
        {
            return apps;
        }

        foreach (var item in items)
        {
            var name = item?["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = item?["description"]?.Value<string>();
            apps.Add(new CodexAppSummary
            {
                Name = name!,
                Description = description ?? string.Empty
            });
        }

        return apps;
    }

    public async Task<IReadOnlyList<CodexMcpServerSummary>> ListMcpServersAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync(
            "mcpServerStatus/list",
            new
            {
                limit = 20
            },
            cancellationToken).ConfigureAwait(false);

        var servers = new List<CodexMcpServerSummary>();
        var items = response?["data"] as JArray;
        if (items is null)
        {
            return servers;
        }

        foreach (var item in items)
        {
            var name = item?["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var tools = item?["tools"]?.Children<JProperty>().Select(property => property.Name).Take(4).ToArray() ?? new string[0];
            var toolsLabel = tools.Length == 0 ? string.Empty : string.Join(", ", tools);
            servers.Add(new CodexMcpServerSummary
            {
                Name = name!,
                AuthStatus = item?["authStatus"]?.Value<string>() ?? string.Empty,
                ToolsLabel = toolsLabel
            });
        }

        return servers;
    }

    public async Task<IReadOnlyList<CodexSkillSummary>> ListSkillsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken, bool forceReload = false)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync(
            "skills/list",
            new { cwds = new[] { workingDirectory }, forceReload },
            cancellationToken).ConfigureAwait(false);

        var homeSkillsDirectory = Path.Combine(
            CodexEnvironmentPathHelper.GetCodexHomeDirectory(settings.EnvironmentVariables),
            "skills");
        var localization = new LocalizationService(settings.LanguageOverride);

        var summaries = new List<CodexSkillSummary>();
        var entries = response?["data"] as JArray;
        if (entries is null)
        {
            return summaries;
        }

        foreach (var entry in entries)
        {
            var skills = entry["skills"] as JArray;
            if (skills is null)
            {
                continue;
            }

            foreach (var skill in skills)
            {
                var name = skill["name"]?.Value<string>();
                var path = skill["path"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var isSystem = IsSystemSkillPath(path!);
                summaries.Add(new CodexSkillSummary
                {
                    Name = name!,
                    DisplayName = skill["interface"]?["displayName"]?.Value<string>() ?? name!,
                    Description = skill["description"]?.Value<string>() ?? string.Empty,
                    ShortDescription = skill["interface"]?["shortDescription"]?.Value<string>()
                        ?? skill["shortDescription"]?.Value<string>()
                        ?? string.Empty,
                    Path = path!,
                    IsEnabled = skill["enabled"]?.Value<bool>() ?? true,
                    IsSystem = isSystem,
                    ScopeLabel = BuildSkillScopeLabel(path!, workingDirectory, homeSkillsDirectory, isSystem, localization)
                });
            }
        }

        return summaries
            .OrderBy(skill => skill.IsSystem)
            .ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<CodexRemoteSkillSummary>> ListRemoteSkillsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        try
        {
            return await ListPluginMarketplaceSkillsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (CodexAppServerException ex) when (ex.IsMethodNotFound)
        {
            return await ListLegacyRemoteSkillsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<CodexRemoteSkillSummary>> ListPluginMarketplaceSkillsAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            "plugin/list",
            new
            {
                cwds = new[] { workingDirectory },
                marketplaceKinds = new[] { "local", "vertical", "workspace-directory", "shared-with-me", "created-by-me-remote" }
            },
            cancellationToken).ConfigureAwait(false);

        var summaries = new List<CodexRemoteSkillSummary>();
        var marketplaces = response?["marketplaces"] as JArray;
        if (marketplaces is null)
        {
            return summaries;
        }

        foreach (var marketplace in marketplaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marketplaceName = marketplace?["name"]?.Value<string>();
            var plugins = marketplace?["plugins"] as JArray;
            if (string.IsNullOrWhiteSpace(marketplaceName) || plugins is null)
            {
                continue;
            }

            foreach (var plugin in plugins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (plugin?["installed"]?.Value<bool>() == true)
                {
                    continue;
                }

                var pluginName = plugin?["name"]?.Value<string>();
                var remotePluginId = plugin?["remotePluginId"]?.Value<string>() ?? string.Empty;
                var sourceType = plugin?["source"]?["type"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(pluginName)
                    || (string.IsNullOrWhiteSpace(remotePluginId)
                        && !string.Equals(sourceType, "remote", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                JToken? detail;
                try
                {
                    var detailResponse = await SendRequestAsync(
                        "plugin/read",
                        new
                        {
                            pluginName,
                            remoteMarketplaceName = marketplaceName
                        },
                        cancellationToken).ConfigureAwait(false);
                    detail = detailResponse?["plugin"];
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    PublishError("[plugin/read] " + ex.Message + Environment.NewLine);
                    continue;
                }

                var skills = detail?["skills"] as JArray;
                if (skills is null)
                {
                    continue;
                }

                foreach (var skill in skills)
                {
                    var skillName = skill?["name"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(skillName) || skill?["enabled"]?.Value<bool>() == false)
                    {
                        continue;
                    }

                    var displayName = skill?["interface"]?["displayName"]?.Value<string>();
                    summaries.Add(new CodexRemoteSkillSummary
                    {
                        Id = marketplaceName + "\u001f" + pluginName + "\u001f" + skillName,
                        Name = string.IsNullOrWhiteSpace(displayName) ? skillName! : displayName!,
                        Description = skill?["interface"]?["shortDescription"]?.Value<string>()
                            ?? skill?["shortDescription"]?.Value<string>()
                            ?? skill?["description"]?.Value<string>()
                            ?? plugin?["interface"]?["shortDescription"]?.Value<string>()
                            ?? string.Empty,
                        MarketplaceName = marketplaceName!,
                        PluginName = pluginName!,
                        RemotePluginId = remotePluginId,
                        SkillName = skillName!
                    });
                }
            }
        }

        return summaries
            .GroupBy(skill => skill.MarketplaceName + "\u001f" + skill.PluginName + "\u001f" + skill.SkillName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<CodexRemoteSkillSummary>> ListLegacyRemoteSkillsAsync(CancellationToken cancellationToken)
    {

        var response = await SendRequestAsync(
            "skills/remote/list",
            new
            {
                hazelnutScope = "all-shared",
                productSurface = "codex",
                enabled = true
            },
            cancellationToken).ConfigureAwait(false);

        var items = response?["data"] as JArray;
        var summaries = new List<CodexRemoteSkillSummary>();
        if (items is null)
        {
            return summaries;
        }

        foreach (var item in items)
        {
            var id = item?["id"]?.Value<string>();
            var name = item?["name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            summaries.Add(new CodexRemoteSkillSummary
            {
                Id = id!,
                Name = name!,
                Description = item?["description"]?.Value<string>() ?? string.Empty
            });
        }

        return summaries
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string?> InstallRemoteSkillAsync(CodexExtensionSettings settings, CodexRemoteSkillSummary remoteSkill, CancellationToken cancellationToken)
    {
        if (remoteSkill is null || string.IsNullOrWhiteSpace(remoteSkill.Id))
        {
            return null;
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        if (remoteSkill.UsesPluginMarketplace)
        {
            await SendRequestAsync(
                "plugin/install",
                new
                {
                    pluginName = remoteSkill.PluginName,
                    remoteMarketplaceName = remoteSkill.MarketplaceName
                },
                cancellationToken).ConfigureAwait(false);

            InvalidateSkillsCache();
            var installedSkills = await ListSkillsAsync(settings, cancellationToken, forceReload: true).ConfigureAwait(false);
            return installedSkills.FirstOrDefault(skill =>
                string.Equals(skill.Name, remoteSkill.SkillName, StringComparison.OrdinalIgnoreCase))?.Path;
        }

        var response = await SendRequestAsync(
            "skills/remote/export",
            new { hazelnutId = remoteSkill.Id },
            cancellationToken).ConfigureAwait(false);

        return response?["path"]?.Value<string>();
    }

    public async Task<bool> SetSkillEnabledAsync(CodexExtensionSettings settings, string path, bool enabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return enabled;
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync(
            "skills/config/write",
            new
            {
                path,
                enabled
            },
            cancellationToken).ConfigureAwait(false);

        return response?["effectiveEnabled"]?.Value<bool>() ?? enabled;
    }

    public async Task<CodexRateLimitSummary> GetAccountRateLimitsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync("account/rateLimits/read", new { }, cancellationToken).ConfigureAwait(false);
        return BuildRateLimitSummary(response);
    }

    public async Task LogoutAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);
        try
        {
            await SendRequestAsync("account/logout", null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RestartServer(clearConfig: false);
        }
    }

    public void InvalidateSkillsCache()
    {
        lock (_syncRoot)
        {
            _skillsCacheKey = null;
            _skillsByName.Clear();
        }
    }

    private async Task EnsureServerReadyAsync(CodexExtensionSettings settings, string workingDirectory, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _languageOverride = settings.LanguageOverride;
            var desiredServerConfig = BuildServerConfigKey(settings);
            Task? initializedTask = null;
            var isReady = false;

            lock (_syncRoot)
            {
                isReady = _serverProcess is not null
                    && !_serverProcess.HasExited
                    && string.Equals(_serverConfigKey, desiredServerConfig, StringComparison.Ordinal);
                if (isReady)
                {
                    initializedTask = _initializedTcs?.Task;
                }
            }

            if (isReady)
            {
                if (initializedTask is not null)
                {
                    await initializedTask.ConfigureAwait(false);
                }

                return;
            }

            RestartServer(clearConfig: false);
            lock (_syncRoot)
            {
                _serverConfigKey = desiredServerConfig;
                _initializedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _lastServerError = string.Empty;
            }

            try
            {
                await EnsureExecutableAvailableAsync(settings, userInitiated: false, cancellationToken).ConfigureAwait(false);
                StartServerProcess(settings, workingDirectory);
                await InitializeServerAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string diagnostics;
                lock (_syncRoot)
                {
                    diagnostics = _lastServerError;
                    _initializedTcs?.TrySetException(ex);
                }

                RestartServer(clearConfig: false);
                if (!string.IsNullOrWhiteSpace(diagnostics)
                    && ex.Message.IndexOf(diagnostics, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException(ex.Message + Environment.NewLine + diagnostics.Trim(), ex);
                }

                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task InitializeServerAsync(CancellationToken cancellationToken)
    {
        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "codex-vsix", version = ExtensionInfo.Version },
                capabilities = new { experimentalApi = true }
            },
            cancellationToken).ConfigureAwait(false);

        await SendNotificationAsync("initialized", new { }).ConfigureAwait(false);

        lock (_syncRoot)
        {
            _initializedTcs?.TrySetResult(true);
        }
    }

    private async Task EnsureThreadReadyAsync(CodexExtensionSettings settings, string workingDirectory, string? requestedThreadId, CancellationToken cancellationToken)
    {
        var desiredThreadConfig = BuildThreadConfigKey(settings, workingDirectory);
        if (!string.IsNullOrWhiteSpace(requestedThreadId))
        {
            if (string.Equals(CurrentThreadId, requestedThreadId, StringComparison.Ordinal) && _threadLoaded && string.Equals(_threadConfigKey, desiredThreadConfig, StringComparison.Ordinal))
            {
                return;
            }

            var resumed = await SendRequestAsync(
                "thread/resume",
                BuildThreadResumeParams(requestedThreadId!, settings, workingDirectory),
                cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                _threadId = resumed?["thread"]?["id"]?.Value<string>() ?? requestedThreadId;
                _threadConfigKey = desiredThreadConfig;
                _threadLoaded = true;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(_threadId) && _threadLoaded && string.Equals(_threadConfigKey, desiredThreadConfig, StringComparison.Ordinal))
        {
            return;
        }

        var result = await SendRequestAsync(
            "thread/start",
            BuildThreadStartParams(settings, workingDirectory),
            cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            _threadId = result?["thread"]?["id"]?.Value<string>();
            _threadConfigKey = desiredThreadConfig;
            _threadLoaded = true;
        }
    }

    private async Task RefreshSkillsAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        if (string.Equals(_skillsCacheKey, workingDirectory, StringComparison.OrdinalIgnoreCase) && _skillsByName.Count > 0)
        {
            return;
        }

        try
        {
            var response = await SendRequestAsync(
                "skills/list",
                new { cwds = new[] { workingDirectory }, forceReload = false },
                cancellationToken).ConfigureAwait(false);

            var entries = response?["data"] as JArray;
            lock (_syncRoot)
            {
                _skillsByName.Clear();

                if (entries is not null)
                {
                    foreach (var entry in entries)
                    {
                        var skills = entry["skills"] as JArray;
                        if (skills is null)
                        {
                            continue;
                        }

                        foreach (var skill in skills)
                        {
                            var enabled = skill["enabled"]?.Value<bool>() ?? true;
                            var name = skill["name"]?.Value<string>();
                            var path = skill["path"]?.Value<string>();
                            if (enabled && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                            {
                                _skillsByName[name!] = path!;
                            }
                        }
                    }
                }

                _skillsCacheKey = workingDirectory;
            }
        }
        catch
        {
            lock (_syncRoot)
            {
                _skillsByName.Clear();
                _skillsCacheKey = workingDirectory;
            }
        }
    }

    private static Task<bool> IsExecutableMissingAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        return Task.Run(
            () => string.IsNullOrWhiteSpace(CodexExecutableResolver.ResolveExecutableLocation(settings.CodexExecutablePath, settings.EnvironmentVariables)),
            cancellationToken);
    }

    private async Task EnsureExecutableAvailableAsync(CodexExtensionSettings settings, bool userInitiated, CancellationToken cancellationToken)
    {
        var handler = MissingExecutableHandler;
        if (handler is null)
        {
            return;
        }

        var resolved = await Task.Run(
            () => CodexExecutableResolver.ResolveExecutableLocation(settings.CodexExecutablePath, settings.EnvironmentVariables),
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        _diagnostics.Write("appserver.executable.missing", new JObject { ["configured"] = settings.CodexExecutablePath });
        if (!await handler(settings, userInitiated, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Codex CLI not found. Install it with: " + CodexEnvironmentService.FallbackInstallCommand);
        }
    }

    private void StartServerProcess(CodexExtensionSettings settings, string workingDirectory)
    {
        var executablePath = CodexExecutableResolver.ResolveExecutableLocation(settings.CodexExecutablePath, settings.EnvironmentVariables);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = CodexExecutableResolver.NormalizeConfiguredExecutablePath(settings.CodexExecutablePath);
        }

        var arguments = BuildServerArguments(settings);
        var startInfo = BuildStartInfo(executablePath, arguments, workingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = startInfo.FileName,
            WorkingDirectory = startInfo.WorkingDirectory,
            Arguments = startInfo.Arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        ApplyEnvironmentVariables(psi, settings.EnvironmentVariables);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();

        var serverInput = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false), 1024, true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        long generation;
        lock (_syncRoot)
        {
            generation = ++_serverGeneration;
            _serverProcess = process;
            _serverInput = serverInput;
        }

        _diagnostics.Write(
            "appserver.process.started",
            new JObject
            {
                ["generation"] = generation,
                ["processId"] = process.Id,
                ["executable"] = Path.GetFileName(executablePath)
            });

        process.Exited += (_, _) => FailPendingOperations(process, generation, GetLocalization().AppServerClosedUnexpectedly);
        _ = Task.Run(() => ReadStdoutLoopAsync(process, generation));
        _ = Task.Run(() => ReadStderrLoopAsync(process, generation));
    }

    private async Task ReadStdoutLoopAsync(Process process, long generation)
    {
        try
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    HandleServerMessage(line, generation);
                }
                catch (Exception ex)
                {
                    _diagnostics.Write(
                        "appserver.message.failed",
                        new JObject
                        {
                            ["generation"] = generation,
                            ["errorType"] = ex.GetType().FullName,
                            ["error"] = ex.Message,
                            ["messageLength"] = line.Length
                        });
                    PublishError("[" + GetLocalization().OutputTagAppServer + "] " + ex.Message + Environment.NewLine);
                }
            }
        }
        catch (Exception ex)
        {
            FailPendingOperations(process, generation, ex.Message);
        }
    }

    private async Task ReadStderrLoopAsync(Process process, long generation)
    {
        try
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is not null)
                {
                    _diagnostics.Write(
                        "appserver.stderr",
                        new JObject
                        {
                            ["generation"] = generation,
                            ["message"] = line
                        });
                    lock (_syncRoot)
                    {
                        if (ReferenceEquals(_serverProcess, process) && _serverGeneration == generation)
                        {
                            _lastServerError = string.IsNullOrWhiteSpace(_lastServerError)
                                ? line
                                : _lastServerError + Environment.NewLine + line;
                            if (_lastServerError.Length > 8000)
                            {
                                _lastServerError = _lastServerError.Substring(_lastServerError.Length - 8000);
                            }
                        }
                    }

                    PublishError(line + Environment.NewLine);
                }
            }
        }
        catch (Exception ex)
        {
            _diagnostics.Write(
                "appserver.stderr-reader.failed",
                new JObject
                {
                    ["generation"] = generation,
                    ["errorType"] = ex.GetType().FullName,
                    ["error"] = ex.Message
                });
            PublishError("[" + GetLocalization().OutputTagStderr + "] " + ex.Message + Environment.NewLine);
        }
    }

    private void HandleServerMessage(string rawMessage, long generation)
    {
        lock (_syncRoot)
        {
            if (_serverGeneration != generation)
            {
                return;
            }
        }

        JToken parsedMessage;
        try
        {
            parsedMessage = JToken.Parse(rawMessage);
        }
        catch
        {
            _diagnostics.Write(
                "appserver.message.invalid-json",
                new JObject
                {
                    ["generation"] = generation,
                    ["messageLength"] = rawMessage.Length
                });
            PublishError("[" + GetLocalization().OutputTagAppServer + "] " + rawMessage + Environment.NewLine);
            return;
        }

        if (parsedMessage is not JObject message)
        {
            _diagnostics.Write(
                "appserver.message.invalid-shape",
                new JObject
                {
                    ["generation"] = generation,
                    ["tokenType"] = parsedMessage.Type.ToString()
                });
            PublishError("[" + GetLocalization().OutputTagAppServer + "] " + rawMessage + Environment.NewLine);
            return;
        }

        if (message["id"] is not null && (message["result"] is not null || message["error"] is not null) && message["method"] is null)
        {
            ResolvePendingRequest(message);
            return;
        }

        if (message["id"] is not null && message["method"] is not null)
        {
            _ = HandleServerRequestAsync(message);
            return;
        }

        HandleNotification(message);
    }

    private void ResolvePendingRequest(JObject message)
    {
        var id = message["id"]?.Value<long>() ?? 0L;
        PendingRequest? pendingRequest;
        lock (_syncRoot)
        {
            _pendingRequests.TryGetValue(id, out pendingRequest);
            if (pendingRequest is not null)
            {
                _pendingRequests.Remove(id);
            }
        }

        if (pendingRequest is null)
        {
            return;
        }

        var tcs = pendingRequest.Completion;

        if (message["error"] is JToken error
            && error.Type != JTokenType.Null
            && error.Type != JTokenType.Undefined)
        {
            var errorMessage = ExtractAppServerErrorMessage(error, GetLocalization().AppServerRequestFailed);
            var errorCode = ExtractAppServerErrorCode(error);
            _diagnostics.Write(
                "appserver.response.error",
                new JObject
                {
                    ["requestId"] = id,
                    ["method"] = pendingRequest.Method,
                    ["errorType"] = error.Type.ToString(),
                    ["code"] = errorCode,
                    ["message"] = errorMessage
                });
            tcs.TrySetException(new CodexAppServerException(errorMessage, errorCode));
            return;
        }

        _diagnostics.Write(
            "appserver.response.completed",
            new JObject
            {
                ["requestId"] = id,
                ["method"] = pendingRequest.Method,
                ["resultType"] = message["result"]?.Type.ToString() ?? "missing"
            });
        tcs.TrySetResult(message["result"]);
    }

    private async Task HandleServerRequestAsync(JObject message)
    {
        var id = message["id"];
        var method = message["method"]?.Value<string>() ?? string.Empty;
        var parameters = message["params"] as JObject;
        try
        {
            var appServerRequestHandler = AppServerRequestHandler;
            if (appServerRequestHandler is not null)
            {
                var webViewResponse = await appServerRequestHandler((JObject)message.DeepClone()).ConfigureAwait(false);
                if (webViewResponse is not null)
                {
                    if (webViewResponse["error"] is JToken error)
                    {
                        var errorCode = ExtractAppServerErrorCode(error) ?? -32603;
                        var errorMessage = ExtractAppServerErrorMessage(
                            error,
                            "The Codex interface could not resolve the app-server request.");
                        await SendErrorResponseAsync(id, errorCode, errorMessage).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendResponseAsync(id, webViewResponse["result"]?.DeepClone() ?? new JObject()).ConfigureAwait(false);
                    }

                    return;
                }
            }

            if (IsApprovalRequestMethod(method))
            {
                var approvalRequest = BuildApprovalRequest(method, parameters);
                var decision = await ResolveApprovalDecisionAsync(approvalRequest).ConfigureAwait(false);
                await SendResponseAsync(
                    id,
                    new JObject
                    {
                        ["decision"] = decision
                    }).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "item/tool/requestUserInput", StringComparison.Ordinal))
            {
                var userInputRequest = BuildUserInputRequest(parameters);
                var response = await ResolveUserInputRequestAsync(userInputRequest).ConfigureAwait(false);
                await SendResponseAsync(id, response ?? new JObject { ["answers"] = new JObject() }).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "item/permissions/requestApproval", StringComparison.Ordinal))
            {
                var requestedPermissions = parameters?["permissions"] as JObject ?? new JObject();
                var request = new CodexApprovalRequest
                {
                    Method = method,
                    ThreadId = parameters?["threadId"]?.Value<string>() ?? string.Empty,
                    TurnId = parameters?["turnId"]?.Value<string>() ?? string.Empty,
                    ItemId = parameters?["itemId"]?.Value<string>() ?? string.Empty,
                    WorkingDirectory = parameters?["cwd"]?.Value<string>(),
                    Reason = parameters?["reason"]?.Value<string>(),
                    Command = NewtonsoftJsonCompatibility.Serialize(requestedPermissions, Formatting.Indented),
                    Options = new[]
                    {
                        new CodexApprovalOption("accept", requestedPermissions.DeepClone()),
                        new CodexApprovalOption("decline", new JObject())
                    }
                };
                var permissions = await ResolveApprovalDecisionAsync(request).ConfigureAwait(false);
                await SendResponseAsync(id, new JObject
                {
                    ["permissions"] = permissions is JObject ? permissions : new JObject(),
                    ["scope"] = "turn"
                }).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "mcpServer/elicitation/request", StringComparison.Ordinal))
            {
                var response = await ResolveMcpElicitationAsync(parameters).ConfigureAwait(false);
                await SendResponseAsync(id, response).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "item/tool/call", StringComparison.Ordinal))
            {
                await SendResponseAsync(id, new JObject
                {
                    ["success"] = false,
                    ["contentItems"] = new JArray(new JObject
                    {
                        ["type"] = "inputText",
                        ["text"] = "This Visual Studio client did not register the requested dynamic tool."
                    })
                }).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "currentTime/read", StringComparison.Ordinal))
            {
                await SendResponseAsync(id, new JObject
                {
                    ["currentTimeAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }).ConfigureAwait(false);
                return;
            }

            await SendErrorResponseAsync(id, -32601, "Method not supported by the Visual Studio client: " + method).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PublishError("[" + GetLocalization().OutputTagAppServer + "] " + ex.Message + Environment.NewLine);
            await SendErrorResponseAsync(id, -32603, ex.Message).ConfigureAwait(false);
        }
    }

    private void HandleNotification(JObject message)
    {
        var method = message["method"]?.Value<string>() ?? string.Empty;
        var parameters = message["params"] as JObject;

        try
        {
            AppServerNotificationReceived?.Invoke(method, parameters?.DeepClone());
        }
        catch (Exception ex)
        {
            PublishError("[webview notification] " + ex.Message + Environment.NewLine);
        }

        switch (method)
        {
            case "turn/started":
                HandleTurnStarted(parameters);
                break;

            case "item/agentMessage/delta":
                HandleAgentMessageDelta(parameters);
                break;

            case "item/plan/delta":
                HandlePlanDelta(parameters);
                break;

            case "item/completed":
                HandleCompletedItem(parameters);
                break;

            case "turn/plan/updated":
                HandleTurnPlanUpdated(parameters);
                break;

            case "turn/completed":
                HandleTurnCompleted(parameters);
                break;

            case "codex/event/agent_message":
                HandleAgentMessageEvent(parameters);
                break;

            case "codex/event/task_complete":
                HandleTaskCompleteEvent(parameters);
                break;

            case "codex/event/token_count":
                HandleTokenCountEvent(parameters);
                break;

            case "thread/tokenUsage/updated":
                HandleTokenUsageUpdated(parameters);
                break;

            case "item/mcpToolCall/progress":
                HandleMcpToolCallProgress(parameters);
                break;

            case "thread/started":
            case "thread/nameUpdated":
            case "thread/statusChanged":
            case "thread/closed":
            case "thread/name/updated":
            case "thread/status/changed":
            case "thread/closed/updated":
                NotifyThreadCatalogChanged();
                break;

            case "account/rateLimits/updated":
                PublishRateLimitsUpdate(parameters);
                break;

            case "account/updated":
                AccountUpdated?.Invoke();
                break;

            case "skills/changed":
                lock (_syncRoot)
                {
                    _skillsCacheKey = null;
                }
                break;

            case "error":
                var errorMessage = GetNestedString(parameters?["error"], "message")
                    ?? parameters?["error"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    PublishError(errorMessage + Environment.NewLine);
                }
                break;
        }
    }

    private void HandleTurnStarted(JToken? parameters)
    {
        var turnId = parameters?["turn"]?["id"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(turnId))
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_activeTurn is not null && string.IsNullOrWhiteSpace(_activeTurn.TurnId))
            {
                _activeTurn.TurnId = turnId;
            }
        }
    }

    private void HandleAgentMessageDelta(JToken? parameters)
    {
        if (!MatchesActiveTurn(parameters?["turnId"]?.Value<string>()))
        {
            return;
        }

        var itemId = parameters?["itemId"]?.Value<string>();
        var delta = parameters?["delta"]?.Value<string>();

        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
            if (turnState is not null && !string.IsNullOrWhiteSpace(itemId))
            {
                turnState.StreamedItemIds.Add(itemId!);
            }
        }

        if (!string.IsNullOrWhiteSpace(delta))
        {
            turnState!.HasAssistantOutput = true;
            turnState?.OnOutput(delta!);
        }
    }

    private void HandlePlanDelta(JToken? parameters)
    {
        if (!MatchesActiveTurn(parameters?["turnId"]?.Value<string>()))
        {
            return;
        }

        var delta = parameters?["delta"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(delta))
        {
            return;
        }

        var itemId = parameters?["itemId"]?.Value<string>();
        string accumulatedPlan;

        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
            if (turnState is null)
            {
                return;
            }

            accumulatedPlan = turnState.AppendPlanDelta(itemId, delta!);
        }

        turnState.OnEventMessage?.Invoke(CreatePlanEventMessage(accumulatedPlan));
    }

    private void HandleAgentMessageEvent(JToken? parameters)
    {
        if (!MatchesActiveTurnEvent(parameters))
        {
            return;
        }

        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
        }

        if (turnState is null)
        {
            return;
        }

        var payload = parameters?["msg"];
        var message = payload?["message"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var phase = payload?["phase"]?.Value<string>();
        if (string.Equals(phase, "commentary", StringComparison.OrdinalIgnoreCase))
        {
            turnState.OnEventMessage?.Invoke(new ChatMessage(false, message!.Trim(), isEvent: true, title: GetLocalization().EventCommentaryTitle));
            return;
        }

        PublishAssistantOutput(turnState, message, skipIfAlreadyPublished: true);
    }

    private void HandleTurnPlanUpdated(JToken? parameters)
    {
        if (!MatchesActiveTurn(parameters?["turnId"]?.Value<string>()))
        {
            return;
        }

        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
        }

        if (turnState is null)
        {
            return;
        }

        var planMarkdown = BuildStructuredPlanMarkdown(
            parameters?["explanation"]?.Value<string>(),
            parameters?["plan"] as JArray,
            GetLocalization());
        if (string.IsNullOrWhiteSpace(planMarkdown))
        {
            return;
        }

        lock (_syncRoot)
        {
            turnState.SetPlanText(null, planMarkdown!);
        }

        turnState.OnEventMessage?.Invoke(CreatePlanEventMessage(planMarkdown));
    }

    private void HandleTaskCompleteEvent(JToken? parameters)
    {
        if (!MatchesActiveTurnEvent(parameters))
        {
            return;
        }

        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
        }

        if (turnState is null)
        {
            return;
        }

        var lastAgentMessage = parameters?["msg"]?["last_agent_message"]?.Value<string>();
        PublishAssistantOutput(turnState, lastAgentMessage, skipIfAlreadyPublished: true);
    }

    private void HandleTokenCountEvent(JToken? parameters)
    {
        if (!MatchesActiveTurnEvent(parameters))
        {
            return;
        }

        var info = parameters?["msg"]?["info"];
        var tokensInContextWindow = GetNestedToken(info, "last_token_usage", "total_tokens")?.Value<long?>()
            ?? GetNestedToken(info, "lastTokenUsage", "totalTokens")?.Value<long?>()
            ?? GetNestedToken(info, "total_token_usage", "total_tokens")?.Value<long?>()
            ?? GetNestedToken(info, "totalTokenUsage", "totalTokens")?.Value<long?>()
            ?? 0L;
        var contextWindow = GetNestedToken(info, "model_context_window")?.Value<long?>()
            ?? GetNestedToken(info, "modelContextWindow")?.Value<long?>();

        lock (_syncRoot)
        {
            _activeTurn?.OnTokenUsage?.Invoke(tokensInContextWindow, contextWindow);
        }
    }

    private void HandleMcpToolCallProgress(JToken? parameters)
    {
        if (!MatchesActiveTurn(parameters?["turnId"]?.Value<string>()))
        {
            return;
        }

        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
        }

        var message = parameters?["message"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            turnState?.OnEventMessage?.Invoke(new ChatMessage(false, message!.Trim(), isEvent: true, title: GetLocalization().EventMcpProgressTitle));
        }
    }

    private void HandleCompletedItem(JToken? parameters)
    {
        if (!MatchesActiveTurn(parameters?["turnId"]?.Value<string>()))
        {
            return;
        }

        var item = parameters?["item"];
        var itemType = item?["type"]?.Value<string>();

        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
        }

        if (turnState is null)
        {
            return;
        }

        if (string.Equals(itemType, "plan", StringComparison.OrdinalIgnoreCase))
        {
            var itemId = item?["id"]?.Value<string>();
            string? planText;
            lock (_syncRoot)
            {
                planText = turnState.GetPlanText(itemId);
            }

            if (string.IsNullOrWhiteSpace(planText))
            {
                planText = NormalizeDetail(item?["text"]?.Value<string>(), maxLength: null);
            }

            turnState.OnEventMessage?.Invoke(CreatePlanEventMessage(planText));
            return;
        }

        if (!string.Equals(itemType, "agentMessage", StringComparison.OrdinalIgnoreCase))
        {
            var eventMessage = BuildThreadEventMessage(item);
            if (eventMessage is not null && eventMessage.IsEvent)
            {
                turnState.OnEventMessage?.Invoke(eventMessage);
            }

            return;
        }

        var agentItemId = item?["id"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(agentItemId) && turnState.StreamedItemIds.Contains(agentItemId!))
        {
            return;
        }

        var text = item?["text"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = ExtractText(item?["content"]);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            PublishAssistantOutput(turnState, text, skipIfAlreadyPublished: false);
        }
    }

    private void HandleTurnCompleted(JToken? parameters)
    {
        if (!MatchesActiveTurn(parameters?["turn"]?["id"]?.Value<string>()))
        {
            return;
        }

        var status = parameters?["turn"]?["status"]?.Value<string>();
        var errorMessage = GetNestedString(parameters?["turn"], "error", "message");
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            PublishError(errorMessage + Environment.NewLine);
        }

        lock (_syncRoot)
        {
            _activeTurn?.TrySetResult(string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        }
    }

    private void HandleTokenUsageUpdated(JToken? parameters)
    {
        if (!MatchesActiveTurn(parameters?["turnId"]?.Value<string>()))
        {
            return;
        }

        var tokenUsage = parameters?["tokenUsage"];
        var tokensInContextWindow = GetNestedToken(tokenUsage, "last", "totalTokens")?.Value<long?>()
            ?? GetNestedToken(tokenUsage, "lastTokenUsage", "totalTokens")?.Value<long?>()
            ?? GetNestedToken(tokenUsage, "last_token_usage", "total_tokens")?.Value<long?>()
            ?? GetNestedToken(tokenUsage, "total", "totalTokens")?.Value<long?>()
            ?? GetNestedToken(tokenUsage, "totalTokenUsage", "totalTokens")?.Value<long?>()
            ?? GetNestedToken(tokenUsage, "total_token_usage", "total_tokens")?.Value<long?>()
            ?? 0L;
        var contextWindow = GetNestedToken(parameters?["tokenUsage"], "modelContextWindow")?.Value<long?>();

        lock (_syncRoot)
        {
            _activeTurn?.OnTokenUsage?.Invoke(tokensInContextWindow, contextWindow);
        }
    }

    private bool MatchesActiveTurn(string? turnId)
    {
        lock (_syncRoot)
        {
            return _activeTurn is not null && (string.IsNullOrWhiteSpace(_activeTurn.TurnId) || string.Equals(_activeTurn.TurnId, turnId, StringComparison.Ordinal));
        }
    }

    private bool MatchesActiveTurnEvent(JToken? parameters)
    {
        var turnId = parameters?["id"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(turnId))
        {
            turnId = parameters?["msg"]?["turn_id"]?.Value<string>();
        }

        return MatchesActiveTurn(turnId);
    }

    private static void PublishAssistantOutput(ActiveTurnState turnState, string? text, bool skipIfAlreadyPublished)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (skipIfAlreadyPublished && turnState.HasAssistantOutput)
        {
            return;
        }

        turnState.HasAssistantOutput = true;
        turnState.OnOutput(text!);
    }

    private async Task<bool> InterruptActiveTurnAsync()
    {
        ActiveTurnState? turnState;
        string? threadId;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
            threadId = _threadId;
        }

        if (turnState is null || string.IsNullOrWhiteSpace(turnState.TurnId) || string.IsNullOrWhiteSpace(threadId))
        {
            return false;
        }

        if (turnState.InterruptRequested)
        {
            return true;
        }

        turnState.InterruptRequested = true;

        try
        {
            await SendRequestAsync(
                "turn/interrupt",
                new { threadId, turnId = turnState.TurnId },
                CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<JToken?> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);
        long generation;

        lock (_syncRoot)
        {
            if (_serverInput is null || _serverProcess is null || _serverProcess.HasExited)
            {
                throw new InvalidOperationException(GetLocalization().AppServerUnavailable);
            }

            generation = _serverGeneration;
            _pendingRequests[id] = new PendingRequest(generation, method, tcs);
        }

        _diagnostics.Write(
            "appserver.request.sent",
            new JObject
            {
                ["requestId"] = id,
                ["generation"] = generation,
                ["method"] = method
            });

        using (cancellationToken.Register(() =>
        {
            lock (_syncRoot)
            {
                if (_pendingRequests.TryGetValue(id, out var pending)
                    && ReferenceEquals(pending.Completion, tcs))
                {
                    _pendingRequests.Remove(id);
                    tcs.TrySetCanceled(cancellationToken);
                }
            }
        }))
        {
            try
            {
                WriteMessage(new JObject
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = ConvertParameters(parameters)
                }, generation);
            }
            catch (Exception ex)
            {
                lock (_syncRoot)
                {
                    if (_pendingRequests.TryGetValue(id, out var pending)
                        && ReferenceEquals(pending.Completion, tcs))
                    {
                        _pendingRequests.Remove(id);
                    }
                }

                _diagnostics.Write(
                    "appserver.request.write-failed",
                    new JObject
                    {
                        ["requestId"] = id,
                        ["generation"] = generation,
                        ["method"] = method,
                        ["errorType"] = ex.GetType().FullName,
                        ["error"] = ex.Message
                    });
                throw;
            }

            return await tcs.Task.ConfigureAwait(false);
        }
    }

    private Task SendNotificationAsync(string method, object parameters)
    {
        WriteMessage(new JObject
        {
            ["method"] = method,
            ["params"] = ConvertParameters(parameters)
        });

        return Task.CompletedTask;
    }

    private Task SendResponseAsync(JToken? id, JToken result)
    {
        WriteMessage(new JObject
        {
            ["id"] = id,
            ["result"] = result
        });

        return Task.CompletedTask;
    }

    private Task SendErrorResponseAsync(JToken? id, int code, string message)
    {
        WriteMessage(new JObject
        {
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message ?? string.Empty
            }
        });

        return Task.CompletedTask;
    }

    private static JToken ConvertParameters(object? parameters)
    {
        if (parameters is null)
        {
            return JValue.CreateNull();
        }

        return parameters is JToken token ? token : JToken.FromObject(parameters);
    }

    private void WriteMessage(JObject message, long? expectedGeneration = null)
    {
        StreamWriter? writer;
        lock (_syncRoot)
        {
            if (expectedGeneration.HasValue && expectedGeneration.Value != _serverGeneration)
            {
                throw new InvalidOperationException(GetLocalization().AppServerUnavailable);
            }

            writer = _serverInput;
        }

        if (writer is null)
        {
            throw new InvalidOperationException(GetLocalization().AppServerUnavailable);
        }

        var json = NewtonsoftJsonCompatibility.Serialize(message, Formatting.None);
        if (string.Equals(message["method"]?.Value<string>(), "turn/start", StringComparison.Ordinal))
        {
            var inputs = message["params"]?["input"] as JArray;
            _diagnostics.Write(
                "appserver.turn.start.inputSummary",
                new JObject
                {
                    ["inputCount"] = inputs?.Count ?? 0,
                    ["textCount"] = inputs?.Count(item => item?["type"]?.Value<string>() == "text") ?? 0,
                    ["imageCount"] = inputs?.Count(item => item?["type"]?.Value<string>() == "image") ?? 0,
                    ["localImageCount"] = inputs?.Count(item => item?["type"]?.Value<string>() == "localImage") ?? 0,
                    ["inlineImageDataLength"] = inputs?
                        .Where(item => item?["type"]?.Value<string>() == "image")
                        .Sum(item => item?["url"]?.Value<string>()?.Length ?? 0) ?? 0
                });
        }

        lock (_writeLock)
        {
            writer.WriteLine(json);
            writer.Flush();
        }
    }

    private JObject BuildThreadStartParams(CodexExtensionSettings settings, string workingDirectory)
    {
        var result = new JObject
        {
            ["cwd"] = workingDirectory,
            ["approvalPolicy"] = NormalizeApprovalPolicy(settings.ApprovalPolicy),
            ["sandbox"] = NormalizeSandboxMode(settings.SandboxMode),
            ["model"] = string.IsNullOrWhiteSpace(settings.DefaultModel) ? null : settings.DefaultModel,
            ["serviceTier"] = NormalizeServiceTier(settings.ServiceTier),
            ["personality"] = "pragmatic"
        };
        return (JObject)CodexRuntimeIdentityContext.EnrichRequest(
            "thread/start",
            result,
            settings.DefaultModel,
            settings.ReasoningEffort)!;
    }

    private object BuildTurnStartParams(string threadId, string prompt, CodexExtensionSettings settings, string workingDirectory, IEnumerable<string> imagePaths, string ideContextSummary)
    {
        return BuildTurnStartParams(threadId, prompt, settings, workingDirectory, imagePaths, ideContextSummary, includeExecutionOverrides: true, includeCollaborationMode: true);
    }

    private JObject BuildTurnStartParams(
        string threadId,
        string prompt,
        CodexExtensionSettings settings,
        string workingDirectory,
        IEnumerable<string> imagePaths,
        string ideContextSummary,
        bool includeExecutionOverrides,
        bool includeCollaborationMode)
    {
        var input = BuildUserInput(prompt, settings, workingDirectory, imagePaths, ideContextSummary);
        var result = new JObject
        {
            ["threadId"] = threadId,
            ["cwd"] = workingDirectory,
            ["input"] = JArray.FromObject(input)
        };

        if (!includeExecutionOverrides)
        {
            return result;
        }

        AddIfNotBlank(result, "model", settings.DefaultModel);
        AddIfNotBlank(result, "effort", settings.ReasoningEffort);
        result["approvalPolicy"] = NormalizeApprovalPolicy(settings.ApprovalPolicy);
        result["sandboxPolicy"] = JToken.FromObject(BuildSandboxPolicy(settings.SandboxMode));
        AddIfNotBlank(result, "serviceTier", NormalizeServiceTier(settings.ServiceTier));

        if (includeCollaborationMode)
        {
            result["collaborationMode"] = BuildCollaborationMode(settings);
        }

        return (JObject)CodexRuntimeIdentityContext.EnrichRequest(
            "turn/start",
            result,
            settings.DefaultModel,
            settings.ReasoningEffort)!;
    }

    private async Task<JToken?> StartTurnWithFallbackAsync(
        ActiveTurnState turnState,
        string threadId,
        string prompt,
        CodexExtensionSettings settings,
        string workingDirectory,
        IEnumerable<string> imagePaths,
        string ideContextSummary,
        CancellationToken cancellationToken)
    {
        var attempts = new List<(string Label, bool IncludeExecutionOverrides, bool IncludeCollaborationMode)>
        {
            ("full", true, true),
            ("no-collaboration-mode", true, false)
        };

        attempts.Add(("minimal", false, false));

        Exception? lastError = null;
        for (var index = 0; index < attempts.Count; index++)
        {
            var attempt = attempts[index];
            try
            {
                var requestParams = BuildTurnStartParams(
                    threadId,
                    prompt,
                    settings,
                    workingDirectory,
                    imagePaths,
                    ideContextSummary,
                    attempt.IncludeExecutionOverrides,
                    attempt.IncludeCollaborationMode);
                return await SendRequestAsync("turn/start", requestParams, cancellationToken).ConfigureAwait(false);
            }
            catch (CodexAppServerException ex) when (ex.IsCompatibilityError)
            {
                lastError = ex;
                if (index < attempts.Count - 1)
                {
                    turnState.OnError("[turn/start fallback:" + attempt.Label + "] " + ex.Message + Environment.NewLine);
                }
            }
        }

        throw lastError ?? new InvalidOperationException(new LocalizationService(settings.LanguageOverride).StartTurnFailedMessage);
    }

    private async Task<JToken?> StartTurnOrReviewAsync(
        ActiveTurnState turnState,
        string threadId,
        string prompt,
        CodexExtensionSettings settings,
        string workingDirectory,
        IEnumerable<string> imagePaths,
        string ideContextSummary,
        CancellationToken cancellationToken)
    {
        if (!TryGetReviewInstructions(prompt, out var instructions))
        {
            return await StartTurnWithFallbackAsync(
                turnState,
                threadId,
                prompt,
                settings,
                workingDirectory,
                imagePaths,
                ideContextSummary,
                cancellationToken).ConfigureAwait(false);
        }

        var target = string.IsNullOrWhiteSpace(instructions)
            ? new JObject { ["type"] = "uncommittedChanges" }
            : new JObject { ["type"] = "custom", ["instructions"] = instructions };
        var delivery = string.Equals(settings.ReviewDelivery, "detached", StringComparison.OrdinalIgnoreCase)
            ? "detached"
            : "inline";

        try
        {
            var response = await SendRequestAsync(
                "review/start",
                new JObject
                {
                    ["threadId"] = threadId,
                    ["target"] = target,
                    ["delivery"] = delivery
                },
                cancellationToken).ConfigureAwait(false);

            var reviewThreadId = response?["reviewThreadId"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(reviewThreadId))
            {
                lock (_syncRoot)
                {
                    _threadId = reviewThreadId;
                    _threadLoaded = true;
                }
            }

            return response;
        }
        catch (CodexAppServerException ex) when (ex.IsMethodNotFound)
        {
            return await StartTurnWithFallbackAsync(
                turnState,
                threadId,
                prompt,
                settings,
                workingDirectory,
                imagePaths,
                ideContextSummary,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryGetReviewInstructions(string prompt, out string instructions)
    {
        instructions = string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var normalized = prompt.TrimStart();
        if (!normalized.StartsWith("/review", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.Length > "/review".Length
            && !char.IsWhiteSpace(normalized["/review".Length]))
        {
            return false;
        }

        instructions = normalized.Substring("/review".Length).Trim();
        return true;
    }

    private static JObject BuildThreadResumeParams(string threadId, CodexExtensionSettings settings, string workingDirectory, bool includeInitialTurnsPage = false)
    {
        var result = new JObject
        {
            ["threadId"] = threadId,
            ["cwd"] = workingDirectory,
            ["approvalPolicy"] = NormalizeApprovalPolicy(settings.ApprovalPolicy),
            ["sandbox"] = NormalizeSandboxMode(settings.SandboxMode),
            ["model"] = string.IsNullOrWhiteSpace(settings.DefaultModel) ? null : settings.DefaultModel,
            ["serviceTier"] = NormalizeServiceTier(settings.ServiceTier),
            ["personality"] = "pragmatic"
        };

        if (includeInitialTurnsPage)
        {
            result["excludeTurns"] = true;
            result["initialTurnsPage"] = new JObject
            {
                ["limit"] = MaxInitialThreadTurnsToLoad,
                ["sortDirection"] = "desc",
                ["itemsView"] = "full"
            };
        }

        return (JObject)CodexRuntimeIdentityContext.EnrichRequest(
            "thread/resume",
            result,
            settings.DefaultModel,
            settings.ReasoningEffort)!;
    }

    private static JObject? BuildCollaborationMode(CodexExtensionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DefaultModel))
        {
            return null;
        }

        var modeSettings = new JObject
        {
            ["model"] = settings.DefaultModel
        };

        AddIfNotBlank(modeSettings, "reasoning_effort", settings.ReasoningEffort);

        // Default must be sent explicitly, otherwise an existing thread can stay in Plan mode.
        return new JObject
        {
            ["mode"] = settings.PlanModeEnabled ? "plan" : "default",
            ["settings"] = modeSettings
        };
    }

    private static void AddIfNotBlank(JObject target, string propertyName, string? value)
    {
        value = (value ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[propertyName] = value;
        }
    }

    private static object BuildSandboxPolicy(string sandboxMode)
    {
        switch (NormalizeSandboxMode(sandboxMode))
        {
            case "read-only":
                return new
                {
                    type = "readOnly",
                    networkAccess = false
                };

            case "workspace-write":
                return new
                {
                    type = "workspaceWrite",
                    networkAccess = false
                };

            default:
                return new
                {
                    type = "dangerFullAccess"
                };
        }
    }

    internal object[] BuildUserInput(string prompt, CodexExtensionSettings settings, string workingDirectory, IEnumerable<string> imagePaths, string ideContextSummary)
    {
        _ = settings;
        var inputs = new List<object>();

        inputs.Add(new
        {
            type = "text",
            text = BuildPromptText(prompt, ideContextSummary, preferredMcpContext: string.Empty)
        });

        foreach (var mention in ExtractMentionInputs(prompt, workingDirectory))
        {
            inputs.Add(mention);
        }

        foreach (var skill in ExtractSkillInputs(prompt))
        {
            inputs.Add(skill);
        }

        foreach (var imagePath in imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            inputs.Add(new { type = "localImage", path = Path.GetFullPath(imagePath) });
        }

        var normalized = (JObject)CodexImageInputInliner.InlineImages("turn/start",
            new JObject { ["input"] = JArray.FromObject(inputs) }, workingDirectory)!;
        return ((JArray)normalized["input"]!).Cast<object>().ToArray();
    }

    internal static string BuildPromptText(string prompt, string ideContextSummary, string preferredMcpContext)
    {
        // Match the official renderer/host contract: IDE state is exposed through
        // structured RPCs and explicit file/selection mentions, never concatenated
        // into the user's text request. Keep the context parameters for backward
        // compatibility with the legacy caller, but deliberately do not serialize them.
        _ = ideContextSummary;
        _ = preferredMcpContext;
        return prompt;
    }

    internal static string ExtractPromptRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var markerIndex = text!.LastIndexOf(PromptRequestBegin, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return text.Trim();
        }

        return text.Substring(markerIndex + PromptRequestBegin.Length).Trim();
    }

    private void PublishRateLimitsUpdate(JObject? parameters)
    {
        RateLimitsUpdated?.Invoke(BuildRateLimitSummary(parameters));
    }

    private CodexRateLimitSummary BuildRateLimitSummary(JToken? response)
    {
        var snapshots = ResolveRateLimitSnapshots(response);
        if (snapshots.Count == 0)
        {
            return new CodexRateLimitSummary();
        }

        var localization = new LocalizationService(_languageOverride);

        return new CodexRateLimitSummary
        {
            Entries = BuildRateLimitEntries(snapshots, localization)
        };
    }

    private static IReadOnlyList<JObject> ResolveRateLimitSnapshots(JToken? response)
    {
        var snapshots = new List<JObject>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendRateLimitSnapshotsFromMap(snapshots, seenKeys, response?["rateLimitsByLimitId"]);
        AppendRateLimitSnapshotsFromMap(snapshots, seenKeys, response?["rate_limits_by_limit_id"]);
        AppendRateLimitSnapshotsFromMap(snapshots, seenKeys, response?["account"]?["rateLimitsByLimitId"]);
        AppendRateLimitSnapshotsFromMap(snapshots, seenKeys, response?["account"]?["rate_limits_by_limit_id"]);

        AddRateLimitSnapshot(snapshots, seenKeys, response?["rateLimits"]);
        AddRateLimitSnapshot(snapshots, seenKeys, response?["rate_limits"]);
        AddRateLimitSnapshot(snapshots, seenKeys, response?["account"]?["rateLimits"]);
        AddRateLimitSnapshot(snapshots, seenKeys, response?["account"]?["rate_limits"]);
        AddRateLimitSnapshot(snapshots, seenKeys, response);

        return snapshots;
    }

    private static void AppendRateLimitSnapshotsFromMap(List<JObject> snapshots, HashSet<string> seenKeys, JToken? token)
    {
        if (token is not JObject snapshotMap)
        {
            return;
        }

        foreach (var property in snapshotMap.Properties())
        {
            AddRateLimitSnapshot(snapshots, seenKeys, property.Value, property.Name);
        }
    }

    private static void AddRateLimitSnapshot(List<JObject> snapshots, HashSet<string> seenKeys, JToken? token, string? fallbackKey = null)
    {
        if (token is not JObject snapshot || !LooksLikeRateLimitSnapshot(snapshot))
        {
            return;
        }

        var identity = ReadString(snapshot, "limitId", "limit_id", "limitName", "limit_name")
            ?? fallbackKey
            ?? NewtonsoftJsonCompatibility.Serialize(snapshot, Formatting.None);

        if (seenKeys.Add(identity))
        {
            snapshots.Add(snapshot);
        }
    }

    private static bool LooksLikeRateLimitSnapshot(JObject snapshot)
    {
        return snapshot["primary"] is not null
            || snapshot["primaryWindow"] is not null
            || snapshot["primary_window"] is not null
            || snapshot["secondary"] is not null
            || snapshot["secondaryWindow"] is not null
            || snapshot["secondary_window"] is not null
            || snapshot["weekly"] is not null
            || snapshot["weeklyWindow"] is not null
            || snapshot["weekly_window"] is not null
            || snapshot["daily"] is not null
            || snapshot["dailyWindow"] is not null
            || snapshot["daily_window"] is not null
            || snapshot["windows"] is not null
            || snapshot["credits"] is not null
            || snapshot["creditBalance"] is not null
            || snapshot["credit_balance"] is not null
            || snapshot["planType"] is not null
            || snapshot["plan_type"] is not null
            || snapshot["limitId"] is not null
            || snapshot["limit_id"] is not null
            || snapshot["limitName"] is not null
            || snapshot["limit_name"] is not null;
    }

    private static IReadOnlyList<CodexRateLimitWindowSummary> BuildRateLimitEntries(
        IReadOnlyList<JObject> snapshots,
        LocalizationService localization)
    {
        var entries = new List<CodexRateLimitWindowSummary>();

        foreach (var snapshot in snapshots
                     .OrderBy(GetRateLimitSortPriority)
                     .ThenBy(GetRateLimitDisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var limitDisplayName = GetRateLimitDisplayName(snapshot);
            var primary = BuildRateLimitWindowEntry(
                limitDisplayName,
                ResolveRateLimitWindow(snapshot, "primary", "primaryWindow", "primary_window", "short", "daily", "dailyWindow", "daily_window", "day", "5h"),
                localization);
            if (primary.HasData)
            {
                entries.Add(primary);
            }

            var secondary = BuildRateLimitWindowEntry(
                limitDisplayName,
                ResolveRateLimitWindow(snapshot, "secondary", "secondaryWindow", "secondary_window", "weekly", "weeklyWindow", "weekly_window", "week", "long", "7d"),
                localization);
            if (secondary.HasData)
            {
                entries.Add(secondary);
            }
        }

        var planSnapshot = snapshots.FirstOrDefault(snapshot => !string.IsNullOrWhiteSpace(ReadString(snapshot, "planType", "plan_type", "plan", "tier")));
        if (planSnapshot is not null)
        {
            var planEntry = BuildPlanEntry(planSnapshot, localization);
            if (planEntry.HasData)
            {
                entries.Add(planEntry);
            }
        }

        var creditsSnapshot = snapshots.FirstOrDefault(snapshot => ResolveCreditsToken(snapshot) is not null);
        if (creditsSnapshot is not null)
        {
            var creditsEntry = BuildCreditsEntry(ResolveCreditsToken(creditsSnapshot), localization);
            if (creditsEntry.HasData)
            {
                entries.Add(creditsEntry);
            }
        }

        return entries;
    }

    private static int GetRateLimitSortPriority(JObject snapshot)
    {
        var rawName = ReadString(snapshot, "limitName", "limit_name", "limitId", "limit_id");
        return string.IsNullOrWhiteSpace(rawName) || string.Equals(rawName, "codex", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }

    private static string GetRateLimitDisplayName(JObject snapshot)
    {
        var rawName = ReadString(snapshot, "limitName", "limit_name", "limitId", "limit_id");
        if (string.IsNullOrWhiteSpace(rawName)
            || string.Equals(rawName, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return rawName!.Replace('_', ' ').Trim();
    }

    private static JToken? ResolveRateLimitWindow(JObject snapshot, params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            if (snapshot[key] is JToken direct)
            {
                return direct;
            }
        }

        if (snapshot["windows"] is JArray windows)
        {
            var windowObjects = windows.OfType<JObject>().ToList();
            foreach (var key in candidateKeys)
            {
                var match = windowObjects
                    .FirstOrDefault(window => MatchesRateLimitWindow(window, key));
                if (match is not null)
                {
                    return match;
                }
            }

            if (candidateKeys.Contains("primary", StringComparer.OrdinalIgnoreCase))
            {
                return SelectWindowByDuration(windowObjects, preferLongest: false) ?? windows.FirstOrDefault();
            }

            if (candidateKeys.Contains("secondary", StringComparer.OrdinalIgnoreCase))
            {
                return windowObjects.Count > 1
                    ? SelectWindowByDuration(windowObjects, preferLongest: true) ?? windows.Skip(1).FirstOrDefault()
                    : windows.Skip(1).FirstOrDefault();
            }
        }

        return null;
    }

    private static bool MatchesRateLimitWindow(JObject window, string key)
    {
        var normalizedKey = NormalizeRateLimitText(key);
        var text = ReadString(window, "name", "title", "id", "kind", "type", "window", "windowType", "window_type", "period", "label") ?? string.Empty;
        var normalizedText = NormalizeRateLimitText(text);

        if (!string.IsNullOrWhiteSpace(normalizedText)
            && (normalizedText.Contains(normalizedKey) || normalizedKey.Contains(normalizedText)))
        {
            return true;
        }

        var durationMinutes = ReadDurationMinutes(window);
        if (IsSecondaryRateLimitKey(key))
        {
            return IsWeeklyRateLimitText(text)
                || durationMinutes >= 10080;
        }

        if (IsPrimaryRateLimitKey(key))
        {
            return IsShortRateLimitText(text)
                || (durationMinutes.HasValue && durationMinutes.Value > 0 && durationMinutes.Value < 10080);
        }

        return false;
    }

    private static JObject? SelectWindowByDuration(IEnumerable<JObject> windows, bool preferLongest)
    {
        var candidates = windows
            .Select(window => new { Window = window, DurationMinutes = ReadDurationMinutes(window) })
            .Where(item => item.DurationMinutes.HasValue && item.DurationMinutes.Value > 0);

        return preferLongest
            ? candidates.OrderByDescending(item => item.DurationMinutes.GetValueOrDefault()).FirstOrDefault()?.Window
            : candidates.OrderBy(item => item.DurationMinutes.GetValueOrDefault()).FirstOrDefault()?.Window;
    }

    private static bool IsPrimaryRateLimitKey(string key)
    {
        var normalized = NormalizeRateLimitText(key);
        return normalized.Contains("primary")
            || normalized.Contains("short")
            || normalized.Contains("daily")
            || normalized.Equals("day", StringComparison.Ordinal)
            || normalized.Equals("5h", StringComparison.Ordinal);
    }

    private static bool IsSecondaryRateLimitKey(string key)
    {
        var normalized = NormalizeRateLimitText(key);
        return normalized.Contains("secondary")
            || normalized.Contains("long")
            || normalized.Contains("weekly")
            || normalized.Equals("week", StringComparison.Ordinal)
            || normalized.Equals("7d", StringComparison.Ordinal);
    }

    private static bool IsShortRateLimitText(string value)
    {
        var normalized = NormalizeRateLimitText(value);
        return normalized.Contains("primary")
            || normalized.Contains("short")
            || normalized.Contains("daily")
            || normalized.Equals("day", StringComparison.Ordinal)
            || normalized.Contains("5h");
    }

    private static bool IsWeeklyRateLimitText(string value)
    {
        var normalized = NormalizeRateLimitText(value);
        return normalized.Contains("secondary")
            || normalized.Contains("weekly")
            || normalized.Contains("week")
            || normalized.Contains("7d")
            || normalized.Contains("long");
    }

    private static string NormalizeRateLimitText(string value)
    {
        return (value ?? string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    private static CodexRateLimitWindowSummary BuildRateLimitWindowEntry(string limitDisplayName, JToken? window, LocalizationService localization)
    {
        if (window is not JObject windowObject)
        {
            return new CodexRateLimitWindowSummary();
        }

        var title = BuildRateLimitWindowTitle(limitDisplayName, windowObject, localization);
        var usedPercent = ReadPercent(
            windowObject,
            "usedPercent",
            "used_percent",
            "usagePercent",
            "usage_percent",
            "usedPercentage",
            "used_percentage",
            "usagePercentage",
            "usage_percentage",
            "usedPercentOfLimit",
            "used_percent_of_limit",
            "usagePercentOfLimit",
            "usage_percent_of_limit",
            "usedRatio",
            "used_ratio",
            "usageRatio",
            "usage_ratio",
            "usedFraction",
            "used_fraction",
            "usageFraction",
            "usage_fraction");
        var remainingPercent = ReadPercent(
            windowObject,
            "remainingPercent",
            "remaining_percent",
            "remainingPercentage",
            "remaining_percentage",
            "percentRemaining",
            "percent_remaining",
            "remainingPercentOfLimit",
            "remaining_percent_of_limit",
            "percentRemainingOfLimit",
            "percent_remaining_of_limit",
            "remainingRatio",
            "remaining_ratio",
            "remainingFraction",
            "remaining_fraction");
        var resetsAt = ReadResetTime(windowObject);
        var remaining = ReadDecimal(
            windowObject,
            "remaining",
            "remainingRequests",
            "remaining_requests",
            "remainingTokens",
            "remaining_tokens",
            "remainingMessages",
            "remaining_messages",
            "remainingUses",
            "remaining_uses",
            "remainingUnits",
            "remaining_units",
            "remainingAmount",
            "remaining_amount",
            "balance");
        var used = ReadDecimal(
            windowObject,
            "used",
            "usedRequests",
            "used_requests",
            "usedTokens",
            "used_tokens",
            "usedMessages",
            "used_messages",
            "usedUses",
            "used_uses",
            "usedUnits",
            "used_units",
            "usedAmount",
            "used_amount",
            "consumed",
            "consumedTokens",
            "consumed_tokens");
        var limit = ReadDecimal(
            windowObject,
            "limit",
            "total",
            "max",
            "quota",
            "limitAmount",
            "limit_amount",
            "totalLimit",
            "total_limit",
            "maxAmount",
            "max_amount",
            "quotaLimit",
            "quota_limit");
        if (!remaining.HasValue && used.HasValue && limit.HasValue)
        {
            remaining = Math.Max(0m, limit.Value - used.Value);
        }

        var effectiveRemainingPercent = remainingPercent
            ?? (usedPercent.HasValue ? Math.Max(0d, 100d - usedPercent.Value) : null);
        if (!effectiveRemainingPercent.HasValue && remaining.HasValue && limit.HasValue && limit.Value > 0m)
        {
            effectiveRemainingPercent = (double)Math.Max(0m, Math.Min(100m, remaining.Value / limit.Value * 100m));
        }

        var detailParts = new List<string>();

        if (remaining.HasValue || limit.HasValue)
        {
            if (remaining.HasValue && limit.HasValue)
            {
                detailParts.Add(
                    remaining.Value.ToString("0.##", localization.Culture)
                    + " / "
                    + limit.Value.ToString("0.##", localization.Culture));
            }
            else
            {
                var value = remaining ?? limit;
                detailParts.Add(value?.ToString("0.##", localization.Culture) ?? string.Empty);
            }
        }

        if (effectiveRemainingPercent.HasValue)
        {
            detailParts.Add(Math.Round(effectiveRemainingPercent.Value).ToString("0", localization.Culture) + "% " + localization.RateLimitRemainingSuffix);
        }

        if (resetsAt.HasValue)
        {
            detailParts.Add(localization.RateLimitResetsPrefix + " " + resetsAt.Value.ToLocalTime().ToString("g", localization.Culture));
        }

        return new CodexRateLimitWindowSummary
        {
            Title = title?.Trim() ?? string.Empty,
            Detail = string.Join("   ", detailParts.Where(part => !string.IsNullOrWhiteSpace(part)))
        };
    }

    private static string BuildRateLimitWindowTitle(string limitDisplayName, JObject window, LocalizationService localization)
    {
        var explicitTitle = ReadString(window, "title", "name", "label", "window", "windowType", "window_type", "period");
        var windowLabel = !string.IsNullOrWhiteSpace(explicitTitle) && !IsGenericRateLimitWindowName(explicitTitle!)
            ? explicitTitle!.Trim()
            : BuildRateLimitWindowDurationLabel(ReadDurationMinutes(window), localization);

        if (string.IsNullOrWhiteSpace(limitDisplayName))
        {
            return windowLabel;
        }

        if (string.IsNullOrWhiteSpace(windowLabel))
        {
            return limitDisplayName;
        }

        return limitDisplayName + " " + windowLabel;
    }

    private static bool IsGenericRateLimitWindowName(string value)
    {
        return string.Equals(value, "primary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "secondary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "short", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "long", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "weekly", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "week", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "daily", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "day", StringComparison.OrdinalIgnoreCase);
    }

    private static CodexRateLimitWindowSummary BuildPlanEntry(JObject snapshot, LocalizationService localization)
    {
        var planType = ReadString(snapshot, "planType", "plan_type", "plan", "tier");
        return string.IsNullOrWhiteSpace(planType)
            ? new CodexRateLimitWindowSummary()
            : new CodexRateLimitWindowSummary
            {
                Title = localization.PlanLabelShort,
                Detail = planType!
            };
    }

    private static CodexRateLimitWindowSummary BuildCreditsEntry(JToken? credits, LocalizationService localization)
    {
        if (credits is not JObject creditsObject)
        {
            return new CodexRateLimitWindowSummary();
        }

        var hasCredits = ReadBoolean(creditsObject, "hasCredits", "has_credits");
        var unlimited = ReadBoolean(creditsObject, "unlimited");
        var balance = ReadDecimal(creditsObject, "balance", "available", "available_credits");

        if (unlimited == true)
        {
            return new CodexRateLimitWindowSummary
            {
                Title = localization.CreditsLabel,
                Detail = localization.RateLimitUnlimitedLabel
            };
        }

        if (balance.HasValue)
        {
            return new CodexRateLimitWindowSummary
            {
                Title = localization.CreditsLabel,
                Detail = balance.Value.ToString("0.##", localization.Culture)
            };
        }

        return hasCredits == true
            ? new CodexRateLimitWindowSummary
            {
                Title = localization.CreditsLabel
            }
            : new CodexRateLimitWindowSummary();
    }

    private static JToken? ResolveCreditsToken(JObject snapshot)
    {
        return snapshot["credits"]
            ?? snapshot["creditBalance"]
            ?? snapshot["credit_balance"];
    }

    private static double? ReadPercent(JObject source, params string[] keys)
    {
        foreach (var key in keys)
        {
            var token = source[key];
            if (token is null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                continue;
            }

            double? value = null;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double?>();
            }
            else
            {
                var text = token.Value<string>();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantValue)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentUICulture, out invariantValue))
                {
                    value = invariantValue;
                }
            }

            if (!value.HasValue)
            {
                continue;
            }

            return IsRateLimitRatioKey(key) && value.Value <= 1d
                ? value.Value * 100d
                : value.Value;
        }

        return null;
    }

    private static bool IsRateLimitRatioKey(string key)
    {
        var normalized = NormalizeRateLimitText(key);
        return normalized.Contains("ratio")
            || normalized.Contains("fraction");
    }

    private static decimal? ReadDecimal(JToken? source, params string[] keys)
    {
        if (source is not JObject sourceObject)
        {
            return null;
        }

        foreach (var key in keys)
        {
            var token = sourceObject[key];
            if (token is null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                continue;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return token.Value<decimal?>();
            }

            var text = token.Value<string>();
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentUICulture, out invariantValue))
            {
                return invariantValue;
            }
        }

        return null;
    }

    private static bool? ReadBoolean(JToken? source, params string[] keys)
    {
        if (source is not JObject sourceObject)
        {
            return null;
        }

        foreach (var key in keys)
        {
            var token = sourceObject[key];
            if (token is null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                continue;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            var text = token.Value<string>();
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? ReadString(JToken? source, params string[] keys)
    {
        if (source is not JObject sourceObject)
        {
            return null;
        }

        foreach (var key in keys)
        {
            var value = sourceObject[key]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadResetTime(JObject source)
    {
        var unixSeconds = source["resetsAt"]?.Value<long?>()
            ?? source["resets_at"]?.Value<long?>()
            ?? source["resetAt"]?.Value<long?>()
            ?? source["reset_at"]?.Value<long?>();
        if (unixSeconds.HasValue && unixSeconds.Value > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value);
        }

        var resetText = source["resetsAt"]?.Value<string>()
            ?? source["resets_at"]?.Value<string>()
            ?? source["resetAt"]?.Value<string>()
            ?? source["reset_at"]?.Value<string>();
        if (DateTimeOffset.TryParse(resetText, out var resetAt))
        {
            return resetAt;
        }

        return null;
    }

    private static int? ReadDurationMinutes(JObject source)
    {
        var duration = ReadDecimal(
            source,
            "windowDurationMins",
            "window_duration_mins",
            "durationMinutes",
            "duration_minutes",
            "windowMinutes",
            "window_minutes",
            "durationMins",
            "duration_mins",
            "minutes");
        if (duration.HasValue)
        {
            return (int)Math.Round(duration.Value, MidpointRounding.AwayFromZero);
        }

        var durationText = ReadString(
            source,
            "windowDuration",
            "window_duration",
            "duration",
            "period",
            "window",
            "windowType",
            "window_type",
            "name",
            "title",
            "label");
        return ParseDurationMinutes(durationText);
    }

    private static int? ParseDurationMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeRateLimitText(value!);
        if (normalized.Contains("weekly") || normalized.Contains("week"))
        {
            return 10080;
        }

        if (normalized.Contains("daily") || normalized.Equals("day", StringComparison.Ordinal))
        {
            return 1440;
        }

        var match = RateLimitDurationRegex.Match(value!);
        if (!match.Success
            || !decimal.TryParse(match.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var minutes = unit switch
        {
            "w" or "week" or "weeks" => amount * 10080m,
            "d" or "day" or "days" => amount * 1440m,
            "h" or "hr" or "hrs" or "hour" or "hours" => amount * 60m,
            _ => amount
        };

        return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    private static string BuildRateLimitWindowDurationLabel(int? durationMinutes, LocalizationService localization)
    {
        if (!durationMinutes.HasValue || durationMinutes.Value <= 0)
        {
            return string.Empty;
        }

        if (durationMinutes.Value == 10080)
        {
            return localization.RateLimitWeeklyLabel;
        }

        if (durationMinutes.Value >= 60 && durationMinutes.Value % 60 == 0)
        {
            return (durationMinutes.Value / 60) + "h";
        }

        return durationMinutes.Value + "m";
    }

    private IEnumerable<object> ExtractMentionInputs(string prompt, string workingDirectory)
    {
        foreach (Match match in MentionRegex.Matches(prompt ?? string.Empty))
        {
            var rawValue = (match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["value"].Value)
                .Trim();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            if (rawValue.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
            {
                yield return new
                {
                    type = "mention",
                    name = rawValue,
                    path = rawValue
                };

                continue;
            }

            var candidatePath = ResolveMentionPath(rawValue, workingDirectory);
            if (candidatePath is null)
            {
                continue;
            }

            yield return new
            {
                type = "mention",
                name = Path.GetFileName(candidatePath),
                path = candidatePath
            };
        }
    }

    private IEnumerable<object> ExtractSkillInputs(string prompt)
    {
        Dictionary<string, string> skillsSnapshot;
        lock (_syncRoot)
        {
            skillsSnapshot = new Dictionary<string, string>(_skillsByName, StringComparer.OrdinalIgnoreCase);
        }

        foreach (Match match in SkillRegex.Matches(prompt ?? string.Empty))
        {
            var skillName = match.Groups["value"].Value.Trim();
            if (!skillsSnapshot.TryGetValue(skillName, out var skillPath))
            {
                continue;
            }

            yield return new
            {
                type = "skill",
                name = skillName,
                path = skillPath
            };
        }
    }

    private static string? ResolveMentionPath(string rawValue, string workingDirectory)
    {
        try
        {
            var candidate = Path.IsPathRooted(rawValue)
                ? rawValue
                : Path.Combine(workingDirectory, rawValue.Replace('/', Path.DirectorySeparatorChar));

            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string ExtractText(JToken? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var node in content.SelectTokens("$..text"))
        {
            var value = node.Value<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.Append(value);
            }
        }

        return builder.ToString();
    }

    private static JToken? GetNestedToken(JToken? token, params string[] path)
    {
        var current = token;
        foreach (var segment in path)
        {
            if (current is not JObject obj)
            {
                return null;
            }

            current = obj[segment];
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static string? GetNestedString(JToken? token, params string[] path)
    {
        var current = GetNestedToken(token, path);
        if (current is null)
        {
            return null;
        }

        return current.Type switch
        {
            JTokenType.String => current.Value<string>(),
            JTokenType.Null => null,
            JTokenType.Undefined => null,
            _ => NewtonsoftJsonCompatibility.Serialize(current, Formatting.None)
        };
    }

    internal static string ExtractAppServerErrorMessage(JToken? error, string fallbackMessage)
    {
        if (error is null || error.Type == JTokenType.Null || error.Type == JTokenType.Undefined)
        {
            return fallbackMessage;
        }

        var nestedMessage = GetNestedString(error, "message");
        if (!string.IsNullOrWhiteSpace(nestedMessage))
        {
            return nestedMessage!;
        }

        if (error is JValue value)
        {
            var scalarMessage = value.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(scalarMessage))
            {
                return scalarMessage!;
            }
        }

        var serialized = NewtonsoftJsonCompatibility.Serialize(error, Formatting.None);
        return string.IsNullOrWhiteSpace(serialized) ? fallbackMessage : serialized;
    }

    internal static int? ExtractAppServerErrorCode(JToken? error)
    {
        if (error is not JObject errorObject)
        {
            return null;
        }

        if (errorObject["code"] is not JValue code
            || code.Type == JTokenType.Null
            || code.Type == JTokenType.Undefined)
        {
            return null;
        }

        var rawCode = Convert.ToString(code.Value, CultureInfo.InvariantCulture);
        return int.TryParse(rawCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private void NotifyThreadCatalogChanged()
    {
        try
        {
            ThreadCatalogChanged?.Invoke();
        }
        catch
        {
        }
    }

    private static CodexThreadSummary? ParseThreadSummary(JToken? thread, string? activeThreadId)
    {
        var threadId = thread?["id"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var preview = (thread?["preview"]?.Value<string>() ?? string.Empty).Trim();
        var name = thread?["name"]?.Value<string>();
        var sessionPath = thread?["path"]?.Value<string>();
        var updatedAt = thread?["updatedAt"]?.Value<long?>() ?? 0L;
        var status = GetNestedString(thread, "status", "type") ?? string.Empty;
        var sanitizedPreview = TryReadThreadTitleFromSession(sessionPath);
        if (string.IsNullOrWhiteSpace(sanitizedPreview))
        {
            sanitizedPreview = SanitizeThreadPreview(preview);
        }

        return new CodexThreadSummary
        {
            ThreadId = threadId!,
            Name = name ?? string.Empty,
            Preview = string.IsNullOrWhiteSpace(sanitizedPreview)
                ? (string.IsNullOrWhiteSpace(name) ? threadId! : string.Empty)
                : sanitizedPreview!,
            UpdatedAt = updatedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(updatedAt).ToLocalTime() : DateTimeOffset.MinValue,
            Status = status,
            IsActive = string.Equals(threadId, activeThreadId, StringComparison.Ordinal)
        };
    }

    private static string TryReadThreadTitleFromSession(string? sessionPath)
    {
        if (string.IsNullOrWhiteSpace(sessionPath) || !File.Exists(sessionPath))
        {
            return string.Empty;
        }

        try
        {
            using var reader = new StreamReader(sessionPath);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = JObject.Parse(line);
                var userMessagePrompt = ExtractPromptFromSessionEvent(entry);
                if (!string.IsNullOrWhiteSpace(userMessagePrompt))
                {
                    return BuildSummary(userMessagePrompt, userMessagePrompt);
                }

                if (!string.Equals(entry["type"]?.Value<string>(), "response_item", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = entry["payload"];
                if (!string.Equals(payload?["type"]?.Value<string>(), "message", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(payload?["role"]?.Value<string>(), "user", StringComparison.Ordinal))
                {
                    continue;
                }

                var prompt = ExtractPromptFromSessionMessage(payload?["content"] as JArray);
                return string.IsNullOrWhiteSpace(prompt) ? string.Empty : BuildSummary(prompt, prompt);
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string ExtractPromptFromSessionEvent(JObject entry)
    {
        if (!string.Equals(entry["type"]?.Value<string>(), "event_msg", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var payload = entry["payload"];
        if (!string.Equals(payload?["type"]?.Value<string>(), "user_message", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return SanitizeThreadPreview(payload?["message"]?.Value<string>() ?? string.Empty);
    }

    private static string ExtractPromptFromSessionMessage(JArray? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        var segments = new List<string>();
        foreach (var item in content)
        {
            if (!string.Equals(item?["type"]?.Value<string>(), "input_text", StringComparison.Ordinal))
            {
                continue;
            }

            var text = item?["text"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var visiblePrompt = NormalizeUserMessageTextSegment(text);
            if (string.IsNullOrWhiteSpace(visiblePrompt))
            {
                continue;
            }

            segments.Add(visiblePrompt);
        }

        return string.Join(" ", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string SanitizeThreadPreview(string preview)
    {
        var trimmed = preview?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (!ContainsInjectedContext(trimmed))
        {
            return BuildSummary(trimmed, trimmed);
        }

        var extractedPrompt = TryExtractPromptFromThreadPreview(trimmed);
        if (!string.IsNullOrWhiteSpace(extractedPrompt))
        {
            return BuildSummary(extractedPrompt, extractedPrompt);
        }

        return string.Empty;
    }

    private static bool ContainsInjectedContext(string text)
    {
        return ContainsAny(text, ExtensionContextPrefixes)
            || ContainsAny(text, IdeContextPrefixes)
            || ContainsAny(text, PreferredMcpPrefixes)
            || text.IndexOf(PromptRequestBegin, StringComparison.Ordinal) >= 0
            || text.IndexOf(IdeContextHeading, StringComparison.Ordinal) >= 0;
    }

    private static bool StartsWithAny(string text, IEnumerable<string> prefixes)
    {
        return prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool ContainsAny(string text, IEnumerable<string> prefixes)
    {
        return prefixes.Any(prefix => text.IndexOf(prefix, StringComparison.Ordinal) >= 0);
    }

    private static string[] CreateLocalizedSet(params Func<LocalizationService, string>[] selectors)
    {
        var languages = new[] { "pt-BR", "en", "es", "fr", "de" };
        return languages
            .SelectMany(language =>
            {
                var localization = new LocalizationService(language);
                return selectors.Select(selector => selector(localization));
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string TryExtractPromptFromThreadPreview(string preview)
    {
        var text = ExtractPromptRequest(preview);
        text = RemoveLeadingExtensionContext(text);
        text = RemoveLeadingPreferredMcpContext(text);
        text = RemoveLeadingIdeContextPrefix(text);

        while (TryStripLeadingIdeContextLine(ref text))
        {
        }

        if (StartsWithIdeContextLine(text))
        {
            var trailingPrompt = ExtractPromptFromTrailingContextLine(text);
            return CompactSingleLine(trailingPrompt);
        }

        return CompactSingleLine(text);
    }

    private static string RemoveLeadingExtensionContext(string text)
    {
        foreach (var prefix in ExtensionContextPrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var closingQuoteIndex = text.IndexOf('"', prefix.Length);
            if (closingQuoteIndex < 0)
            {
                return string.Empty;
            }

            var nextIndex = closingQuoteIndex + 1;
            if (nextIndex < text.Length && text[nextIndex] == '.')
            {
                nextIndex++;
            }

            return text.Substring(nextIndex).TrimStart();
        }

        return text;
    }

    private static string RemoveLeadingPreferredMcpContext(string text)
    {
        foreach (var prefix in PreferredMcpPrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var sentenceEndIndex = text.IndexOf('.');
            if (sentenceEndIndex < 0)
            {
                return string.Empty;
            }

            return text.Substring(sentenceEndIndex + 1).TrimStart();
        }

        return text;
    }

    private static string RemoveLeadingIdeContextPrefix(string text)
    {
        foreach (var prefix in IdeContextPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return text.Substring(prefix.Length).TrimStart();
            }
        }

        return text;
    }

    private static bool TryStripLeadingIdeContextLine(ref string text)
    {
        foreach (var prefix in IdeContextLinePrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var newlineIndex = text.IndexOf('\n');
            if (newlineIndex < 0)
            {
                return false;
            }

            text = text.Substring(newlineIndex + 1).TrimStart();
            return true;
        }

        return false;
    }

    private static bool StartsWithIdeContextLine(string text)
    {
        return IdeContextLinePrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string ExtractPromptFromTrailingContextLine(string text)
    {
        foreach (var prefix in IdeContextLinePrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = text.Substring(prefix.Length).TrimStart();
            var match = TrailingPromptAfterPathRegex.Match(remainder);
            if (match.Success)
            {
                return match.Groups["prompt"].Value.Trim();
            }

            return string.Empty;
        }

        return text;
    }

    private static JToken? BuildThreadWithInitialTurnsPage(JToken? thread, JToken? initialTurnsPage)
    {
        if (thread is null)
        {
            return null;
        }

        var clonedThread = thread.DeepClone();
        if (clonedThread is not JObject threadObject)
        {
            return clonedThread;
        }

        if (initialTurnsPage?["data"] is JArray initialTurns)
        {
            threadObject["turns"] = new JArray(initialTurns.Reverse().Select(turn => turn.DeepClone()));
            return threadObject;
        }

        if (threadObject["turns"] is JArray turns && turns.Count > MaxInitialThreadTurnsToLoad)
        {
            threadObject["turns"] = new JArray(
                turns
                    .Skip(turns.Count - MaxInitialThreadTurnsToLoad)
                    .Select(turn => turn.DeepClone()));
        }

        return threadObject;
    }

    private static IReadOnlyList<ChatMessage> ParseThreadMessages(JToken thread, string? threadId, string? sessionPath)
    {
        var messages = new List<ChatMessage>();
        var turns = thread["turns"] as JArray;
        if (turns is null)
        {
            return ReadMessagesFromSession(sessionPath);
        }

        var fallbackPrompts = ReadPromptHistoryForThread(threadId);
        var fallbackPromptIndex = 0;

        foreach (var turn in turns)
        {
            var items = turn["items"] as JArray;
            if (items is null)
            {
                continue;
            }

            var turnMessages = new List<ChatMessage>();
            foreach (var item in items)
            {
                var message = ParseThreadMessage(item);
                if (message is not null)
                {
                    turnMessages.Add(message);
                }
            }

            if (!turnMessages.Any(message => message.IsUser) && fallbackPromptIndex < fallbackPrompts.Count)
            {
                messages.Add(new ChatMessage(true, fallbackPrompts[fallbackPromptIndex]));
                fallbackPromptIndex++;
            }
            else if (turnMessages.Any(message => message.IsUser))
            {
                fallbackPromptIndex++;
            }

            messages.AddRange(turnMessages);
        }

        return messages.Count > 0 ? messages : ReadMessagesFromSession(sessionPath);
    }

    private static IReadOnlyList<ChatMessage> ReadMessagesFromSession(string? sessionPath)
    {
        if (string.IsNullOrWhiteSpace(sessionPath) || !File.Exists(sessionPath))
        {
            return Array.Empty<ChatMessage>();
        }

        var responseMessages = new List<ChatMessage>();
        var eventMessages = new List<ChatMessage>();
        try
        {
            foreach (var line in ReadRecentNonEmptyLines(sessionPath!, MaxSessionLinesToParse))
            {
                JObject entry;
                try
                {
                    entry = JObject.Parse(line);
                }
                catch
                {
                    continue;
                }

                var responseMessage = ParseSessionResponseMessage(entry);
                if (responseMessage is not null)
                {
                    responseMessages.Add(responseMessage);
                    continue;
                }

                var eventMessage = ParseSessionEventMessage(entry);
                if (eventMessage is not null)
                {
                    eventMessages.Add(eventMessage);
                }
            }
        }
        catch
        {
        }

        return responseMessages.Count > 0 ? responseMessages : eventMessages;
    }

    private static IReadOnlyList<string> ReadRecentNonEmptyLines(string path, int maxLines)
    {
        var lines = new Queue<string>();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            lines.Enqueue(line);
            while (lines.Count > maxLines)
            {
                lines.Dequeue();
            }
        }

        return lines.ToList();
    }

    private static ChatMessage? ParseSessionResponseMessage(JObject entry)
    {
        if (!string.Equals(entry["type"]?.Value<string>(), "response_item", StringComparison.Ordinal))
        {
            return null;
        }

        var payload = entry["payload"];
        if (!string.Equals(payload?["type"]?.Value<string>(), "message", StringComparison.Ordinal))
        {
            return null;
        }

        return ParseRoleMessage(payload);
    }

    private static ChatMessage? ParseSessionEventMessage(JObject entry)
    {
        if (!string.Equals(entry["type"]?.Value<string>(), "event_msg", StringComparison.Ordinal))
        {
            return null;
        }

        var payload = entry["payload"];
        var payloadType = payload?["type"]?.Value<string>();
        switch (payloadType)
        {
            case "user_message":
                var userText = NormalizeUserMessageTextSegment(payload?["message"]?.Value<string>());
                return string.IsNullOrWhiteSpace(userText) ? null : new ChatMessage(true, userText);

            case "agent_message":
                var agentText = payload?["message"]?.Value<string>();
                return string.IsNullOrWhiteSpace(agentText) ? null : new ChatMessage(false, agentText!);

            default:
                return null;
        }
    }

    private static string? FindSessionPathForThread(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var sessionsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "sessions");

        if (!Directory.Exists(sessionsRoot))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).IndexOf(threadId, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadPromptHistoryForThread(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return Array.Empty<string>();
        }

        var historyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "history.jsonl");

        if (!File.Exists(historyPath))
        {
            return Array.Empty<string>();
        }

        var prompts = new Queue<string>();
        try
        {
            using var reader = new StreamReader(historyPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JObject entry;
                try
                {
                    entry = JObject.Parse(line);
                }
                catch
                {
                    continue;
                }

                if (!string.Equals(entry["session_id"]?.Value<string>(), threadId, StringComparison.Ordinal))
                {
                    continue;
                }

                var prompt = NormalizeUserMessageTextSegment(entry["text"]?.Value<string>());
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    prompts.Enqueue(Truncate(prompt, MaxPromptHistoryFallbackEntryLength));
                    while (prompts.Count > MaxPromptHistoryPromptsPerThread)
                    {
                        prompts.Dequeue();
                    }
                }
            }
        }
        catch
        {
        }

        return prompts.ToList();
    }

    private static ChatMessage? ParseThreadMessage(JToken? item)
    {
        var itemType = item?["type"]?.Value<string>();
        switch (itemType)
        {
            case "message":
                return ParseRoleMessage(item);

            case "userMessage":
                var userText = ExtractUserMessageText(item?["content"]);
                if (string.IsNullOrWhiteSpace(userText))
                {
                    userText = ExtractUserMessageText(item?["text"] ?? item?["message"]);
                }

                return string.IsNullOrWhiteSpace(userText) ? null : new ChatMessage(true, userText);

            case "agentMessage":
                var agentText = ExtractAgentMessageText(item);
                return string.IsNullOrWhiteSpace(agentText) ? null : new ChatMessage(false, agentText);

            case "plan":
                return BuildThreadEventMessage(item);

            case "reasoning":
            case "commandExecution":
            case "fileChange":
            case "mcpToolCall":
            case "dynamicToolCall":
            case "collabAgentToolCall":
            case "webSearch":
            case "imageView":
            case "imageGeneration":
            case "enteredReviewMode":
            case "exitedReviewMode":
            case "contextCompaction":
                return null;

            default:
                return null;
        }
    }

    private static ChatMessage? ParseRoleMessage(JToken? item)
    {
        var role = item?["role"]?.Value<string>();
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            var userText = ExtractUserMessageText(item?["content"]);
            if (string.IsNullOrWhiteSpace(userText))
            {
                userText = ExtractUserMessageText(item?["text"] ?? item?["message"]);
            }

            return string.IsNullOrWhiteSpace(userText) ? null : new ChatMessage(true, userText);
        }

        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "agent", StringComparison.OrdinalIgnoreCase))
        {
            var agentText = ExtractAgentMessageText(item);
            return string.IsNullOrWhiteSpace(agentText) ? null : new ChatMessage(false, agentText);
        }

        return null;
    }

    private static string ExtractAgentMessageText(JToken? item)
    {
        var text = item?["text"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = ExtractText(item?["content"]);
        }

        return text ?? string.Empty;
    }

    private static string ExtractUserMessageText(JToken? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        if (content.Type == JTokenType.String)
        {
            return NormalizeUserMessageTextSegment(content.Value<string>());
        }

        if (content is JObject obj)
        {
            return ExtractUserMessageText(new JArray(obj));
        }

        if (content is not JArray items)
        {
            return string.Empty;
        }

        var segments = new List<string>();
        foreach (var item in items)
        {
            switch (item?["type"]?.Value<string>())
            {
                case "text":
                case "input_text":
                    var text = item?["text"]?.Value<string>();
                    var normalizedText = NormalizeUserMessageTextSegment(text);
                    if (!string.IsNullOrWhiteSpace(normalizedText))
                    {
                        segments.Add(normalizedText);
                    }
                    break;

                case "localImage":
                case "image":
                case "input_image":
                    segments.Add("[image]");
                    break;

                case "mention":
                    var mention = item?["name"]?.Value<string>() ?? item?["path"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(mention))
                    {
                        segments.Add("@" + mention);
                    }
                    break;

                case "skill":
                    var skill = item?["name"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(skill))
                    {
                        segments.Add("$" + skill);
                    }
                    break;

                default:
                    var fallbackText = NormalizeUserMessageTextSegment(item?["text"]?.Value<string>());
                    if (!string.IsNullOrWhiteSpace(fallbackText))
                    {
                        segments.Add(fallbackText);
                    }
                    break;
            }
        }

        return string.Join(" ", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    internal static string NormalizeUserMessageTextSegment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = ExtractPromptRequest(text);
        if (StartsWithAny(trimmed, ExtensionContextPrefixes)
            || StartsWithAny(trimmed, IdeContextPrefixes)
            || StartsWithAny(trimmed, PreferredMcpPrefixes)
            || StartsWithAny(trimmed, SyntheticUserContextPrefixes))
        {
            return string.Empty;
        }

        return trimmed;
    }

    private static ChatMessage? BuildThreadEventMessage(JToken? item)
    {
        var itemType = item?["type"]?.Value<string>();
        switch (itemType)
        {
            case "plan":
                var planText = NormalizeDetail(item?["text"]?.Value<string>(), maxLength: null);
                return CreatePlanEventMessage(planText);

            default:
                var localization = new LocalizationService();
                switch (itemType)
                {
                    case "reasoning":
                        var reasoningText = NormalizeDetail(JoinTextArray(item?["summary"]));
                        return CreateEventMessage(localization.EventReasoningTitle, BuildSummary(reasoningText, localization.EventReasoningUpdated), reasoningText);

                    case "commandExecution":
                        var command = item?["command"]?.Value<string>();
                        var status = item?["status"]?.Value<string>();
                        var exitCode = item?["exitCode"]?.Value<int?>();
                        var durationMs = item?["durationMs"]?.Value<long?>();
                        var aggregatedOutput = NormalizeDetail(item?["aggregatedOutput"]?.Value<string>());
                        var cwd = item?["cwd"]?.Value<string>();
                        return CreateEventMessage(
                            localization.EventCommandTitle,
                            BuildCommandSummary(command, status, exitCode, durationMs, localization),
                            BuildDetailSections(
                                string.IsNullOrWhiteSpace(cwd) ? null : localization.EventWorkingDirectoryLabel + Environment.NewLine + cwd!.Trim(),
                                string.IsNullOrWhiteSpace(aggregatedOutput) ? null : localization.EventOutputLabel + Environment.NewLine + aggregatedOutput));

                    case "fileChange":
                        var changes = item?["changes"] as JArray;
                        return CreateEventMessage(localization.EventFileChangesTitle, BuildFileChangeSummary(changes, localization), BuildFileChangeDetail(changes, localization));

                    case "mcpToolCall":
                        var server = item?["server"]?.Value<string>();
                        var tool = item?["tool"]?.Value<string>();
                        var toolLabel = string.IsNullOrWhiteSpace(server) ? tool : server + "." + tool;
                        var toolStatus = item?["status"]?.Value<string>();
                        var toolDurationMs = item?["durationMs"]?.Value<long?>();
                        var errorMessage = GetNestedString(item, "error", "message");
                        bool? mcpSuccess = null;
                        if (!string.IsNullOrWhiteSpace(errorMessage))
                        {
                            mcpSuccess = false;
                        }
                        else if (string.Equals(toolStatus, "completed", StringComparison.OrdinalIgnoreCase))
                        {
                            mcpSuccess = true;
                        }
                        else if (string.Equals(toolStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(toolStatus, "canceled", StringComparison.OrdinalIgnoreCase))
                        {
                            mcpSuccess = false;
                        }

                        return CreateEventMessage(
                            localization.EventMcpToolTitle,
                            BuildToolSummary(toolLabel, toolStatus, toolDurationMs, localization, mcpSuccess),
                            BuildDetailSections(
                                BuildNamedJsonBlock(localization.EventArgumentsLabel, item?["arguments"]),
                                string.IsNullOrWhiteSpace(errorMessage) ? null : localization.EventErrorLabel + Environment.NewLine + errorMessage!.Trim(),
                                BuildNamedTextBlock(localization.EventResultLabel, ExtractContentText(GetNestedToken(item, "result", "content")) ?? SerializeStructuredValue(item?["result"]))));

                    case "dynamicToolCall":
                        var dynamicTool = item?["tool"]?.Value<string>();
                        var success = item?["success"]?.Value<bool?>();
                        return CreateEventMessage(
                            localization.EventToolTitle,
                            BuildToolSummary(dynamicTool, item?["status"]?.Value<string>(), item?["durationMs"]?.Value<long?>(), localization, success),
                            BuildDetailSections(
                                BuildNamedJsonBlock(localization.EventArgumentsLabel, item?["arguments"]),
                                BuildNamedTextBlock(localization.EventOutputLabel, ExtractContentText(item?["contentItems"]) ?? SerializeStructuredValue(item?["contentItems"]))));

                    case "collabAgentToolCall":
                        var collabTool = item?["tool"]?.Value<string>();
                        var receiverIds = item?["receiverThreadIds"] is JArray receivers
                            ? string.Join(", ", receivers.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)))
                            : string.Empty;
                        return CreateEventMessage(
                            localization.EventAgentToolTitle,
                            BuildSummary(string.IsNullOrWhiteSpace(receiverIds) ? collabTool : collabTool + " -> " + receiverIds, localization.EventAgentToolUsed),
                            BuildDetailSections(
                                BuildNamedTextBlock(localization.EventPromptLabel, NormalizeDetail(item?["prompt"]?.Value<string>())),
                                BuildNamedJsonBlock(localization.EventArgumentsLabel, item?["arguments"])));

                    case "webSearch":
                        var query = item?["query"]?.Value<string>();
                        return CreateEventMessage(
                            localization.EventWebSearchTitle,
                            BuildSummary(query, localization.EventWebSearchTitle),
                            BuildNamedTextBlock(localization.EventResultLabel, ExtractContentText(GetNestedToken(item, "result", "content")) ?? SerializeStructuredValue(item?["result"])));

                    case "imageView":
                        return CreateEventMessage(localization.EventImageViewTitle, BuildSummary(item?["path"]?.Value<string>(), localization.EventImageViewed));

                    case "imageGeneration":
                        return CreateEventMessage(
                            localization.EventImageGenerationTitle,
                            BuildSummary(item?["status"]?.Value<string>(), localization.EventImageGenerated),
                            BuildNamedTextBlock(localization.EventPromptLabel, NormalizeDetail(item?["prompt"]?.Value<string>())));

                    case "enteredReviewMode":
                        return CreateEventMessage(localization.EventReviewModeTitle, BuildSummary(item?["review"]?.Value<string>(), localization.EventEnteredReviewMode));

                    case "exitedReviewMode":
                        return CreateEventMessage(localization.EventReviewModeTitle, BuildSummary(item?["review"]?.Value<string>(), localization.EventExitedReviewMode));

                    case "contextCompaction":
                        return CreateEventMessage(localization.EventContextTitle, localization.EventConversationContextCompacted);

                    default:
                        return null;
                }
        }
    }

    private static ChatMessage CreatePlanEventMessage(string? planText)
    {
        var localization = new LocalizationService();
        return CreateEventMessage(
            localization.EventPlanTitle,
            BuildSummary(planText, localization.EventPlanUpdated),
            planText,
            detailMaxLength: null,
            supportsMarkdownDetail: true)!;
    }

    private static ChatMessage? CreateEventMessage(
        string title,
        string? summary,
        string? detail = null,
        int? detailMaxLength = 2200,
        bool supportsMarkdownDetail = false)
    {
        var normalizedSummary = BuildSummary(summary, title);
        var normalizedDetail = NormalizeDetail(detail, detailMaxLength);
        if (string.Equals(normalizedSummary, CompactSingleLine(normalizedDetail), StringComparison.Ordinal))
        {
            normalizedDetail = null;
        }

        return new ChatMessage(
            false,
            normalizedSummary,
            isEvent: true,
            title: title,
            detail: normalizedDetail,
            supportsMarkdownText: false,
            supportsMarkdownDetail: supportsMarkdownDetail);
    }

    private static string? BuildStructuredPlanMarkdown(string? explanation, JArray? plan, LocalizationService localization)
    {
        var sections = new List<string>();
        var normalizedExplanation = NormalizeDetail(explanation, maxLength: null);
        if (!string.IsNullOrWhiteSpace(normalizedExplanation))
        {
            sections.Add(normalizedExplanation!);
        }

        if (plan is not null)
        {
            var items = new List<string>();
            foreach (var step in plan)
            {
                var stepText = NormalizeDetail(step?["step"]?.Value<string>(), maxLength: null);
                if (string.IsNullOrWhiteSpace(stepText))
                {
                    continue;
                }

                items.Add(FormatPlanStep(stepText!, step?["status"]?.Value<string>(), localization));
            }

            if (items.Count > 0)
            {
                sections.Add(string.Join(Environment.NewLine, items));
            }
        }

        return sections.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string FormatPlanStep(string stepText, string? status, LocalizationService localization)
    {
        return NormalizePlanStatus(status) switch
        {
            "completed" => "- [x] " + stepText,
            "inprogress" => "- [ ] **" + localization.EventInProgressStatus + ":** " + stepText,
            "pending" => "- [ ] " + stepText,
            _ => "- " + stepText
        };
    }

    private static string NormalizePlanStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return string.Empty;
        }

        return status!
            .Trim()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static string BuildCommandSummary(string? command, string? status, int? exitCode, long? durationMs, LocalizationService localization)
    {
        var parts = new List<string>();
        var compactCommand = CompactSingleLine(command);
        if (!string.IsNullOrWhiteSpace(compactCommand))
        {
            parts.Add(Truncate(compactCommand, 120));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add("[" + status!.Trim() + "]");
        }

        if (exitCode.HasValue)
        {
            parts.Add("exit " + exitCode.Value);
        }

        var durationSuffix = BuildDurationSuffix(durationMs);
        if (!string.IsNullOrWhiteSpace(durationSuffix))
        {
            parts.Add(durationSuffix);
        }

        return parts.Count == 0 ? localization.EventCommandExecuted : string.Join(" ", parts);
    }

    private static string BuildFileChangeSummary(JArray? changes, LocalizationService localization)
    {
        if (changes is null || changes.Count == 0)
        {
            return localization.EventUpdatedFiles;
        }

        var parts = new List<string>();
        foreach (var change in changes.Take(3))
        {
            var path = change?["path"]?.Value<string>();
            var kind = GetNestedString(change, "kind", "type");
            if (!string.IsNullOrWhiteSpace(path))
            {
                parts.Add(string.IsNullOrWhiteSpace(kind) ? path! : kind + " " + path);
            }
        }

        if (changes.Count > 3)
        {
            parts.Add(string.Format(localization.Culture, localization.EventMoreFormat, changes.Count - 3));
        }

        return string.Join(", ", parts);
    }

    private static string? BuildFileChangeDetail(JArray? changes, LocalizationService localization)
    {
        if (changes is null || changes.Count == 0)
        {
            return null;
        }

        var details = new List<string>();
        foreach (var change in changes.Take(6))
        {
            var path = change?["path"]?.Value<string>();
            var kind = GetNestedString(change, "kind", "type");
            var header = BuildSummary(string.IsNullOrWhiteSpace(kind) ? path : kind + " " + path, localization.EventFileUpdated);
            var diff = NormalizeDetail(change?["diff"]?.Value<string>());
            details.Add(string.IsNullOrWhiteSpace(diff) ? header : header + Environment.NewLine + Truncate(diff!, 700));
        }

        if (changes.Count > 6)
        {
            details.Add(string.Format(localization.Culture, localization.EventMoreFilesFormat, changes.Count - 6));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, details);
    }

    private static string JoinTextArray(JToken? token)
    {
        var values = token as JArray;
        if (values is null)
        {
            return string.Empty;
        }

        return string.Join(" ", values.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildToolSummary(string? label, string? status, long? durationMs, LocalizationService localization, bool? success = null)
    {
        var parts = new List<string>();
        var compactLabel = CompactSingleLine(label);
        if (!string.IsNullOrWhiteSpace(compactLabel))
        {
            parts.Add(Truncate(compactLabel, 120));
        }

        if (success.HasValue)
        {
            parts.Add(success.Value ? "[" + localization.EventCompletedStatus + "]" : "[" + localization.EventFailedStatus + "]");
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add("[" + status!.Trim() + "]");
        }

        var durationSuffix = BuildDurationSuffix(durationMs);
        if (!string.IsNullOrWhiteSpace(durationSuffix))
        {
            parts.Add(durationSuffix);
        }

        return parts.Count == 0 ? localization.EventToolCall : string.Join(" ", parts);
    }

    private static string BuildSummary(string? value, string fallback)
    {
        var compact = CompactSingleLine(value);
        return string.IsNullOrWhiteSpace(compact) ? fallback : Truncate(compact, 180);
    }

    private static string CompactSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value!.Length > MaxSummarySourceLength)
        {
            value = value.Substring(0, MaxSummarySourceLength);
        }

        var compact = Regex.Replace(value.Replace("\r", " ").Replace("\n", " "), @"\s+", " ");
        return compact.Trim();
    }

    private static string? NormalizeDetail(string? value, int? maxLength = 2200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value!.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return maxLength.HasValue ? Truncate(normalized, maxLength.Value) : normalized;
    }

    private static string? BuildNamedTextBlock(string label, string? value)
    {
        var normalized = NormalizeDetail(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : label + Environment.NewLine + normalized;
    }

    private static string? BuildNamedJsonBlock(string label, JToken? value)
    {
        var serialized = SerializeStructuredValue(value);
        return string.IsNullOrWhiteSpace(serialized) ? null : label + Environment.NewLine + serialized;
    }

    private static string? SerializeStructuredValue(JToken? value)
    {
        if (value is null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
        {
            return null;
        }

        if (value.Type == JTokenType.String)
        {
            return NormalizeDetail(value.Value<string>());
        }

        return NormalizeDetail(NewtonsoftJsonCompatibility.Serialize(value, Formatting.Indented));
    }

    private static string? ExtractContentText(JToken? content)
    {
        if (content is null)
        {
            return null;
        }

        var textParts = content.SelectTokens("$..text")
            .Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct()
            .ToList();

        if (textParts.Count == 0)
        {
            return null;
        }

        return NormalizeDetail(string.Join(Environment.NewLine, textParts));
    }

    private static string? BuildDetailSections(params string?[] sections)
    {
        var parts = sections
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .Select(section => section!.Trim())
            .ToList();

        return parts.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string BuildDurationSuffix(long? durationMs)
    {
        if (!durationMs.HasValue || durationMs.Value <= 0)
        {
            return string.Empty;
        }

        var duration = TimeSpan.FromMilliseconds(durationMs.Value);
        if (duration.TotalSeconds < 1)
        {
            return durationMs.Value + " ms";
        }

        if (duration.TotalMinutes < 1)
        {
            return duration.TotalSeconds.ToString("0.0") + " s";
        }

        if (duration.TotalHours < 1)
        {
            return duration.Minutes + "m " + duration.Seconds.ToString("00") + "s";
        }

        return ((int)duration.TotalHours) + "h " + duration.Minutes.ToString("00") + "m";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength - 3).TrimEnd() + "...";
    }

    private void PublishError(string text)
    {
        ActiveTurnState? turnState;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
        }

        turnState?.OnError(text);
    }

    private void FailPendingOperations(Process process, long generation, string message)
    {
        List<TaskCompletionSource<JToken?>> pendingRequests;
        ActiveTurnState? turnState;
        StreamWriter? input;

        lock (_syncRoot)
        {
            if (!ReferenceEquals(_serverProcess, process) || _serverGeneration != generation)
            {
                return;
            }

            var pendingIds = _pendingRequests
                .Where(pair => pair.Value.Generation == generation)
                .Select(pair => pair.Key)
                .ToList();
            pendingRequests = pendingIds.Select(id => _pendingRequests[id].Completion).ToList();
            foreach (var pendingId in pendingIds)
            {
                _pendingRequests.Remove(pendingId);
            }

            turnState = _activeTurn;
            input = _serverInput;
            _threadId = null;
            _threadConfigKey = null;
            _threadLoaded = false;
            _skillsCacheKey = null;
            _serverInput = null;
            _serverProcess = null;
            _initializedTcs?.TrySetException(new InvalidOperationException(message));
        }

        try
        {
            input?.Dispose();
        }
        catch
        {
        }

        _diagnostics.Write(
            "appserver.process.failed",
            new JObject
            {
                ["generation"] = generation,
                ["pendingRequestCount"] = pendingRequests.Count,
                ["message"] = message
            });

        foreach (var pendingRequest in pendingRequests)
        {
            pendingRequest.TrySetException(new InvalidOperationException(message));
        }

        turnState?.OnError(message + Environment.NewLine);
        turnState?.TrySetResult(1);
    }

    private void RestartServer(bool clearConfig)
    {
        Process? process;
        StreamWriter? input;
        List<TaskCompletionSource<JToken?>> pendingRequests;

        lock (_syncRoot)
        {
            process = _serverProcess;
            input = _serverInput;
            pendingRequests = _pendingRequests.Values.Select(request => request.Completion).ToList();
            _pendingRequests.Clear();
            _serverProcess = null;
            _serverInput = null;
            _initializedTcs = null;
            _threadId = null;
            _threadConfigKey = null;
            _threadLoaded = false;
            _skillsCacheKey = null;
            _skillsByName.Clear();

            if (clearConfig)
            {
                _serverConfigKey = null;
            }
        }

        foreach (var pendingRequest in pendingRequests)
        {
            pendingRequest.TrySetException(new InvalidOperationException(GetLocalization().AppServerClosedUnexpectedly));
        }

        try
        {
            input?.Dispose();
        }
        catch
        {
        }

        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000);
            }
        }
        catch
        {
        }
        finally
        {
            process?.Dispose();
        }
    }

    private CodexApprovalRequest BuildApprovalRequest(string method, JToken? parameters)
    {
        var isLegacy = IsLegacyApprovalRequestMethod(method);
        var isCommand = IsCommandApprovalRequestMethod(method);
        var options = isLegacy
            ? BuildLegacyApprovalOptions()
            : isCommand
                ? BuildCommandApprovalOptions(parameters?["availableDecisions"] as JArray, parameters?["proposedExecpolicyAmendment"] as JArray)
                : BuildFileChangeApprovalOptions();

        return new CodexApprovalRequest
        {
            Method = method,
            ThreadId = parameters?["threadId"]?.Value<string>()
                ?? parameters?["conversationId"]?.Value<string>()
                ?? string.Empty,
            TurnId = parameters?["turnId"]?.Value<string>() ?? string.Empty,
            ItemId = parameters?["itemId"]?.Value<string>() ?? string.Empty,
            ApprovalId = parameters?["approvalId"]?.Value<string>(),
            Command = GetCommandText(parameters?["command"]),
            WorkingDirectory = parameters?["cwd"]?.Value<string>(),
            Reason = parameters?["reason"]?.Value<string>(),
            GrantRoot = parameters?["grantRoot"]?.Value<string>(),
            ProposedExecpolicyLabel = parameters?["proposedExecpolicyAmendment"]?.Type == JTokenType.Array
                ? string.Join(" ", parameters["proposedExecpolicyAmendment"]!.Values<string>())
                : null,
            Options = options
        };
    }

    private static bool IsApprovalRequestMethod(string method)
    {
        return string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal)
            || string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal)
            || IsLegacyApprovalRequestMethod(method);
    }

    private static bool IsLegacyApprovalRequestMethod(string method)
    {
        return string.Equals(method, "execCommandApproval", StringComparison.Ordinal)
            || string.Equals(method, "applyPatchApproval", StringComparison.Ordinal);
    }

    private static bool IsCommandApprovalRequestMethod(string method)
    {
        return string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal)
            || string.Equals(method, "execCommandApproval", StringComparison.Ordinal);
    }

    private static string? GetCommandText(JToken? command)
    {
        if (command is null || command.Type == JTokenType.Null)
        {
            return null;
        }

        if (command.Type == JTokenType.Array)
        {
            return string.Join(" ", command.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return command.Value<string>() ?? NewtonsoftJsonCompatibility.Serialize(command, Formatting.None);
    }

    private static CodexUserInputRequest BuildUserInputRequest(JToken? parameters)
    {
        var request = new CodexUserInputRequest
        {
            ThreadId = parameters?["threadId"]?.Value<string>() ?? string.Empty,
            TurnId = parameters?["turnId"]?.Value<string>() ?? string.Empty,
            ItemId = parameters?["itemId"]?.Value<string>() ?? string.Empty
        };

        var questions = parameters?["questions"] as JArray;
        if (questions is null)
        {
            return request;
        }

        var items = new List<CodexUserInputQuestion>();
        foreach (var question in questions)
        {
            var options = question?["options"] as JArray;
            var mappedOptions = new List<CodexUserInputOption>();
            if (options is not null)
            {
                foreach (var option in options)
                {
                    mappedOptions.Add(new CodexUserInputOption
                    {
                        Label = option?["label"]?.Value<string>() ?? string.Empty,
                        Value = option?["label"]?.Value<string>() ?? string.Empty,
                        Description = option?["description"]?.Value<string>() ?? string.Empty
                    });
                }
            }

            items.Add(new CodexUserInputQuestion
            {
                Header = question?["header"]?.Value<string>() ?? string.Empty,
                Id = question?["id"]?.Value<string>() ?? string.Empty,
                Question = question?["question"]?.Value<string>() ?? string.Empty,
                IsOther = question?["isOther"]?.Value<bool>() ?? false,
                IsSecret = question?["isSecret"]?.Value<bool>() ?? false,
                Options = mappedOptions
            });
        }

        request.Questions = items;
        return request;
    }

    private async Task<JObject> ResolveMcpElicitationAsync(JObject? parameters)
    {
        if (parameters is null)
        {
            return new JObject { ["action"] = "decline" };
        }

        var mode = parameters["mode"]?.Value<string>() ?? string.Empty;
        if (string.Equals(mode, "url", StringComparison.OrdinalIgnoreCase))
        {
            var url = parameters["url"]?.Value<string>();
            if (!IsSafeExternalUrl(url))
            {
                return new JObject { ["action"] = "decline" };
            }

            var request = new CodexApprovalRequest
            {
                Method = "mcpServer/elicitation/request",
                ThreadId = parameters["threadId"]?.Value<string>() ?? string.Empty,
                TurnId = parameters["turnId"]?.Value<string>() ?? string.Empty,
                Command = url,
                Reason = parameters["message"]?.Value<string>(),
                Options = new[]
                {
                    new CodexApprovalOption("accept", JValue.CreateString("accept")),
                    new CodexApprovalOption("decline", JValue.CreateString("decline"))
                }
            };
            var decision = await ResolveApprovalDecisionAsync(request).ConfigureAwait(false);
            if (!string.Equals(decision.Value<string>(), "accept", StringComparison.Ordinal))
            {
                return new JObject { ["action"] = "decline" };
            }

            try
            {
                Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
                return new JObject { ["action"] = "accept" };
            }
            catch (Exception ex)
            {
                PublishError("[mcp elicitation] " + ex.Message + Environment.NewLine);
                return new JObject { ["action"] = "decline" };
            }
        }

        var schema = parameters["requestedSchema"] as JObject;
        if (schema is null)
        {
            return new JObject { ["action"] = "decline" };
        }

        var requestModel = BuildMcpElicitationInputRequest(parameters, schema);
        var response = await ResolveUserInputRequestAsync(requestModel).ConfigureAwait(false);
        var answers = response?["answers"] as JObject;
        if (answers is null || answers.Count == 0)
        {
            return new JObject { ["action"] = "decline" };
        }

        var content = ConvertMcpElicitationAnswers(schema, answers);
        return content.Count == 0
            ? new JObject { ["action"] = "decline" }
            : new JObject { ["action"] = "accept", ["content"] = content };
    }

    private static CodexUserInputRequest BuildMcpElicitationInputRequest(JObject parameters, JObject schema)
    {
        var request = new CodexUserInputRequest
        {
            ThreadId = parameters["threadId"]?.Value<string>() ?? string.Empty,
            TurnId = parameters["turnId"]?.Value<string>() ?? string.Empty
        };
        var required = new HashSet<string>(
            (schema["required"] as JArray)?.Values<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);
        var questions = new List<CodexUserInputQuestion>();
        if (schema["properties"] is not JObject properties)
        {
            request.Questions = questions;
            return request;
        }

        foreach (var property in properties.Properties())
        {
            if (property.Value is not JObject definition)
            {
                continue;
            }

            var options = ExtractMcpElicitationOptions(definition)
                .Select(value => new CodexUserInputOption { Label = value.Label, Value = value.Value })
                .ToList();
            var description = definition["description"]?.Value<string>() ?? parameters["message"]?.Value<string>() ?? property.Name;
            if (!required.Contains(property.Name))
            {
                description += " (optional)";
            }

            questions.Add(new CodexUserInputQuestion
            {
                Id = property.Name,
                Header = definition["title"]?.Value<string>() ?? property.Name,
                Question = description,
                IsOther = options.Count == 0,
                Options = options
            });
        }

        request.Questions = questions;
        return request;
    }

    private static IEnumerable<(string Label, string Value)> ExtractMcpElicitationOptions(JObject definition)
    {
        if (definition["enum"] is JArray legacyValues)
        {
            var labels = (definition["enumNames"] as JArray)?.Values<string>().Select(value => value ?? string.Empty).ToList() ?? new List<string>();
            var values = legacyValues.Values<string>().Where(value => value is not null).Select(value => value!).ToList();
            for (var index = 0; index < values.Count; index++)
            {
                yield return (index < labels.Count ? labels[index] : values[index], values[index]);
            }

            yield break;
        }

        var titledValues = definition["oneOf"] as JArray ?? definition["items"]?["anyOf"] as JArray;
        if (titledValues is not null)
        {
            foreach (var option in titledValues)
            {
                var value = option?["const"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return (option?["title"]?.Value<string>() ?? value!, value!);
                }
            }

            yield break;
        }

        if (definition["items"]?["enum"] is JArray itemValues)
        {
            foreach (var value in itemValues.Values<string>().Where(value => value is not null).Select(value => value!))
            {
                yield return (value, value);
            }
        }
    }

    private static JObject ConvertMcpElicitationAnswers(JObject schema, JObject answers)
    {
        var content = new JObject();
        var properties = schema["properties"] as JObject;
        if (properties is null)
        {
            return content;
        }

        foreach (var answer in answers.Properties())
        {
            var raw = answer.Value?["answers"]?.First?.Value<string>();
            if (raw is null || properties[answer.Name] is not JObject definition)
            {
                continue;
            }

            var type = definition["type"]?.Value<string>() ?? "string";
            switch (type)
            {
                case "boolean" when bool.TryParse(raw, out var booleanValue):
                    content[answer.Name] = booleanValue;
                    break;
                case "integer" when long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue):
                    content[answer.Name] = integerValue;
                    break;
                case "number" when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var numberValue):
                    content[answer.Name] = numberValue;
                    break;
                case "array":
                    content[answer.Name] = new JArray(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()));
                    break;
                default:
                    content[answer.Name] = raw;
                    break;
            }
        }

        return content;
    }

    private static bool IsSafeExternalUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<JToken> ResolveApprovalDecisionAsync(CodexApprovalRequest request)
    {
        var info = JsonConvert.SerializeObject(request, Formatting.None);
        if (!string.IsNullOrWhiteSpace(info))
        {
            PublishError("[" + GetLocalization().OutputTagApproval + "] " + info + Environment.NewLine);
        }

        if (ApprovalRequestHandler is null)
        {
            return GetDefaultDeclineDecision(request.Method);
        }

        try
        {
            var decision = await ApprovalRequestHandler.Invoke(request).ConfigureAwait(false);
            return decision ?? GetDefaultDeclineDecision(request.Method);
        }
        catch (Exception ex)
        {
            PublishError("[" + GetLocalization().OutputTagApproval + "] " + ex.Message + Environment.NewLine);
            return GetDefaultDeclineDecision(request.Method);
        }
    }

    private async Task<JObject?> ResolveUserInputRequestAsync(CodexUserInputRequest request)
    {
        if (UserInputRequestHandler is null)
        {
            return new JObject { ["answers"] = new JObject() };
        }

        try
        {
            return await UserInputRequestHandler.Invoke(request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PublishError("[" + GetLocalization().OutputTagUserInput + "] " + ex.Message + Environment.NewLine);
            return new JObject { ["answers"] = new JObject() };
        }
    }

    private LocalizationService GetLocalization()
    {
        return new LocalizationService(_languageOverride);
    }

    private static IReadOnlyList<CodexApprovalOption> BuildCommandApprovalOptions(JArray? availableDecisions, JArray? proposedExecpolicyAmendment)
    {
        var options = new List<CodexApprovalOption>();
        if (availableDecisions is not null)
        {
            foreach (var decision in availableDecisions)
            {
                var option = CreateCommandApprovalOption(decision, proposedExecpolicyAmendment);
                if (option is not null)
                {
                    options.Add(option);
                }
            }
        }

        if (options.Count == 0)
        {
            options.Add(new CodexApprovalOption("accept", JValue.CreateString("accept")));
            options.Add(new CodexApprovalOption("decline", JValue.CreateString("decline")));
            options.Add(new CodexApprovalOption("cancel", JValue.CreateString("cancel")));
        }

        return options;
    }

    private static CodexApprovalOption? CreateCommandApprovalOption(JToken decision, JArray? proposedExecpolicyAmendment)
    {
        if (decision.Type == JTokenType.String)
        {
            var key = decision.Value<string>();
            return string.IsNullOrWhiteSpace(key) ? null : new CodexApprovalOption(key!, JValue.CreateString(key!));
        }

        if (decision["acceptWithExecpolicyAmendment"] is not null && proposedExecpolicyAmendment is not null)
        {
            return new CodexApprovalOption(
                "acceptWithExecpolicyAmendment",
                new JObject
                {
                    ["acceptWithExecpolicyAmendment"] = new JObject
                    {
                        ["execpolicy_amendment"] = proposedExecpolicyAmendment.DeepClone()
                    }
                });
        }

        if (decision["applyNetworkPolicyAmendment"] is not null)
        {
            return new CodexApprovalOption("applyNetworkPolicyAmendment", decision.DeepClone());
        }

        return null;
    }

    private static IReadOnlyList<CodexApprovalOption> BuildFileChangeApprovalOptions()
    {
        return
        [
            new CodexApprovalOption("accept", JValue.CreateString("accept")),
            new CodexApprovalOption("decline", JValue.CreateString("decline")),
            new CodexApprovalOption("cancel", JValue.CreateString("cancel"))
        ];
    }

    private static IReadOnlyList<CodexApprovalOption> BuildLegacyApprovalOptions()
    {
        return
        [
            new CodexApprovalOption("accept", JValue.CreateString("approved")),
            new CodexApprovalOption("decline", JValue.CreateString("denied")),
            new CodexApprovalOption("cancel", JValue.CreateString("abort"))
        ];
    }

    private static JToken GetDefaultDeclineDecision(string method)
    {
        if (IsLegacyApprovalRequestMethod(method))
        {
            return JValue.CreateString("denied");
        }

        if (string.Equals(method, "item/permissions/requestApproval", StringComparison.Ordinal))
        {
            return new JObject();
        }

        return JValue.CreateString(string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal) ? "decline" : "cancel");
    }

    private static string BuildServerArguments(CodexExtensionSettings settings)
    {
        return CodexAppServerCommandLine.Build(settings);
    }

    private static string BuildServerConfigKey(CodexExtensionSettings settings)
    {
        var resolvedExecutablePath = CodexExecutableResolver.ResolveExecutableLocation(settings.CodexExecutablePath, settings.EnvironmentVariables);
        return string.Join("\n", new[]
        {
            string.IsNullOrWhiteSpace(resolvedExecutablePath)
                ? CodexExecutableResolver.NormalizeConfiguredExecutablePath(settings.CodexExecutablePath)
                : resolvedExecutablePath,
            settings.Profile ?? string.Empty,
            string.Join("\n", CodexAppServerCommandLine.BuildConfigOverrides(settings)),
            settings.AdditionalArguments ?? string.Empty,
            settings.EnvironmentVariables ?? string.Empty
        });
    }

    private static string BuildSkillScopeLabel(string path, string workingDirectory, string homeSkillsDirectory, bool isSystem, LocalizationService localization)
    {
        if (isSystem)
        {
            return localization.SkillScopeSystem;
        }

        if (IsPathWithinDirectory(path, homeSkillsDirectory))
        {
            return localization.SkillScopeGlobal;
        }

        if (IsPathWithinDirectory(path, workingDirectory))
        {
            return localization.SkillScopeWorkspace;
        }

        return localization.SkillScopeExternal;
    }

    internal static bool IsSystemSkillPath(string path)
    {
        return (path ?? string.Empty)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, ".system", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsPathWithinDirectory(string path, string directory)
    {
        var normalizedPath = NormalizeComparablePath(path);
        var normalizedDirectory = NormalizeComparablePath(directory);
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedDirectory))
        {
            return false;
        }

        return string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    || normalizedDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                        ? normalizedDirectory
                        : normalizedDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildThreadConfigKey(CodexExtensionSettings settings, string workingDirectory)
    {
        return string.Join("\n", new[]
        {
            workingDirectory,
            settings.DefaultModel ?? string.Empty,
            NormalizeServiceTier(settings.ServiceTier) ?? string.Empty,
            NormalizeApprovalPolicy(settings.ApprovalPolicy),
            NormalizeSandboxMode(settings.SandboxMode)
        });
    }

    private static string NormalizeApprovalPolicy(string approvalPolicy)
    {
        return string.IsNullOrWhiteSpace(approvalPolicy) ? "never" : approvalPolicy;
    }

    private static string NormalizeSandboxMode(string sandboxMode)
    {
        return string.IsNullOrWhiteSpace(sandboxMode) ? "danger-full-access" : sandboxMode;
    }

    private static string? NormalizeServiceTier(string? serviceTier)
    {
        var normalized = (serviceTier ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized switch
        {
            "fast" => "fast",
            "flex" => "flex",
            _ => normalized
        };
    }

    private static ProcessStartInfo BuildStartInfo(string executablePath, string arguments, string workingDirectory)
    {
        var resolvedWorkingDirectory = ResolveWorkingDirectory(workingDirectory);
        if (IsPowerShellScript(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = ResolvePowerShellHost(),
                WorkingDirectory = resolvedWorkingDirectory,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + CodexAppServerCommandLine.QuoteArgument(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments)
            };
        }

        if (!RequiresCommandShell(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = resolvedWorkingDirectory,
                Arguments = arguments
            };
        }

        var commandShell = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(commandShell))
        {
            commandShell = "cmd.exe";
        }

        return new ProcessStartInfo
        {
            FileName = commandShell,
            WorkingDirectory = resolvedWorkingDirectory,
            Arguments = "/d /s /c \"" + QuoteForCommandShell(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments) + "\""
        };
    }

    private static string ResolveWorkingDirectory(string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            return workingDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    internal static string NormalizeComparablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path!.Trim();
        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"\\" + normalized.Substring(@"\\?\UNC\".Length);
        }
        else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(@"\\?\".Length);
        }

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        var root = Path.GetPathRoot(normalized);
        return !string.IsNullOrWhiteSpace(root) && string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)
            ? root!
            : normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ToWindowsDevicePath(string path)
    {
        if (!IsWindows() || string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path.Substring(2);
        }

        return @"\\?\" + path;
    }

    private static void ApplyEnvironmentVariables(ProcessStartInfo psi, string environmentVariables)
    {
        foreach (var line in environmentVariables.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separatorIndex).Trim();
            var value = line.Substring(separatorIndex + 1).Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                psi.EnvironmentVariables[key] = value;
            }
        }
    }

    private static bool RequiresCommandShell(string executablePath)
    {
        if (!IsWindows())
        {
            return false;
        }

        var extension = Path.GetExtension(executablePath);
        return string.IsNullOrEmpty(extension)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerShellScript(string executablePath)
    {
        return IsWindows()
            && Path.GetExtension(executablePath).Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteForCommandShell(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string ResolvePowerShellHost()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPowerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(windowsPowerShell)
            ? windowsPowerShell
            : "powershell.exe";
    }

    private static bool IsWindows()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
    }

    private sealed class PendingRequest
    {
        public PendingRequest(long generation, string method, TaskCompletionSource<JToken?> completion)
        {
            Generation = generation;
            Method = method ?? string.Empty;
            Completion = completion;
        }

        public long Generation { get; }

        public string Method { get; }

        public TaskCompletionSource<JToken?> Completion { get; }
    }

    private sealed class ActiveTurnState
    {
        public ActiveTurnState(Action<string> onOutput, Action<string> onError, Action<ChatMessage>? onEventMessage, Action<long, long?>? onTokenUsage)
        {
            OnOutput = onOutput;
            OnError = onError;
            OnEventMessage = onEventMessage;
            OnTokenUsage = onTokenUsage;
            Completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TaskCompletionSource<int> Completion { get; }
        public Action<string> OnOutput { get; }
        public Action<string> OnError { get; }
        public Action<ChatMessage>? OnEventMessage { get; }
        public Action<long, long?>? OnTokenUsage { get; }
        public HashSet<string> StreamedItemIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StringBuilder> PlanTextByItemId { get; } = new(StringComparer.Ordinal);
        public string? TurnId { get; set; }
        public bool HasAssistantOutput { get; set; }
        public bool InterruptRequested { get; set; }

        public string AppendPlanDelta(string? itemId, string delta)
        {
            var key = string.IsNullOrWhiteSpace(itemId) ? "__plan__" : itemId!;
            if (!PlanTextByItemId.TryGetValue(key, out var builder))
            {
                builder = new StringBuilder();
                PlanTextByItemId[key] = builder;
            }

            builder.Append(delta);
            return builder.ToString();
        }

        public void SetPlanText(string? itemId, string planText)
        {
            var key = string.IsNullOrWhiteSpace(itemId) ? "__plan__" : itemId!;
            PlanTextByItemId[key] = new StringBuilder(planText ?? string.Empty);
        }

        public string? GetPlanText(string? itemId)
        {
            var key = string.IsNullOrWhiteSpace(itemId) ? "__plan__" : itemId!;
            if (PlanTextByItemId.TryGetValue(key, out var builder) && builder.Length > 0)
            {
                return builder.ToString();
            }

            if (!string.Equals(key, "__plan__", StringComparison.Ordinal)
                && PlanTextByItemId.TryGetValue("__plan__", out var sharedBuilder)
                && sharedBuilder.Length > 0)
            {
                return sharedBuilder.ToString();
            }

            if (PlanTextByItemId.Count == 1)
            {
                return PlanTextByItemId.Values.First().ToString();
            }

            return null;
        }

        public void TrySetResult(int result)
        {
            Completion.TrySetResult(result);
        }
    }

    private sealed class CodexAppServerException : InvalidOperationException
    {
        public CodexAppServerException(string message, int? code)
            : base(message)
        {
            Code = code;
        }

        public int? Code { get; }

        public bool IsMethodNotFound => Code == -32601
            || Message.IndexOf("method not found", StringComparison.OrdinalIgnoreCase) >= 0
            || Message.IndexOf("unknown variant", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsCompatibilityError => IsMethodNotFound
            || Code == -32602
            || Message.IndexOf("invalid params", StringComparison.OrdinalIgnoreCase) >= 0
            || Message.IndexOf("unknown field", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
