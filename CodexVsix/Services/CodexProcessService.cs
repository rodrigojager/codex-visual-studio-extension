using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static readonly Regex MentionRegex = new(@"(?<!\S)@(?<value>\S+)", RegexOptions.Compiled);
    private static readonly Regex SkillRegex = new(@"(?<!\S)\$(?<value>[A-Za-z0-9][A-Za-z0-9._-]*)", RegexOptions.Compiled);
    private const string ExtensionContextPrefix = "Contexto da extensão: o diretório de trabalho atual do projeto aberto é \"";

    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly object _writeLock = new();
    private readonly Dictionary<long, TaskCompletionSource<JToken?>> _pendingRequests = new();
    private readonly Dictionary<string, string> _skillsByName = new(StringComparer.OrdinalIgnoreCase);

    private Process? _serverProcess;
    private StreamWriter? _serverInput;
    private TaskCompletionSource<bool>? _initializedTcs;
    private ActiveTurnState? _activeTurn;
    private string? _threadId;
    private string? _threadConfigKey;
    private string? _serverConfigKey;
    private string? _skillsCacheKey;
    private bool _threadLoaded;
    private long _nextRequestId;

    public Func<CodexApprovalRequest, Task<JToken?>>? ApprovalRequestHandler { get; set; }
    public Func<CodexUserInputRequest, Task<JObject?>>? UserInputRequestHandler { get; set; }
    public event Action? ThreadCatalogChanged;

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

    public async Task<int> ExecuteAsync(
        string prompt,
        CodexExtensionSettings settings,
        IEnumerable<string> imagePaths,
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
                    var turnResult = await SendRequestAsync(
                        "turn/start",
                        BuildTurnStartParams(_threadId!, prompt, settings, workingDirectory, imagePaths),
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

    public async Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync(
            "thread/list",
            new
            {
                cwd = workingDirectory,
                limit = 50,
                archived = false,
                sortKey = "updated_at",
                sourceKinds = new[] { "appServer", "vscode", "cli", "exec" }
            },
            cancellationToken).ConfigureAwait(false);

        var currentThreadId = CurrentThreadId ?? settings.CurrentThreadId;
        var items = response?["data"] as JArray;
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

    public async Task<CodexThreadConversation?> LoadThreadConversationAsync(CodexExtensionSettings settings, string threadId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var resumed = await SendRequestAsync(
            "thread/resume",
            BuildThreadResumeParams(threadId, settings, workingDirectory),
            cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            _threadId = resumed?["thread"]?["id"]?.Value<string>() ?? threadId;
            _threadConfigKey = BuildThreadConfigKey(settings, workingDirectory);
            _threadLoaded = true;
        }

        var readResponse = await SendRequestAsync(
            "thread/read",
            new
            {
                threadId,
                includeTurns = true
            },
            cancellationToken).ConfigureAwait(false);

        var thread = readResponse?["thread"] ?? resumed?["thread"];
        if (thread is null)
        {
            return null;
        }

        var summary = ParseThreadSummary(thread, threadId) ?? new CodexThreadSummary { ThreadId = threadId };
        return new CodexThreadConversation
        {
            Thread = summary,
            Messages = ParseThreadMessages(thread)
        };
    }

    public async Task<IReadOnlyList<SelectionOption>> ListModelsAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        await EnsureServerReadyAsync(settings, workingDirectory, cancellationToken).ConfigureAwait(false);

        var response = await SendRequestAsync("model/list", new { }, cancellationToken).ConfigureAwait(false);
        var items = response?["data"] as JArray;
        var models = new List<SelectionOption>();
        if (items is null)
        {
            return models;
        }

        foreach (var item in items)
        {
            if (item?["hidden"]?.Value<bool>() == true)
            {
                continue;
            }

            var value = item?["model"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var label = item?["displayName"]?.Value<string>();
            models.Add(new SelectionOption(string.IsNullOrWhiteSpace(label) ? value! : label!, value!));
        }

        return models;
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
            "thread/setName",
            new
            {
                threadId,
                name = name.Trim()
            },
            cancellationToken).ConfigureAwait(false);
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
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "skills");

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

                var isSystem = path.IndexOf(".system", StringComparison.OrdinalIgnoreCase) >= 0;
                summaries.Add(new CodexSkillSummary
                {
                    Name = name!,
                    Path = path!,
                    IsEnabled = skill["enabled"]?.Value<bool>() ?? true,
                    IsSystem = isSystem,
                    ScopeLabel = BuildSkillScopeLabel(path!, workingDirectory, homeSkillsDirectory, isSystem)
                });
            }
        }

        return summaries
            .OrderBy(skill => skill.IsSystem)
            .ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var desiredServerConfig = BuildServerConfigKey(settings);
        var shouldStart = false;
        var needsRestart = false;

        lock (_syncRoot)
        {
            if (_serverProcess is null || _serverProcess.HasExited || !string.Equals(_serverConfigKey, desiredServerConfig, StringComparison.Ordinal))
            {
                shouldStart = true;
                needsRestart = true;
                _serverConfigKey = desiredServerConfig;
                _initializedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        if (needsRestart)
        {
            RestartServer(clearConfig: false);
            lock (_syncRoot)
            {
                _serverConfigKey = desiredServerConfig;
                _initializedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            shouldStart = true;
        }

        if (shouldStart)
        {
            StartServerProcess(settings, workingDirectory);
            await InitializeServerAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var initTask = _initializedTcs?.Task;
        if (initTask is not null)
        {
            await initTask.ConfigureAwait(false);
        }
    }

    private async Task InitializeServerAsync(CancellationToken cancellationToken)
    {
        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "codex-vsix", version = "1.0" },
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
                BuildThreadResumeParams(requestedThreadId, settings, workingDirectory),
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

    private void StartServerProcess(CodexExtensionSettings settings, string workingDirectory)
    {
        var executablePath = ResolveExecutablePath(settings.CodexExecutablePath);
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
        process.Exited += (_, _) => FailPendingOperations("Codex app server was closed unexpectedly.");

        var serverInput = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false), 1024, true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        lock (_syncRoot)
        {
            _serverProcess = process;
            _serverInput = serverInput;
        }

        _ = Task.Run(() => ReadStdoutLoopAsync(process));
        _ = Task.Run(() => ReadStderrLoopAsync(process));
    }

    private async Task ReadStdoutLoopAsync(Process process)
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
                    HandleServerMessage(line);
                }
                catch (Exception ex)
                {
                    PublishError("[app-server] " + ex.Message + Environment.NewLine);
                }
            }
        }
        catch (Exception ex)
        {
            FailPendingOperations(ex.Message);
        }
    }

    private async Task ReadStderrLoopAsync(Process process)
    {
        try
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is not null)
                {
                    PublishError(line + Environment.NewLine);
                }
            }
        }
        catch (Exception ex)
        {
            PublishError("[stderr] " + ex.Message + Environment.NewLine);
        }
    }

    private void HandleServerMessage(string rawMessage)
    {
        JToken parsedMessage;
        try
        {
            parsedMessage = JToken.Parse(rawMessage);
        }
        catch
        {
            PublishError("[app-server] " + rawMessage + Environment.NewLine);
            return;
        }

        if (parsedMessage is not JObject message)
        {
            PublishError("[app-server] " + rawMessage + Environment.NewLine);
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
        TaskCompletionSource<JToken?>? tcs;
        lock (_syncRoot)
        {
            _pendingRequests.TryGetValue(id, out tcs);
            if (tcs is not null)
            {
                _pendingRequests.Remove(id);
            }
        }

        if (tcs is null)
        {
            return;
        }

        if (message["error"] is not null)
        {
            var errorMessage = GetNestedString(message["error"], "message")
                ?? message["error"]?.Value<string>()
                ?? "App server request failed.";
            tcs.TrySetException(new InvalidOperationException(errorMessage));
            return;
        }

        tcs.TrySetResult(message["result"]);
    }

    private async Task HandleServerRequestAsync(JObject message)
    {
        var id = message["id"];
        var method = message["method"]?.Value<string>() ?? string.Empty;
        var parameters = message["params"] as JObject;
        if (string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal) ||
            string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal))
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

        await SendResponseAsync(id, new JObject()).ConfigureAwait(false);
    }

    private void HandleNotification(JObject message)
    {
        var method = message["method"]?.Value<string>() ?? string.Empty;
        var parameters = message["params"] as JObject;

        switch (method)
        {
            case "item/agentMessage/delta":
                HandleAgentMessageDelta(parameters);
                break;

            case "item/completed":
                HandleCompletedItem(parameters);
                break;

            case "turn/completed":
                HandleTurnCompleted(parameters);
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
                NotifyThreadCatalogChanged();
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
            turnState?.OnOutput(delta!);
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
            turnState?.OnEventMessage?.Invoke(new ChatMessage(false, message.Trim(), isEvent: true, title: "MCP progress"));
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

        if (!string.Equals(itemType, "agentMessage", StringComparison.OrdinalIgnoreCase))
        {
            var eventMessage = BuildThreadEventMessage(item);
            if (eventMessage is not null && eventMessage.IsEvent)
            {
                turnState.OnEventMessage?.Invoke(eventMessage);
            }

            return;
        }

        var itemId = item?["id"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(itemId) && turnState.StreamedItemIds.Contains(itemId!))
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
            turnState.OnOutput(text);
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

        var totalTokens = GetNestedToken(parameters?["tokenUsage"], "total", "totalTokens")?.Value<long?>() ?? 0L;
        var contextWindow = GetNestedToken(parameters?["tokenUsage"], "modelContextWindow")?.Value<long?>();

        lock (_syncRoot)
        {
            _activeTurn?.OnTokenUsage?.Invoke(totalTokens, contextWindow);
        }
    }

    private bool MatchesActiveTurn(string? turnId)
    {
        lock (_syncRoot)
        {
            return _activeTurn is not null && (string.IsNullOrWhiteSpace(_activeTurn.TurnId) || string.Equals(_activeTurn.TurnId, turnId, StringComparison.Ordinal));
        }
    }

    private async Task InterruptActiveTurnAsync()
    {
        ActiveTurnState? turnState;
        string? threadId;
        lock (_syncRoot)
        {
            turnState = _activeTurn;
            threadId = _threadId;
        }

        if (turnState is null || string.IsNullOrWhiteSpace(turnState.TurnId) || string.IsNullOrWhiteSpace(threadId) || turnState.InterruptRequested)
        {
            return;
        }

        turnState.InterruptRequested = true;

        try
        {
            await SendRequestAsync(
                "turn/interrupt",
                new { threadId, turnId = turnState.TurnId },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private Task<JToken?> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_syncRoot)
        {
            _pendingRequests[id] = tcs;
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                lock (_syncRoot)
                {
                    if (_pendingRequests.Remove(id))
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    }
                }
            });
        }

        WriteMessage(new JObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = JObject.FromObject(parameters)
        });

        return tcs.Task;
    }

    private Task SendNotificationAsync(string method, object parameters)
    {
        WriteMessage(new JObject
        {
            ["method"] = method,
            ["params"] = JObject.FromObject(parameters)
        });

        return Task.CompletedTask;
    }

    private Task SendResponseAsync(JToken? id, JObject result)
    {
        WriteMessage(new JObject
        {
            ["id"] = id,
            ["result"] = result
        });

        return Task.CompletedTask;
    }

    private void WriteMessage(JObject message)
    {
        StreamWriter? writer;
        lock (_syncRoot)
        {
            writer = _serverInput;
        }

        if (writer is null)
        {
            throw new InvalidOperationException("Codex app server is not available.");
        }

        var json = message.ToString(Formatting.None);
        lock (_writeLock)
        {
            writer.WriteLine(json);
            writer.Flush();
        }
    }

    private object BuildThreadStartParams(CodexExtensionSettings settings, string workingDirectory)
    {
        return new
        {
            cwd = workingDirectory,
            approvalPolicy = NormalizeApprovalPolicy(settings.ApprovalPolicy),
            sandbox = NormalizeSandboxMode(settings.SandboxMode),
            model = string.IsNullOrWhiteSpace(settings.DefaultModel) ? null : settings.DefaultModel,
            personality = "pragmatic",
            persistExtendedHistory = true
        };
    }

    private object BuildTurnStartParams(string threadId, string prompt, CodexExtensionSettings settings, string workingDirectory, IEnumerable<string> imagePaths)
    {
        return new
        {
            threadId,
            cwd = workingDirectory,
            model = string.IsNullOrWhiteSpace(settings.DefaultModel) ? null : settings.DefaultModel,
            effort = string.IsNullOrWhiteSpace(settings.ReasoningEffort) ? null : settings.ReasoningEffort,
            approvalPolicy = NormalizeApprovalPolicy(settings.ApprovalPolicy),
            sandboxPolicy = BuildSandboxPolicy(settings.SandboxMode),
            collaborationMode = BuildCollaborationMode(settings),
            input = BuildUserInput(prompt, workingDirectory, imagePaths)
        };
    }

    private static object BuildThreadResumeParams(string threadId, CodexExtensionSettings settings, string workingDirectory)
    {
        return new
        {
            threadId,
            cwd = workingDirectory,
            approvalPolicy = NormalizeApprovalPolicy(settings.ApprovalPolicy),
            sandbox = NormalizeSandboxMode(settings.SandboxMode),
            model = string.IsNullOrWhiteSpace(settings.DefaultModel) ? null : settings.DefaultModel,
            personality = "pragmatic",
            persistExtendedHistory = true
        };
    }

    private static object? BuildCollaborationMode(CodexExtensionSettings settings)
    {
        if (!settings.PlanModeEnabled || string.IsNullOrWhiteSpace(settings.DefaultModel))
        {
            return null;
        }

        return new
        {
            mode = "plan",
            settings = new
            {
                model = settings.DefaultModel,
                reasoning_effort = string.IsNullOrWhiteSpace(settings.ReasoningEffort) ? null : settings.ReasoningEffort
            }
        };
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

    private object[] BuildUserInput(string prompt, string workingDirectory, IEnumerable<string> imagePaths)
    {
        var inputs = new List<object>
        {
            new
            {
                type = "text",
                text = "Contexto da extensão: o diretório de trabalho atual do projeto aberto é \"" + Path.GetFullPath(workingDirectory) + "\"."
            },
            new
            {
                type = "text",
                text = prompt
            }
        };

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
            inputs.Add(new
            {
                type = "localImage",
                path = Path.GetFullPath(imagePath)
            });
        }

        return inputs.ToArray();
    }

    private IEnumerable<object> ExtractMentionInputs(string prompt, string workingDirectory)
    {
        foreach (Match match in MentionRegex.Matches(prompt ?? string.Empty))
        {
            var rawValue = match.Groups["value"].Value.Trim();
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
            _ => current.ToString(Formatting.None)
        };
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
        var updatedAt = thread?["updatedAt"]?.Value<long?>() ?? 0L;
        var status = GetNestedString(thread, "status", "type") ?? string.Empty;

        return new CodexThreadSummary
        {
            ThreadId = threadId,
            Name = name,
            Preview = string.IsNullOrWhiteSpace(preview) ? threadId : preview,
            UpdatedAt = updatedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(updatedAt).ToLocalTime() : DateTimeOffset.MinValue,
            Status = status,
            IsActive = string.Equals(threadId, activeThreadId, StringComparison.Ordinal)
        };
    }

    private static IReadOnlyList<ChatMessage> ParseThreadMessages(JToken thread)
    {
        var messages = new List<ChatMessage>();
        var turns = thread["turns"] as JArray;
        if (turns is null)
        {
            return messages;
        }

        foreach (var turn in turns)
        {
            var items = turn["items"] as JArray;
            if (items is null)
            {
                continue;
            }

            foreach (var item in items)
            {
                var message = ParseThreadMessage(item);
                if (message is not null)
                {
                    messages.Add(message);
                }
            }
        }

        return messages;
    }

    private static ChatMessage? ParseThreadMessage(JToken? item)
    {
        var itemType = item?["type"]?.Value<string>();
        switch (itemType)
        {
            case "userMessage":
                var userText = ExtractUserMessageText(item?["content"]);
                return string.IsNullOrWhiteSpace(userText) ? null : new ChatMessage(true, userText);

            case "agentMessage":
                var agentText = item?["text"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(agentText))
                {
                    agentText = ExtractText(item?["content"]);
                }

                return string.IsNullOrWhiteSpace(agentText) ? null : new ChatMessage(false, agentText);

            case "plan":
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

    private static string ExtractUserMessageText(JToken? content)
    {
        var items = content as JArray;
        if (items is null)
        {
            return string.Empty;
        }

        var segments = new List<string>();
        foreach (var item in items)
        {
            switch (item?["type"]?.Value<string>())
            {
                case "text":
                    var text = item?["text"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith(ExtensionContextPrefix, StringComparison.Ordinal))
                    {
                        segments.Add(text.Trim());
                    }
                    break;

                case "localImage":
                case "image":
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
            }
        }

        return string.Join(" ", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static ChatMessage? BuildThreadEventMessage(JToken? item)
    {
        var itemType = item?["type"]?.Value<string>();
        switch (itemType)
        {
            case "plan":
                var planText = NormalizeDetail(item?["text"]?.Value<string>());
                return CreateEventMessage("Plan", BuildSummary(planText, "Plan updated"), planText);

            case "reasoning":
                var reasoningText = NormalizeDetail(JoinTextArray(item?["summary"]));
                return CreateEventMessage("Reasoning", BuildSummary(reasoningText, "Reasoning updated"), reasoningText);

            case "commandExecution":
                var command = item?["command"]?.Value<string>();
                var status = item?["status"]?.Value<string>();
                var exitCode = item?["exitCode"]?.Value<int?>();
                var durationMs = item?["durationMs"]?.Value<long?>();
                var aggregatedOutput = NormalizeDetail(item?["aggregatedOutput"]?.Value<string>());
                var cwd = item?["cwd"]?.Value<string>();
                return CreateEventMessage(
                    "Command",
                    BuildCommandSummary(command, status, exitCode, durationMs),
                    BuildDetailSections(
                        string.IsNullOrWhiteSpace(cwd) ? null : "Working directory" + Environment.NewLine + cwd.Trim(),
                        string.IsNullOrWhiteSpace(aggregatedOutput) ? null : "Output" + Environment.NewLine + aggregatedOutput));

            case "fileChange":
                var changes = item?["changes"] as JArray;
                return CreateEventMessage("File changes", BuildFileChangeSummary(changes), BuildFileChangeDetail(changes));

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
                    "MCP tool",
                    BuildToolSummary(toolLabel, toolStatus, toolDurationMs, mcpSuccess),
                    BuildDetailSections(
                        BuildNamedJsonBlock("Arguments", item?["arguments"]),
                        string.IsNullOrWhiteSpace(errorMessage) ? null : "Error" + Environment.NewLine + errorMessage.Trim(),
                        BuildNamedTextBlock("Result", ExtractContentText(GetNestedToken(item, "result", "content")) ?? SerializeStructuredValue(item?["result"]))));

            case "dynamicToolCall":
                var dynamicTool = item?["tool"]?.Value<string>();
                var success = item?["success"]?.Value<bool?>();
                return CreateEventMessage(
                    "Tool",
                    BuildToolSummary(dynamicTool, item?["status"]?.Value<string>(), item?["durationMs"]?.Value<long?>(), success),
                    BuildDetailSections(
                        BuildNamedJsonBlock("Arguments", item?["arguments"]),
                        BuildNamedTextBlock("Output", ExtractContentText(item?["contentItems"]) ?? SerializeStructuredValue(item?["contentItems"]))));

            case "collabAgentToolCall":
                var collabTool = item?["tool"]?.Value<string>();
                var receiverIds = item?["receiverThreadIds"] is JArray receivers
                    ? string.Join(", ", receivers.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)))
                    : string.Empty;
                return CreateEventMessage(
                    "Agent tool",
                    BuildSummary(string.IsNullOrWhiteSpace(receiverIds) ? collabTool : collabTool + " -> " + receiverIds, "Agent tool used"),
                    BuildDetailSections(
                        BuildNamedTextBlock("Prompt", NormalizeDetail(item?["prompt"]?.Value<string>())),
                        BuildNamedJsonBlock("Arguments", item?["arguments"])));

            case "webSearch":
                var query = item?["query"]?.Value<string>();
                return CreateEventMessage(
                    "Web search",
                    BuildSummary(query, "Web search"),
                    BuildNamedTextBlock("Result", ExtractContentText(GetNestedToken(item, "result", "content")) ?? SerializeStructuredValue(item?["result"])));

            case "imageView":
                return CreateEventMessage("Image view", BuildSummary(item?["path"]?.Value<string>(), "Image viewed"));

            case "imageGeneration":
                return CreateEventMessage(
                    "Image generation",
                    BuildSummary(item?["status"]?.Value<string>(), "Image generated"),
                    BuildNamedTextBlock("Prompt", NormalizeDetail(item?["prompt"]?.Value<string>())));

            case "enteredReviewMode":
                return CreateEventMessage("Review mode", BuildSummary(item?["review"]?.Value<string>(), "Entered review mode"));

            case "exitedReviewMode":
                return CreateEventMessage("Review mode", BuildSummary(item?["review"]?.Value<string>(), "Exited review mode"));

            case "contextCompaction":
                return CreateEventMessage("Context", "Conversation context compacted");

            default:
                return null;
        }
    }

    private static ChatMessage? CreateEventMessage(string title, string? summary, string? detail = null)
    {
        var normalizedSummary = BuildSummary(summary, title);
        var normalizedDetail = NormalizeDetail(detail);
        if (string.Equals(normalizedSummary, CompactSingleLine(normalizedDetail), StringComparison.Ordinal))
        {
            normalizedDetail = null;
        }

        return new ChatMessage(false, normalizedSummary, isEvent: true, title: title, detail: normalizedDetail);
    }

    private static string BuildCommandSummary(string? command, string? status, int? exitCode, long? durationMs)
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

        return parts.Count == 0 ? "Command executed" : string.Join(" ", parts);
    }

    private static string BuildFileChangeSummary(JArray? changes)
    {
        if (changes is null || changes.Count == 0)
        {
            return "updated files";
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
            parts.Add("+" + (changes.Count - 3) + " more");
        }

        return string.Join(", ", parts);
    }

    private static string? BuildFileChangeDetail(JArray? changes)
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
            var header = BuildSummary(string.IsNullOrWhiteSpace(kind) ? path : kind + " " + path, "file updated");
            var diff = NormalizeDetail(change?["diff"]?.Value<string>());
            details.Add(string.IsNullOrWhiteSpace(diff) ? header : header + Environment.NewLine + Truncate(diff, 700));
        }

        if (changes.Count > 6)
        {
            details.Add("+" + (changes.Count - 6) + " more files");
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

    private static string BuildToolSummary(string? label, string? status, long? durationMs, bool? success = null)
    {
        var parts = new List<string>();
        var compactLabel = CompactSingleLine(label);
        if (!string.IsNullOrWhiteSpace(compactLabel))
        {
            parts.Add(Truncate(compactLabel, 120));
        }

        if (success.HasValue)
        {
            parts.Add(success.Value ? "[completed]" : "[failed]");
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

        return parts.Count == 0 ? "Tool call" : string.Join(" ", parts);
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

        var compact = Regex.Replace(value.Replace("\r", " ").Replace("\n", " "), @"\s+", " ");
        return compact.Trim();
    }

    private static string? NormalizeDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : Truncate(normalized, 2200);
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

        return NormalizeDetail(value.ToString(Formatting.Indented));
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
            .Select(value => value.Trim())
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

    private void FailPendingOperations(string message)
    {
        List<TaskCompletionSource<JToken?>> pendingRequests;
        ActiveTurnState? turnState;

        lock (_syncRoot)
        {
            pendingRequests = _pendingRequests.Values.ToList();
            _pendingRequests.Clear();
            turnState = _activeTurn;
            _threadId = null;
            _threadConfigKey = null;
            _threadLoaded = false;
            _skillsCacheKey = null;
            _serverInput = null;
            _serverProcess = null;
        }

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

        lock (_syncRoot)
        {
            process = _serverProcess;
            input = _serverInput;
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
        var options = string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal)
            ? BuildCommandApprovalOptions(parameters?["availableDecisions"] as JArray, parameters?["proposedExecpolicyAmendment"] as JArray)
            : BuildFileChangeApprovalOptions();

        return new CodexApprovalRequest
        {
            Method = method,
            ThreadId = parameters?["threadId"]?.Value<string>() ?? string.Empty,
            TurnId = parameters?["turnId"]?.Value<string>() ?? string.Empty,
            ItemId = parameters?["itemId"]?.Value<string>() ?? string.Empty,
            ApprovalId = parameters?["approvalId"]?.Value<string>(),
            Command = parameters?["command"]?.Value<string>(),
            WorkingDirectory = parameters?["cwd"]?.Value<string>(),
            Reason = parameters?["reason"]?.Value<string>(),
            GrantRoot = parameters?["grantRoot"]?.Value<string>(),
            ProposedExecpolicyLabel = parameters?["proposedExecpolicyAmendment"]?.Type == JTokenType.Array
                ? string.Join(" ", parameters["proposedExecpolicyAmendment"]!.Values<string>())
                : null,
            Options = options
        };
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

    private async Task<JToken> ResolveApprovalDecisionAsync(CodexApprovalRequest request)
    {
        var info = JsonConvert.SerializeObject(request, Formatting.None);
        if (!string.IsNullOrWhiteSpace(info))
        {
            PublishError("[approval] " + info + Environment.NewLine);
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
            PublishError("[approval] " + ex.Message + Environment.NewLine);
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
            PublishError("[user-input] " + ex.Message + Environment.NewLine);
            return new JObject { ["answers"] = new JObject() };
        }
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

    private static JToken GetDefaultDeclineDecision(string method)
    {
        return JValue.CreateString(string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal) ? "decline" : "cancel");
    }

    private static string BuildServerArguments(CodexExtensionSettings settings)
    {
        var args = new List<string> { "app-server", "--listen", "stdio://" };

        foreach (var line in BuildManagedMcpOverrideLines(settings))
        {
            args.Add("-c");
            args.Add(line);
        }

        if (!string.IsNullOrWhiteSpace(settings.RawTomlOverrides))
        {
            foreach (var line in settings.RawTomlOverrides.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                args.Add("-c");
                args.Add(line.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.AdditionalArguments))
        {
            foreach (var token in SplitArguments(settings.AdditionalArguments))
            {
                args.Add(token);
            }
        }

        return JoinArguments(args);
    }

    private static string BuildServerConfigKey(CodexExtensionSettings settings)
    {
        return string.Join("\n", new[]
        {
            ResolveExecutablePath(settings.CodexExecutablePath),
            string.Join("\n", BuildManagedMcpOverrideLines(settings)),
            settings.RawTomlOverrides ?? string.Empty,
            settings.AdditionalArguments ?? string.Empty,
            settings.EnvironmentVariables ?? string.Empty
        });
    }

    private static IEnumerable<string> BuildManagedMcpOverrideLines(CodexExtensionSettings settings)
    {
        if (settings.ManagedMcpServers is null)
        {
            yield break;
        }

        foreach (var server in settings.ManagedMcpServers)
        {
            if (server is null || !server.Enabled)
            {
                continue;
            }

            var name = (server.Name ?? string.Empty).Trim();
            if (!IsValidManagedMcpName(name))
            {
                continue;
            }

            if (string.Equals(server.TransportType, "url", StringComparison.OrdinalIgnoreCase))
            {
                var url = (server.Url ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                yield return "[mcp_servers." + name + "]";
                yield return "url = " + EncodeTomlString(url);
                continue;
            }

            var command = (server.Command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            yield return "[mcp_servers." + name + "]";
            yield return "command = " + EncodeTomlString(command);

            var args = SplitManagedMcpArguments(server.Arguments).ToList();
            if (args.Count > 0)
            {
                yield return "args = [" + string.Join(", ", args.Select(EncodeTomlString)) + "]";
            }
        }
    }

    private static bool IsValidManagedMcpName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-');
    }

    private static IEnumerable<string> SplitManagedMcpArguments(string? text)
    {
        return (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string EncodeTomlString(string value)
    {
        return "\"" + (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"") + "\"";
    }

    private static string BuildSkillScopeLabel(string path, string workingDirectory, string homeSkillsDirectory, bool isSystem)
    {
        if (isSystem)
        {
            return "System";
        }

        if (path.StartsWith(homeSkillsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return "Global";
        }

        if (path.StartsWith(workingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return "Workspace";
        }

        return "External";
    }

    private static string BuildThreadConfigKey(CodexExtensionSettings settings, string workingDirectory)
    {
        return string.Join("\n", new[]
        {
            workingDirectory,
            settings.DefaultModel ?? string.Empty,
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

    private static ProcessStartInfo BuildStartInfo(string executablePath, string arguments, string workingDirectory)
    {
        var resolvedWorkingDirectory = ResolveWorkingDirectory(workingDirectory);
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

    private static string ResolveExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = "codex.cmd";
        }

        if (!IsWindows())
        {
            return executablePath;
        }

        return string.Equals(executablePath, "codex", StringComparison.OrdinalIgnoreCase)
            ? "codex.cmd"
            : executablePath;
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

    private static string JoinArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Any(ch => char.IsWhiteSpace(ch) || ch == '"' || ch == '\\'))
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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

    private static string QuoteForCommandShell(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static bool IsWindows()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
    }

    private static IEnumerable<string> SplitArguments(string commandLine)
    {
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
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
        public string? TurnId { get; set; }
        public bool InterruptRequested { get; set; }

        public void TrySetResult(int result)
        {
            Completion.TrySetResult(result);
        }
    }
}
