using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Models;
using CodexVsix.ViewModels;
using Microsoft.Win32;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal sealed class CodexOfficialWebViewBridge : IDisposable
{
    private const string Channel = "codex-webview-ipc";
    private const string RpcPrefix = "vscode://codex/";
    private const string LegacySettingPrefix = "chatgpt.";
    private const string PersistedSettingPrefix = "setting:";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly CodexToolWindowViewModel _viewModel;
    private readonly CodexProcessService _processService;
    private readonly SolutionContextService _solutionContextService = new();
    private readonly ExtensionSettingsStore _settingsStore = new();
    private static readonly CodexWebViewStateStore SharedStateStore = new();
    private readonly CodexWebViewStateStore _stateStore = SharedStateStore;
    private readonly CodexGitService _gitService = new();
    private readonly CodexGitHubService _gitHubService = new();
    private readonly CodexAppServerRequestRelay _appServerRequestRelay;
    private readonly Func<JObject, Task<JObject?>> _appServerRequestForwarder;
    private readonly CodexWebViewPayloadLimiter _payloadLimiter = new();
    private readonly CodexWebViewHistoryWindowController _historyWindowController;
    private readonly CodexWebViewAutoCompactionCoordinator _autoCompactionCoordinator;
    private readonly Dictionary<string, JToken?> _sharedObjects = new(StringComparer.Ordinal)
    {
        ["host_config"] = new JObject
        {
            ["id"] = "local",
            ["display_name"] = "Local",
            ["kind"] = "local"
        }
    };
    private readonly Action<JObject> _postMessage;
    private readonly Action<string>? _openSettings;
    private readonly Action<JToken>? _broadcastQueryInvalidation;
    private readonly bool _isSettingsSurface;
    private readonly CodexDiffArtifactController _diffArtifacts;
    private readonly Action<string>? _routeChanged;
    private bool _disposed;

    public CodexOfficialWebViewBridge(
        CodexToolWindowViewModel viewModel,
        Action<JObject> postMessage,
        Action<string>? openSettings = null,
        Action<JToken>? broadcastQueryInvalidation = null,
        bool isSettingsSurface = false,
        Action<string>? routeChanged = null)
    {
        _viewModel = viewModel;
        _processService = viewModel.ProcessService;
        _postMessage = postMessage;
        _openSettings = openSettings;
        _broadcastQueryInvalidation = broadcastQueryInvalidation;
        _isSettingsSurface = isSettingsSurface;
        _routeChanged = routeChanged;
        _diffArtifacts = new CodexDiffArtifactController(
            Path.Combine(Path.GetTempPath(), "CodexVsix", "artifacts"),
            _solutionContextService.TryUpdateOpenTextDocument,
            path =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _solutionContextService.OpenFileInVisualStudio(path);
            });
        _appServerRequestRelay = new CodexAppServerRequestRelay(Post);
        _appServerRequestForwarder = _appServerRequestRelay.ForwardAsync;
        _historyWindowController = new CodexWebViewHistoryWindowController(PostHistoryWindowStatus);
        _autoCompactionCoordinator = new CodexWebViewAutoCompactionCoordinator(
            () => _viewModel.Settings.AutoCompactLongConversations,
            async (threadId, cancellationToken) =>
            {
                await _processService.InvokeAppServerRequestAsync(
                    _viewModel.Settings,
                    "thread/compact/start",
                    new JObject { ["threadId"] = threadId },
                    cancellationToken).ConfigureAwait(false);
            },
            LogInformation);
        _processService.MissingExecutableHandler ??= CodexCliInstaller.EnsureInstalledAsync;
        _processService.AppServerNotificationReceived += OnAppServerNotificationReceived;
    }

    public async Task HandleEnvelopeAsync(JObject envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope["channel"]?.Value<string>(), Channel, StringComparison.Ordinal)
            || envelope["message"] is not JObject message)
        {
            return;
        }

        var type = message["type"]?.Value<string>() ?? string.Empty;
        switch (type)
        {
            case "webview-ready":
            case "ready":
                await PostThemeAsync().ConfigureAwait(false);
                return;

            case "view-focused":
            case "tray-menu-threads-changed":
                return;

            case "query-cache-invalidate":
                HandleQueryCacheInvalidation(message);
                return;

            case "fetch":
                await HandleFetchAsync(message, cancellationToken).ConfigureAwait(false);
                return;

            case "fetch-stream":
                Post(new JObject
                {
                    ["type"] = "fetch-stream-complete",
                    ["requestId"] = message["requestId"]?.DeepClone()
                });
                return;

            case "cancel-fetch":
            case "cancel-fetch-stream":
                return;

            case "mcp-request":
            case "thread-prewarm-start":
                await HandleMcpRequestAsync(message, cancellationToken).ConfigureAwait(false);
                return;

            case "mcp-response":
                _appServerRequestRelay.TryHandleResponse(message);
                return;

            case "history-load-older":
                _historyWindowController.LoadOlder(message["threadId"]?.Value<string>());
                return;

            case "history-compact":
                await HandleHistoryCompactionAsync(message, cancellationToken).ConfigureAwait(false);
                return;

            case "history-auto-compact-set":
                _viewModel.Settings.AutoCompactLongConversations = message["enabled"]?.Value<bool>() == true;
                _settingsStore.Save(_viewModel.Settings);
                _historyWindowController.Republish(message["threadId"]?.Value<string>());
                return;

            case "recent-history-request":
                await HandleRecentHistoryRequestAsync(message, cancellationToken).ConfigureAwait(false);
                return;

            case "persisted-atom-sync-request":
                Post(new JObject
                {
                    ["type"] = "persisted-atom-sync",
                    ["state"] = _stateStore.GetSnapshot()
                });
                return;

            case "persisted-atom-update":
                HandlePersistedAtomUpdate(message);
                return;

            case "shared-object-subscribe":
                HandleSharedObjectSubscribe(message);
                return;

            case "shared-object-unsubscribe":
                return;

            case "shared-object-set":
                HandleSharedObjectSet(message);
                return;

            case "diagnostic-settings-request":
                PostDiagnosticLoggingState();
                return;

            case "diagnostic-settings-set":
                SetDiagnosticLogging(message["enabled"]?.Value<bool>() == true);
                return;

            case "navigate-to-route":
                var navigationPath = message["path"]?.Value<string>() ?? "/";
                if (IsSettingsRoute(navigationPath) && TryOpenExternalSettings(message, navigationPath))
                {
                    return;
                }

                _routeChanged?.Invoke(navigationPath);
                Post(new JObject
                {
                    ["type"] = "navigate-to-route",
                    ["path"] = navigationPath,
                    ["state"] = message["state"]?.DeepClone()
                });
                return;

            case "navigate-back":
            case "navigate-forward":
                Post(CreateHistoryNavigationMessage(type));
                return;

            case "navigate-in-new-editor-tab":
                var editorRoute = ResolveRoute(message, "/");
                _routeChanged?.Invoke(editorRoute);
                Post(new JObject
                {
                    ["type"] = "navigate-to-route",
                    ["path"] = editorRoute,
                    ["state"] = message["state"]?.DeepClone()
                });
                return;

            case "show-settings":
                var settingsPath = ResolveRoute(message, "/settings");
                if (TryOpenExternalSettings(message, settingsPath))
                {
                    return;
                }

                _routeChanged?.Invoke(settingsPath);
                Post(new JObject
                {
                    ["type"] = "navigate-to-route",
                    ["path"] = settingsPath,
                    ["state"] = message["state"]?.DeepClone()
                });
                return;

            case "open-vscode-command":
                await HandleOfficialCommandAsync(message).ConfigureAwait(false);
                return;

            case "open-in-browser":
                OpenInBrowser(message["url"]?.Value<string>());
                return;

            case "open-config-toml":
                await OpenConfigAsync(message["path"]?.Value<string>()).ConfigureAwait(false);
                return;

            case "open-keyboard-shortcuts":
                if (TryOpenExternalSettings(
                    new JObject { ["section"] = "keyboard-shortcuts" },
                    "/settings/keyboard-shortcuts"))
                {
                    return;
                }

                Post(new JObject { ["type"] = "navigate-to-route", ["path"] = "/settings/keyboard-shortcuts" });
                return;

            case "implement-todo":
                PostSharedObject(
                    "composer_prefill",
                    new JObject
                    {
                        ["text"] = message["todoText"]?.Value<string>() ?? "Implement the TODO comment",
                        ["cwd"] = JValue.CreateNull()
                    });
                return;

            case "show-diff":
            case "update-diff-if-open":
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                if (_disposed) return;
                var diffKey = message["conversationId"]?.Value<string>()
                    ?? message["threadId"]?.Value<string>() ?? string.Empty;
                if (type == "show-diff")
                    _diffArtifacts.Show(diffKey, message["unifiedDiff"]?.Value<string>());
                else
                    _diffArtifacts.UpdateIfOpen(diffKey, message["unifiedDiff"]?.Value<string>());
                return;

            case "show-plan-summary":
                await OpenTextArtifactAsync(
                    message["planContent"]?.Value<string>(),
                    ".md").ConfigureAwait(false);
                return;

            case "worker-request":
                await HandleWorkerRequestAsync(message, cancellationToken).ConfigureAwait(false);
                return;

            case "worker-request-cancel":
            case "hotkey-window-dismiss":
                return;

            case "log-message":
                LogWebViewMessage(
                    message["level"]?.Value<string>(),
                    message["message"]?.Value<string>() ?? message.ToString(Formatting.None));
                return;
        }
    }

    public void PostTheme()
    {
        PostThemeAsync().FileAndForget("CodexVsix/OfficialWebViewTheme");
    }

    private async Task PostThemeAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        Post(CodexVisualStudioTheme.Capture().ToMessage());
    }

    public void PostSharedObject(string key, JToken? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _sharedObjects[key] = value?.DeepClone();
        Post(new JObject
        {
            ["type"] = "shared-object-updated",
            ["key"] = key,
            ["value"] = value?.DeepClone()
        });
    }

    private void PostDiagnosticLoggingState()
    {
        Post(new JObject
        {
            ["type"] = "diagnostic-settings-state",
            ["enabled"] = _viewModel.Settings.EnableDiagnosticLogging
        });
    }

    private void SetDiagnosticLogging(bool enabled)
    {
        if (_viewModel.Settings.EnableDiagnosticLogging != enabled)
        {
            _viewModel.Settings.EnableDiagnosticLogging = enabled;
            _settingsStore.Save(_viewModel.Settings);
            _viewModel.NotifySettingsChangedFromOfficialWebView("diagnosticLoggingEnabled");
        }

        CodexDiagnosticLogger.Shared.SetEnabled(enabled);
        PostDiagnosticLoggingState();
    }

    public void HideHistoryWindow()
    {
        Post(new JObject
        {
            ["type"] = "history-window-state",
            ["isVisible"] = false
        });
    }

    public void EnableInteractiveServerRequests()
    {
        if (!_disposed)
        {
            _processService.AppServerRequestHandler = _appServerRequestForwarder;
        }
    }

    public void DisableInteractiveServerRequests()
    {
        if (ReferenceEquals(_processService.AppServerRequestHandler, _appServerRequestForwarder))
        {
            _processService.AppServerRequestHandler = null;
        }

        _appServerRequestRelay.CancelPending();
    }

    private async Task HandleFetchAsync(JObject message, CancellationToken cancellationToken)
    {
        var requestId = message["requestId"]?.Value<string>() ?? string.Empty;
        var url = message["url"]?.Value<string>() ?? string.Empty;
        try
        {
            JToken? result;
            if (url.StartsWith(RpcPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var method = url.Substring(RpcPrefix.Length).TrimEnd('/');
                LogInformation("host rpc: " + method);
                var parameters = ParseJsonBody(message["body"]?.Value<string>());
                result = await InvokeHostRpcAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await ProxyFetchAsync(message, cancellationToken).ConfigureAwait(false);
            }

            Post(new JObject
            {
                ["type"] = "fetch-response",
                ["requestId"] = requestId,
                ["responseType"] = "success",
                ["status"] = 200,
                ["headers"] = new JObject { ["content-type"] = "application/json" },
                ["bodyJsonString"] = SerializeResult(result)
            });
        }
        catch (Exception ex)
        {
            LogError("host fetch failed: " + DescribeFetchUrl(url) + ": " + ex.Message);
            Post(new JObject
            {
                ["type"] = "fetch-response",
                ["requestId"] = requestId,
                ["responseType"] = "error",
                ["status"] = 500,
                ["error"] = ex.Message,
                ["bodyJsonString"] = new JObject { ["error"] = ex.Message }.ToString(Formatting.None)
            });
        }
    }

    private async Task HandleMcpRequestAsync(JObject message, CancellationToken cancellationToken)
    {
        var hostId = message["hostId"]?.Value<string>() ?? "local";
        var request = message["request"] as JObject;
        if (request is null || string.IsNullOrWhiteSpace(request["method"]?.Value<string>()))
        {
            return;
        }

        var id = request["id"]?.DeepClone() ?? JValue.CreateString(Guid.NewGuid().ToString("N"));
        var method = request["method"]!.Value<string>()!;
        LogInformation("mcp request: " + method);
        try
        {
            var requestParameters = request["params"]?.DeepClone();
            var result = string.Equals(method, "initialize", StringComparison.Ordinal)
                ? CreateInitializeResult()
                : await InvokeAppServerForWebViewAsync(
                    method,
                    requestParameters,
                    cancellationToken).ConfigureAwait(false);
            var response = new JObject
            {
                ["id"] = id,
                ["result"] = NormalizeAppServerResult(method, result)
            };
            Post(new JObject
            {
                ["type"] = "mcp-response",
                ["hostId"] = hostId,
                ["message"] = response,
                ["response"] = response.DeepClone()
            });
        }
        catch (Exception ex)
        {
            LogError("mcp request failed: " + method + ": " + ex.Message);
            var response = new JObject
            {
                ["id"] = id,
                ["error"] = ex.Message
            };
            Post(new JObject
            {
                ["type"] = "mcp-response",
                ["hostId"] = hostId,
                ["message"] = response,
                ["response"] = response.DeepClone()
            });
        }
    }

    private async Task<JToken?> InvokeHostRpcAsync(string method, JToken? parameters, CancellationToken cancellationToken)
    {
        var values = UnwrapParameters(parameters);
        switch (method)
        {
            case "ping":
                return new JObject { ["ok"] = true };

            case "get-configuration":
                return GetConfiguration(values);

            case "set-configuration":
                SetConfiguration(values);
                return new JObject { ["ok"] = true };

            case "get-settings":
                return new JObject { ["values"] = BuildSettingsValues() };

            case "get-setting":
                return new JObject { ["value"] = ReadSetting(ExtractKey(values)) };

            case "set-setting":
                WriteSetting(ExtractKey(values), values["value"]);
                return new JObject { ["ok"] = true };

            case "get-global-state": {
                var key = ExtractKey(values);
                return string.IsNullOrWhiteSpace(key)
                    ? _stateStore.GetSnapshot()
                    : new JObject { ["value"] = _stateStore.Get(key!) };
            }

            case "set-global-state":
                _stateStore.Set(ExtractKey(values) ?? string.Empty, values["value"]);
                return new JObject { ["ok"] = true };

            case "list-pinned-threads":
                return new JObject
                {
                    ["threadIds"] = _stateStore.Get("pinnedThreadIds") as JArray ?? new JArray()
                };

            case "set-pinned-threads-order":
                _stateStore.Set("pinnedThreadIds", values["threadIds"] as JArray ?? new JArray());
                return new JObject
                {
                    ["threadIds"] = values["threadIds"]?.DeepClone() ?? new JArray()
                };

            case "extension-info":
                return new JObject
                {
                    ["appName"] = "Codex",
                    ["displayName"] = "Codex",
                    ["extensionId"] = "openai.chatgpt",
                    ["extensionVersion"] = CodexOfficialWebViewShell.OfficialExtensionVersion,
                    ["name"] = "chatgpt",
                    ["publisher"] = "openai",
                    ["version"] = CodexOfficialWebViewShell.OfficialExtensionVersion,
                    ["host"] = "visual-studio-2026"
                };

            case "os-info":
                return new JObject
                {
                    ["arch"] = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                    ["platform"] = "win32",
                    ["release"] = Environment.OSVersion.VersionString,
                    ["homedir"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ["tmpdir"] = Path.GetTempPath()
                };

            case "active-workspace-roots":
            case "workspace-root-options":
                return await BuildWorkspaceRootsAsync().ConfigureAwait(false);

            case "locale-info": {
                var locale = GetLocale();
                return new JObject { ["ideLocale"] = locale, ["systemLocale"] = CultureInfo.CurrentUICulture.Name };
            }

            case "openai-api-key":
                return BuildOpenAiApiKeyResponse();

            case "read-file":
                return ReadTextFile(values);

            case "read-file-binary":
                return ReadBinaryFile(values);

            case "read-file-metadata":
                return ReadFileMetadata(values);

            case "paths-exist":
                return PathsExist(values);

            case "pick-files":
                return await PickFilesAsync(values, allowMultiple: true).ConfigureAwait(false);

            case "pick-file":
                return await PickFilesAsync(values, allowMultiple: false).ConfigureAwait(false);

            case "ide-context":
                return await BuildIdeContextAsync(values).ConfigureAwait(false);

            case "add-context-file":
                return AddContextFile(values);

            case "open-file":
                return await OpenFileAsync(values).ConfigureAwait(false);

            case "mcp-codex-config":
                return new JObject { ["path"] = _solutionContextService.GetCodexConfigPath() };

            case "codex-home":
            {
                var codexHome = CodexEnvironmentPathHelper.GetCodexHomeDirectory(_viewModel.Settings.EnvironmentVariables);
                return new JObject { ["path"] = codexHome, ["codexHome"] = codexHome };
            }

            case "home-directory":
            {
                var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return new JObject { ["path"] = homeDirectory, ["homeDirectory"] = homeDirectory };
            }

            case "has-custom-cli-executable":
                return new JObject
                {
                    ["hasCustomCliExecutable"] = !string.IsNullOrWhiteSpace(_viewModel.Settings.CodexExecutablePath)
                        && !string.Equals(_viewModel.Settings.CodexExecutablePath, "codex.cmd", StringComparison.OrdinalIgnoreCase)
                };

            case "git-origins":
            case "git-merge-base":
            case "git-create-branch":
            case "git-checkout-branch":
            case "git-push":
            case "apply-patch":
            case "codex-worktrees":
            case "list-worktrees":
            case "prepare-worktree-snapshot":
            case "upload-worktree-snapshot":
                return await _gitService.HandleHostRequestAsync(
                    method,
                    values,
                    ResolveWorkingDirectory(),
                    cancellationToken).ConfigureAwait(false);

            case "resolve-worktree-for-thread":
                return new JObject { ["worktree"] = JValue.CreateNull() };

            case "gh-cli-status":
            case "gh-current-user":
            case "gh-pr-create":
            case "gh-pr-board":
            case "gh-pr-body":
            case "gh-pr-checks":
            case "gh-pr-comments":
            case "gh-pr-status":
            case "gh-pr-diff":
            case "gh-pr-merge":
            case "gh-pr-update":
            case "gh-pr-comment":
                return await _gitHubService.HandleAsync(
                    method,
                    values,
                    ResolveWorkingDirectory(),
                    cancellationToken).ConfigureAwait(false);

            case "send-cli-request-for-host": {
                var cliMethod = values["method"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(cliMethod))
                {
                    throw new InvalidOperationException("A Codex app-server method is required.");
                }

                var result = await InvokeAppServerForWebViewAsync(
                    cliMethod!,
                    values["params"]?.DeepClone(),
                    cancellationToken).ConfigureAwait(false);
                return NormalizeAppServerResult(cliMethod!, result);
            }

            case "refresh-recent-conversations-for-host": {
                var result = await InvokeAppServerForWebViewAsync(
                    "thread/list",
                    BuildRecentConversationRefreshParams(values),
                    cancellationToken).ConfigureAwait(false);
                return NormalizeAppServerResult("thread/list", result);
            }

            case "account-info":
                return await TryInvokeAppServerAsync("account/read", values, cancellationToken).ConfigureAwait(false);

            case "read-config-for-host":
                return await TryInvokeAppServerAsync("config/read", values, cancellationToken).ConfigureAwait(false);

            case "get-config-requirements-for-host":
                return await TryInvokeAppServerAsync("configRequirements/read", values, cancellationToken).ConfigureAwait(false);

            case "list-mcp-server-status":
                return await TryInvokeAppServerAsync("mcpServerStatus/list", values, cancellationToken).ConfigureAwait(false);

            case "read-mcp-resource":
                return await TryInvokeAppServerAsync("mcpServer/resource/read", values, cancellationToken).ConfigureAwait(false);

            case "read-plugin-skill":
                return await TryInvokeAppServerAsync("plugin/skill/read", values, cancellationToken).ConfigureAwait(false);

            case "list-hooks-for-host":
                return await TryInvokeAppServerAsync("hooks/list", values, cancellationToken).ConfigureAwait(false);

            case "ipc-request":
                return await HandleIpcRequestAsync(values, cancellationToken).ConfigureAwait(false);

            case "start-conversation":
                return await StartConversationAsync(values, cancellationToken).ConfigureAwait(false);

            case "thread-follower-compact-thread":
                return await CompactThreadAsync(values, cancellationToken).ConfigureAwait(false);

            case "thread-follower-set-thread":
            case "thread-follower-start-turn":
            case "thread-follower-steer-turn":
            case "thread-follower-interrupt-turn":
                return new JObject { ["ok"] = true, ["method"] = method };

            case "is-copilot-api-available":
                return new JObject { ["available"] = false };

            case "get-copilot-api-proxy-info":
            case "fast-mode-rollout-metrics":
                return JValue.CreateNull();

            case "recommended-skills":
                return new JObject { ["skills"] = new JArray(), ["data"] = new JArray() };

            case "install-recommended-skill":
                return new JObject { ["ok"] = false, ["unavailable"] = true };

            case "developer-instructions":
                return new JObject { ["instructions"] = string.Empty };

            case "open-in-targets": {
                var preferredTarget = _stateStore.Get("preferredOpenTarget")?.Value<string>() ?? "editor";
                var targets = new JArray(
                    new JObject
                    {
                        ["id"] = "visual-studio",
                        ["target"] = "editor",
                        ["label"] = "Visual Studio",
                        ["kind"] = "editor",
                        ["icon"] = JValue.CreateNull(),
                        ["hidden"] = false
                    },
                    new JObject
                    {
                        ["id"] = "system-default",
                        ["target"] = "systemDefault",
                        ["label"] = "System default",
                        ["kind"] = "native",
                        ["icon"] = JValue.CreateNull(),
                        ["hidden"] = false
                    },
                    new JObject
                    {
                        ["id"] = "file-manager",
                        ["target"] = "fileManager",
                        ["label"] = "File Explorer",
                        ["kind"] = "native",
                        ["icon"] = JValue.CreateNull(),
                        ["hidden"] = false
                    });
                return new JObject
                {
                    ["mode"] = "editor",
                    ["preferredTarget"] = preferredTarget,
                    ["availableTargets"] = new JArray("editor", "systemDefault", "fileManager"),
                    ["targets"] = targets
                };
            }

            case "set-preferred-app": {
                var target = values["target"]?.Value<string>()
                    ?? values["preferredTarget"]?.Value<string>()
                    ?? values["appId"]?.Value<string>()
                    ?? values["id"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    _stateStore.Set("preferredOpenTarget", target);
                }
                return new JObject
                {
                    ["success"] = !string.IsNullOrWhiteSpace(target),
                    ["preferredTarget"] = string.IsNullOrWhiteSpace(target) ? JValue.CreateNull() : target
                };
            }

            case "terminal-shell-options": {
                var shells = new JArray(
                    new JObject
                    {
                        ["id"] = "powershell",
                        ["label"] = "PowerShell",
                        ["path"] = "powershell.exe",
                        ["args"] = new JArray("-NoLogo")
                    },
                    new JObject
                    {
                        ["id"] = "pwsh",
                        ["label"] = "PowerShell 7",
                        ["path"] = "pwsh.exe",
                        ["args"] = new JArray("-NoLogo")
                    },
                    new JObject
                    {
                        ["id"] = "cmd",
                        ["label"] = "Command Prompt",
                        ["path"] = "cmd.exe",
                        ["args"] = new JArray()
                    });
                return new JObject
                {
                    ["defaultShell"] = "powershell",
                    ["availableShells"] = shells.DeepClone(),
                    ["shells"] = shells,
                    ["options"] = shells.DeepClone()
                };
            }

            case "thread-terminal-snapshot":
                return new JObject
                {
                    ["session"] = JValue.CreateNull(),
                    ["terminal"] = JValue.CreateNull(),
                    ["cwd"] = values["cwd"]?.Value<string>()
                        ?? values["workspaceRoot"]?.Value<string>()
                        ?? ResolveWorkingDirectory(),
                    ["scrollback"] = string.Empty
                };

            case "local-environments":
                return new JObject
                {
                    ["environments"] = new JArray(
                        new JObject
                        {
                            ["id"] = "local",
                            ["displayName"] = "Local",
                            ["kind"] = "local",
                            ["cwd"] = ResolveWorkingDirectory()
                        })
                };

            case "local-environment":
                return new JObject
                {
                    ["id"] = "local",
                    ["displayName"] = "Local",
                    ["kind"] = "local",
                    ["cwd"] = ResolveWorkingDirectory()
                };

            case "local-environment-config":
            case "worktree-shell-environment-config":
                return new JObject
                {
                    ["type"] = "success",
                    ["environment"] = new JObject(),
                    ["shellEnvironment"] = new JObject()
                };

            case "local-environment-config-save":
                return new JObject { ["success"] = false, ["unavailable"] = true };

            case "queued-follow-up-send-lock-acquire":
            case "queued-follow-up-send-lock-release":
                return new JObject { ["acquired"] = false, ["released"] = false };

            case "confirm-trace-recording-start":
            case "cancel-trace-recording-start":
            case "submit-trace-recording-details":
                return new JObject { ["success"] = false };

            case "chrome-native-host-install":
            case "chrome-native-host-uninstall":
                return new JObject
                {
                    ["success"] = false,
                    ["unavailable"] = true,
                    ["reason"] = "Chrome native-host integration is not available in Visual Studio."
                };

            case "set-vs-context":
            case "write-config-value":
            case "batch-write-config-value":
            case "set-thread-title":
                return new JObject { ["ok"] = true };

            case "generate-thread-title":
                return new JObject { ["title"] = BuildLocalThreadTitle(values["prompt"]?.Value<string>()) };

            case "flow-list-workflows":
                return new JObject { ["workflows"] = new JArray() };

            case "flow-list-workflow-patterns":
                return new JObject { ["patterns"] = new JArray() };

            case "flow-ai-authoring-spec":
                return new JObject { ["available"] = false, ["unavailable"] = true };

            case "flow-create-workflow-from-ai-authoring-draft":
            case "flow-plan-dynamic-workflow":
            case "flow-start-workflow":
            case "flow-run-dynamic-workflow":
                return new JObject { ["success"] = false, ["unavailable"] = true };

            case "refresh-remote-connections":
            case "discover-remote-ssh-connections":
            case "refresh-remote-control-connections":
                return new JObject { ["connections"] = new JArray() };

            case "set-remote-control-connections-enabled":
                return new JObject { ["enabled"] = false, ["connections"] = new JArray() };

            case "add-remote-connection":
                return new JObject { ["success"] = false, ["unavailable"] = true };

            case "fork-conversation-from-latest":
            case "fork-conversation-from-turn":
                return "thread-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

            default:
                LogWarning("unsupported host rpc: " + method);
                return new JObject
                {
                    ["ok"] = true,
                    ["unsupported"] = true,
                    ["method"] = method
                };
        }
    }

    private async Task<JToken?> TryInvokeAppServerAsync(string method, JToken? parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await _processService.InvokeAppServerRequestAsync(
                _viewModel.Settings,
                method,
                parameters,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return new JObject();
        }
    }

    private async Task<JToken?> HandleIpcRequestAsync(JObject values, CancellationToken cancellationToken)
    {
        var source = values["method"] is not null
            ? values
            : values["params"] as JObject ?? values;
        var method = source["method"]?.Value<string>();
        var requestId = source["requestId"]?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(method))
        {
            return new JObject
            {
                ["requestId"] = requestId,
                ["type"] = "response",
                ["resultType"] = "success",
                ["result"] = new JObject { ["ok"] = true, ["unsupported"] = true }
            };
        }

        try
        {
            LogInformation("ipc request: " + method);
            var requestParameters = source["params"]?.DeepClone();
            var result = string.Equals(method, "initialize", StringComparison.Ordinal)
                ? CreateInitializeResult()
                : await InvokeAppServerForWebViewAsync(
                    method!,
                    requestParameters,
                    cancellationToken).ConfigureAwait(false);
            return new JObject
            {
                ["requestId"] = requestId,
                ["type"] = "response",
                ["resultType"] = "success",
                ["result"] = NormalizeAppServerResult(method!, result)
            };
        }
        catch (Exception ex)
        {
            LogError("ipc request failed: " + method + ": " + ex.Message);
            return new JObject
            {
                ["requestId"] = requestId,
                ["type"] = "response",
                ["resultType"] = "error",
                ["error"] = ex.Message
            };
        }
    }

    private async Task<JToken?> StartConversationAsync(JObject values, CancellationToken cancellationToken)
    {
        HideHistoryWindow();
        var cwd = values["cwd"]?.Value<string>() ?? ResolveWorkingDirectory();
        var parameters = (JObject)values.DeepClone();
        parameters.Remove("hostId");
        parameters.Remove("preparePrimaryRuntimeForFirstTurn");
        parameters["cwd"] = cwd;
        parameters["input"] ??= new JArray();
        parameters["workspaceRoots"] ??= new JArray(cwd);
        parameters["threadSource"] ??= "user";

        if (parameters["approvalPolicy"] is null
            && parameters["permissions"] is JObject permissions
            && permissions["approvalPolicy"] is not null)
        {
            parameters["approvalPolicy"] = permissions["approvalPolicy"]!.DeepClone();
        }

        var enrichedParameters = CodexRuntimeIdentityContext.EnrichRequest(
            "thread/start",
            parameters,
            _viewModel.Settings.DefaultModel,
            _viewModel.Settings.ReasoningEffort);
        var result = await _processService.InvokeAppServerRequestAsync(
            _viewModel.Settings,
            "thread/start",
            enrichedParameters,
            cancellationToken).ConfigureAwait(false);
        return result?["thread"]?["id"]?.Value<string>()
            ?? "thread-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private async Task<JToken?> CompactThreadAsync(JObject values, CancellationToken cancellationToken)
    {
        var threadId = values["threadId"]?.Value<string>() ?? values["sessionId"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return new JObject { ["ok"] = false };
        }

        return await _processService.InvokeAppServerRequestAsync(
            _viewModel.Settings,
            "thread/compact/start",
            new JObject { ["threadId"] = threadId },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JToken?> InvokeAppServerForWebViewAsync(
        string method,
        JToken? parameters,
        CancellationToken cancellationToken)
    {
        var enrichedParameters = CodexRuntimeIdentityContext.EnrichRequest(
            method,
            parameters,
            _viewModel.Settings.DefaultModel,
            _viewModel.Settings.ReasoningEffort);
        await _historyWindowController.WaitForCapacityAsync(method, enrichedParameters, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _processService.InvokeAppServerRequestAsync(
                _viewModel.Settings,
                method,
                enrichedParameters,
                cancellationToken).ConfigureAwait(false);
            var limitedResult = _payloadLimiter.LimitAppServerResult(method, result);
            _historyWindowController.ObserveResponse(method, enrichedParameters, limitedResult);
            return limitedResult;
        }
        catch
        {
            _historyWindowController.ObserveFailure(method, enrichedParameters);
            throw;
        }
    }

    private async Task HandleHistoryCompactionAsync(JObject message, CancellationToken cancellationToken)
    {
        var threadId = message["threadId"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        Post(new JObject
        {
            ["type"] = "history-compaction-state",
            ["threadId"] = threadId,
            ["isCompacting"] = true
        });

        try
        {
            await CompactThreadAsync(
                new JObject { ["threadId"] = threadId },
                cancellationToken).ConfigureAwait(false);
            Post(new JObject
            {
                ["type"] = "history-compaction-state",
                ["threadId"] = threadId,
                ["isCompacting"] = false,
                ["succeeded"] = true
            });
        }
        catch (Exception ex)
        {
            LogError("manual conversation compaction failed: " + ex.Message);
            Post(new JObject
            {
                ["type"] = "history-compaction-state",
                ["threadId"] = threadId,
                ["isCompacting"] = false,
                ["succeeded"] = false,
                ["error"] = ex.Message
            });
        }
    }

    private async Task HandleRecentHistoryRequestAsync(JObject message, CancellationToken cancellationToken)
    {
        var requestId = LimitHistoryText(message["requestId"], 128);
        try
        {
            var result = await InvokeAppServerForWebViewAsync(
                "thread/list",
                BuildRecentConversationRefreshParams(new JObject()),
                cancellationToken).ConfigureAwait(false);
            var response = BuildRecentHistoryResponse(result);
            response["type"] = "recent-history-response";
            response["requestId"] = requestId is null ? JValue.CreateNull() : requestId;
            Post(response);
        }
        catch (Exception ex)
        {
            LogError("recent local history failed: " + ex.Message);
            Post(new JObject
            {
                ["type"] = "recent-history-response",
                ["requestId"] = requestId is null ? JValue.CreateNull() : requestId,
                ["items"] = new JArray(),
                ["hasMore"] = false,
                ["error"] = "LOCAL_HISTORY_UNAVAILABLE"
            });
        }
    }

    private JObject GetConfiguration(JObject values)
    {
        var key = ExtractKey(values);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return new JObject { ["value"] = ReadSetting(key) };
        }

        var settings = BuildSettingsValues();
        return new JObject { ["config"] = settings, ["values"] = settings.DeepClone() };
    }

    private void SetConfiguration(JObject values)
    {
        foreach (var property in values.Properties())
        {
            WriteSetting(property.Name, property.Value);
        }
    }

    private JObject BuildSettingsValues()
    {
        return BuildSettingsValues(_viewModel.Settings, _stateStore.GetSnapshot());
    }

    internal static JObject BuildSettingsValues(CodexExtensionSettings settings, JObject persistedState)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (persistedState is null)
        {
            throw new ArgumentNullException(nameof(persistedState));
        }

        var values = new JObject();
        foreach (var property in persistedState.Properties())
        {
            if (!property.Name.StartsWith(PersistedSettingPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var settingKey = NormalizeSettingKey(property.Name.Substring(PersistedSettingPrefix.Length));
            if (settingKey.Length > 0)
            {
                SetSettingAliases(values, settingKey, property.Value);
            }
        }

        SetSettingAliases(values, "commentCodeLensEnabled", true);
        SetSettingAliases(values, "cliExecutable", settings.CodexExecutablePath);
        SetSettingAliases(values, "openOnStartup", settings.OpenOnStartup);
        SetSettingAliases(values, "autoCompactLongConversations", settings.AutoCompactLongConversations);
        SetSettingAliases(values, "diagnosticLoggingEnabled", settings.EnableDiagnosticLogging);
        SetSettingAliases(values, "followUpQueueMode", settings.FollowUpQueueMode);
        SetSettingAliases(values, "composerEnterBehavior", settings.ComposerEnterBehavior);
        SetSettingAliases(values, "reviewDelivery", settings.ReviewDelivery);
        SetSettingAliases(
            values,
            "localeOverride",
            string.IsNullOrWhiteSpace(settings.LanguageOverride)
                ? JValue.CreateNull()
                : new JValue(settings.LanguageOverride));
        SetSettingAliases(values, "runCodexInWindowsSubsystemForLinux", false);
        return values;
    }

    private static void SetSettingAliases(JObject values, string key, JToken? value)
    {
        var settingValue = value?.DeepClone() ?? JValue.CreateNull();
        values[key] = settingValue;
        values[LegacySettingPrefix + key] = settingValue.DeepClone();
    }

    internal static string NormalizeSettingKey(string? key)
    {
        var normalized = key?.Trim() ?? string.Empty;
        return normalized.StartsWith(LegacySettingPrefix, StringComparison.Ordinal)
            ? normalized.Substring(LegacySettingPrefix.Length)
            : normalized;
    }

    private JToken? ReadSetting(string? key)
    {
        var normalizedKey = NormalizeSettingKey(key);
        if (normalizedKey.Length == 0)
        {
            return null;
        }

        return normalizedKey switch
        {
            "commentCodeLensEnabled" => true,
            "cliExecutable" => _viewModel.Settings.CodexExecutablePath,
            "openOnStartup" => _viewModel.Settings.OpenOnStartup,
            "autoCompactLongConversations" => _viewModel.Settings.AutoCompactLongConversations,
            "diagnosticLoggingEnabled" => _viewModel.Settings.EnableDiagnosticLogging,
            "followUpQueueMode" => _viewModel.Settings.FollowUpQueueMode,
            "composerEnterBehavior" => _viewModel.Settings.ComposerEnterBehavior,
            "reviewDelivery" => _viewModel.Settings.ReviewDelivery,
            "localeOverride" => string.IsNullOrWhiteSpace(_viewModel.Settings.LanguageOverride)
                ? null
                : _viewModel.Settings.LanguageOverride,
            "runCodexInWindowsSubsystemForLinux" => false,
            _ => ReadPersistedSetting(key!, normalizedKey)
        };
    }

    private JToken? ReadPersistedSetting(string requestedKey, string normalizedKey)
    {
        var value = _stateStore.Get(PersistedSettingPrefix + normalizedKey);
        return value ?? (string.Equals(requestedKey, normalizedKey, StringComparison.Ordinal)
            ? null
            : _stateStore.Get(PersistedSettingPrefix + requestedKey));
    }

    private void WriteSetting(string? key, JToken? value)
    {
        var normalizedKey = NormalizeSettingKey(key);
        if (normalizedKey.Length == 0)
        {
            return;
        }

        switch (normalizedKey)
        {
            case "cliExecutable":
                _viewModel.Settings.CodexExecutablePath = value?.Value<string>() ?? "codex.cmd";
                break;
            case "openOnStartup":
                _viewModel.Settings.OpenOnStartup = value?.Value<bool>() == true;
                break;
            case "autoCompactLongConversations":
                _viewModel.Settings.AutoCompactLongConversations = value?.Value<bool>() == true;
                break;
            case "diagnosticLoggingEnabled":
                _viewModel.Settings.EnableDiagnosticLogging = value?.Value<bool>() == true;
                CodexDiagnosticLogger.Shared.SetEnabled(_viewModel.Settings.EnableDiagnosticLogging);
                break;
            case "followUpQueueMode":
                _viewModel.Settings.FollowUpQueueMode = NormalizeEnumSetting(
                    value,
                    "queue",
                    "queue",
                    "steer",
                    "interrupt");
                break;
            case "composerEnterBehavior":
                _viewModel.Settings.ComposerEnterBehavior = NormalizeEnumSetting(
                    value,
                    "enter",
                    "enter",
                    "cmdIfMultiline",
                    "ctrlEnter");
                break;
            case "reviewDelivery":
                _viewModel.Settings.ReviewDelivery = NormalizeEnumSetting(
                    value,
                    "inline",
                    "inline",
                    "detached");
                break;
            case "localeOverride":
                _viewModel.SelectedLanguageTag = value?.Value<string>() ?? string.Empty;
                break;
            default:
                _stateStore.Set(PersistedSettingPrefix + normalizedKey, value);
                if (!string.Equals(key, normalizedKey, StringComparison.Ordinal))
                {
                    _stateStore.Set(PersistedSettingPrefix + key, null);
                }

                return;
        }

        ClearPersistedSettingAliases(normalizedKey);
        _settingsStore.Save(_viewModel.Settings);
        _viewModel.NotifySettingsChangedFromOfficialWebView(normalizedKey);
    }

    private void ClearPersistedSettingAliases(string normalizedKey)
    {
        _stateStore.Set(PersistedSettingPrefix + normalizedKey, null);
        _stateStore.Set(PersistedSettingPrefix + LegacySettingPrefix + normalizedKey, null);
    }

    private static string NormalizeEnumSetting(JToken? value, string fallback, params string[] allowedValues)
    {
        var candidate = value?.Value<string>();
        return allowedValues.Any(allowed => string.Equals(candidate, allowed, StringComparison.Ordinal))
            ? candidate!
            : fallback;
    }

    private JObject ReadTextFile(JObject values)
    {
        var path = ResolveFilePath(values);
        return new JObject
        {
            ["content"] = File.ReadAllText(path),
            ["encoding"] = "utf8"
        };
    }

    internal static JObject ReadBinaryFile(JObject values)
    {
        var encoded = Convert.ToBase64String(CodexImageInputInliner.ReadBoundedFile(
            ResolveFilePath(values), 50 * 1024 * 1024, allowEmpty: true));
        return new JObject
        {
            ["contentsBase64"] = encoded,
            ["data"] = encoded
        };
    }

    private JObject ReadFileMetadata(JObject values)
    {
        var info = new FileInfo(ResolveFilePath(values));
        return new JObject
        {
            ["size"] = info.Length,
            ["mtimeMs"] = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds()
        };
    }

    private JObject PathsExist(JObject values)
    {
        var paths = values["paths"] as JArray ?? new JArray();
        return new JObject
        {
            ["existingPaths"] = new JArray(paths.Values<string>().Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path))))
        };
    }

    private async Task<JObject> PickFilesAsync(JObject values, bool allowMultiple)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dialog = new OpenFileDialog
        {
            Multiselect = allowMultiple,
            Title = values["pickerTitle"]?.Value<string>() ?? (allowMultiple ? "Select files" : "Select file"),
            Filter = values["imagesOnly"]?.Value<bool>() == true
                ? "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp|All files|*.*"
                : "All files|*.*"
        };
        var accepted = dialog.ShowDialog() == true;
        var files = accepted
            ? dialog.FileNames.Select(ToPickedFile).ToArray()
            : Array.Empty<JObject>();
        return allowMultiple
            ? new JObject { ["files"] = new JArray(files) }
            : new JObject { ["file"] = files.FirstOrDefault() };
    }

    private async Task<JObject> BuildWorkspaceRootsAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var root = _solutionContextService.TryGetSolutionDirectory() ?? ResolveWorkingDirectory();
        return new JObject
        {
            ["roots"] = new JArray(root),
            ["labels"] = new JObject { [root] = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) }
        };
    }

    private async Task<JObject> BuildIdeContextAsync(JObject values)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var workspaceRoot = values["workspaceRoot"]?.Value<string>()
            ?? _solutionContextService.TryGetSolutionDirectory()
            ?? ResolveWorkingDirectory();
        var activeFile = _solutionContextService.GetActiveDocumentPath();
        var activeSelection = _solutionContextService.GetActiveSelectionSnippetForPrompt();
        var openDocuments = _solutionContextService.GetOpenDocumentPathsForIdeContext();
        return CodexWebViewIdeContext.BuildResponse(
            workspaceRoot,
            activeFile,
            activeSelection,
            openDocuments);
    }

    private JObject AddContextFile(JObject values)
    {
        var path = values["path"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return new JObject { ["success"] = false };
        }

        Post(new JObject
        {
            ["type"] = "add-context-file",
            ["file"] = ToPickedFile(path!)
        });
        return new JObject { ["success"] = true };
    }

    private async Task<JObject> OpenFileAsync(JObject values)
    {
        var path = values["path"]?.Value<string>() ?? values["uri"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return new JObject { ["success"] = false };
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _solutionContextService.OpenFileInVisualStudio(
            ResolveFilePath(path!),
            values["line"]?.Value<int?>(),
            values["column"]?.Value<int?>());
        return new JObject { ["success"] = true };
    }

    private async Task HandleOfficialCommandAsync(JObject message)
    {
        var command = message["command"]?.Value<string>();
        switch (command)
        {
            case "chatgpt.newChat":
            case "chatgpt.newCodexPanel":
                HideHistoryWindow();
                Post(new JObject
                {
                    ["type"] = "navigate-to-route",
                    ["path"] = "/",
                    ["state"] = new JObject { ["focusComposerNonce"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                });
                break;
            case "chatgpt.openSidebar":
            case "chatgpt.openCommandMenu":
                Post(new JObject
                {
                    ["type"] = "navigate-to-route",
                    ["path"] = "/",
                    ["state"] = new JObject { ["focusComposerNonce"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                });
                break;
            case "chatgpt.openSettings":
                if (!TryOpenExternalSettings(
                        new JObject { ["section"] = "general-settings" },
                        "/settings/general-settings"))
                {
                    Post(new JObject
                    {
                        ["type"] = "navigate-to-route",
                        ["path"] = "/settings/general-settings"
                    });
                }
                break;
            default:
                await Task.CompletedTask;
                break;
        }
    }

    private void HandlePersistedAtomUpdate(JObject message)
    {
        var key = message["key"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var deleted = message["deleted"]?.Value<bool>() == true;
        var value = deleted ? null : message["value"]?.DeepClone();
        _stateStore.Set(key!, value);
        Post(new JObject
        {
            ["type"] = "persisted-atom-updated",
            ["key"] = key,
            ["value"] = value,
            ["deleted"] = deleted
        });
    }

    private void HandleQueryCacheInvalidation(JObject message)
    {
        var queryKey = NormalizeQueryKeyForBroadcast(
            message["queryKey"] ?? message["params"]?["queryKey"]);
        if (queryKey is not null)
        {
            _broadcastQueryInvalidation?.Invoke(queryKey);
        }
    }

    private bool TryOpenExternalSettings(JObject message, string settingsPath)
    {
        if (_isSettingsSurface || _openSettings is null)
        {
            return false;
        }

        _openSettings(ResolveSettingsSection(message, settingsPath));
        return true;
    }

    private void HandleSharedObjectSubscribe(JObject message)
    {
        var key = message["key"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(key) && _sharedObjects.TryGetValue(key!, out var value))
        {
            PostSharedObject(key!, value);
        }
    }

    private void HandleSharedObjectSet(JObject message)
    {
        var key = message["key"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(key))
        {
            _sharedObjects[key!] = message["value"]?.DeepClone();
        }
    }

    private async Task HandleWorkerRequestAsync(JObject message, CancellationToken cancellationToken)
    {
        var request = message["request"] as JObject;
        if (request?["id"] is null)
        {
            return;
        }

        var method = request["method"]?.Value<string>() ?? string.Empty;
        LogInformation("worker request: " + method);
        JObject response;
        try
        {
            var values = request["params"] as JObject ?? new JObject();
            var value = await _gitService.HandleWorkerRequestAsync(
                method,
                values,
                ResolveWorkingDirectory(),
                cancellationToken).ConfigureAwait(false);
            response = new JObject
            {
                ["id"] = request["id"]!.DeepClone(),
                ["method"] = request["method"]?.DeepClone(),
                ["result"] = new JObject
                {
                    ["type"] = "ok",
                    ["value"] = value?.DeepClone() ?? JValue.CreateNull()
                }
            };
        }
        catch (Exception ex)
        {
            LogError("worker request failed: " + method + ": " + ex.Message);
            response = new JObject
            {
                ["id"] = request["id"]!.DeepClone(),
                ["method"] = request["method"]?.DeepClone(),
                ["result"] = new JObject
                {
                    ["type"] = "error",
                    ["error"] = new JObject { ["message"] = ex.Message }
                }
            };
        }

        Post(new JObject
        {
            ["type"] = "worker-response",
            ["workerId"] = message["workerId"]?.Value<string>() ?? string.Empty,
            ["response"] = response
        });
    }

    private async Task<JToken?> ProxyFetchAsync(JObject message, CancellationToken cancellationToken)
    {
        var url = message["url"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("A fetch URL is required.");
        }

        var resolvedUri = ResolveProxyUri(url!);
        var syntheticResponse = TryBuildSyntheticFetchResponse(resolvedUri);
        if (syntheticResponse is not null)
        {
            return syntheticResponse;
        }

        using var request = new HttpRequestMessage(
            new HttpMethod(message["method"]?.Value<string>() ?? "GET"),
            resolvedUri);
        request.Headers.TryAddWithoutValidation("User-Agent", "codex_vscode");
        var body = message["body"]?.Value<string>();
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
        }

        if (message["headers"] is JObject headers)
        {
            foreach (var property in headers.Properties())
            {
                var value = property.Value.Value<string>();
                if (string.IsNullOrWhiteSpace(value)
                    || request.Headers.TryAddWithoutValidation(property.Name, value))
                {
                    continue;
                }

                request.Content?.Headers.TryAddWithoutValidation(property.Name, value);
            }
        }

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("HTTP " + (int)response.StatusCode + ": " + responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JValue.CreateNull();
        }

        try
        {
            return JToken.Parse(responseBody);
        }
        catch
        {
            return responseBody;
        }
    }

    internal static JToken? TryBuildSyntheticFetchResponse(Uri uri)
    {
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        if (string.Equals(uri.Host, "ab.chatgpt.com", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/v1/", StringComparison.Ordinal))
        {
            return BuildNoStatsigUpdatesResponse();
        }

        if (!string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (path.StartsWith("/ces/", StringComparison.Ordinal))
        {
            return BuildNoStatsigUpdatesResponse();
        }

        var whamIndex = path.IndexOf("/wham/", StringComparison.Ordinal);
        if (whamIndex < 0)
        {
            return null;
        }

        var whamPath = path.Substring(whamIndex);
        switch (whamPath)
        {
            case "/wham/accounts/check":
                return new JObject { ["account_ordering"] = new JArray(), ["accounts"] = new JArray() };
            case "/wham/tasks/list":
                return new JObject { ["items"] = new JArray(), ["cursor"] = JValue.CreateNull() };
            case "/wham/usage":
                return JValue.CreateNull();
            case "/wham/environments":
                return new JArray();
            case "/wham/onboarding/context":
            case "/wham/statsig/bootstrap":
                return new JObject();
            default:
                return whamPath.EndsWith("/mark_read", StringComparison.Ordinal)
                    ? new JObject { ["ok"] = true }
                    : JValue.CreateNull();
        }
    }

    internal static Uri ResolveProxyUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri)
            && (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return absoluteUri;
        }

        if (Uri.TryCreate(new Uri("https://chatgpt.com", UriKind.Absolute), url, out var resolvedUri)
            && (string.Equals(resolvedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolvedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return resolvedUri;
        }

        throw new InvalidOperationException("Only HTTP and HTTPS fetch URLs are supported.");
    }

    private static JObject BuildNoStatsigUpdatesResponse()
    {
        return new JObject
        {
            ["has_updates"] = false,
            ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private async Task OpenConfigAsync(string? path)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _solutionContextService.OpenFileInVisualStudio(
            string.IsNullOrWhiteSpace(path) ? _solutionContextService.GetCodexConfigPath() : ResolveFilePath(path!));
    }

    private async Task OpenTextArtifactAsync(string? content, string extension)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "CodexVsix", "artifacts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "codex-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + extension);
        File.WriteAllText(path, content);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _solutionContextService.OpenFileInVisualStudio(path);
    }

    private void OnAppServerNotificationReceived(string method, JToken? parameters)
    {
        if (_disposed || string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        _autoCompactionCoordinator.ObserveNotification(method, parameters);
        if (!_payloadLimiter.TryLimitNotification(method, parameters, out var limitedParameters))
        {
            return;
        }

        Post(new JObject
        {
            ["type"] = "mcp-notification",
            ["hostId"] = "local",
            ["method"] = method,
            ["params"] = limitedParameters
        });
    }

    private void PostHistoryWindowStatus(CodexWebViewHistoryWindowStatus status)
    {
        Post(new JObject
        {
            ["type"] = "history-window-state",
            ["threadId"] = status.ThreadId,
            ["loadedTurns"] = status.LoadedTurns,
            ["loadedBytes"] = status.LoadedBytes,
            ["isVisible"] = status.IsVisible,
            ["isLoading"] = status.IsLoading,
            ["canLoadMore"] = status.CanLoadMore,
            ["loadBatchSize"] = CodexWebViewHistoryWindowController.AdditionalTurnBudget,
            ["autoCompactEnabled"] = _viewModel.Settings.AutoCompactLongConversations
        });
    }

    private void Post(JObject message)
    {
        if (!_disposed)
        {
            _postMessage(message);
        }
    }

    private static JObject CreateInitializeResult()
    {
        return new JObject
        {
            ["protocolVersion"] = "1.0",
            ["serverCapabilities"] = new JObject(),
            ["userCapabilities"] = new JObject()
        };
    }

    internal static JToken NormalizeAppServerResult(string method, JToken? result)
    {
        var normalized = result?.DeepClone() ?? JValue.CreateNull();
        if (normalized is not JObject record)
        {
            return normalized;
        }

        switch (method)
        {
            case "conversation/list":
            case "thread/list": {
                var data = record["data"] as JArray
                    ?? record["threads"] as JArray
                    ?? record["conversations"] as JArray
                    ?? new JArray();
                record["data"] = data;
                record["threads"] ??= data.DeepClone();
                record["conversations"] ??= data.DeepClone();
                record["nextCursor"] ??= record["cursor"]?.DeepClone() ?? JValue.CreateNull();
                break;
            }
            case "model/list": {
                var data = record["data"] as JArray ?? record["models"] as JArray ?? new JArray();
                record["data"] = data;
                record["models"] ??= data.DeepClone();
                break;
            }
        }

        return record;
    }

    internal static JObject BuildRecentConversationRefreshParams(JObject values)
    {
        return new JObject
        {
            ["archived"] = false,
            ["cursor"] = JValue.CreateNull(),
            ["limit"] = 50,
            ["modelProviders"] = JValue.CreateNull(),
            ["sortKey"] = values["sortKey"]?.Value<string>() ?? "updated_at"
        };
    }

    internal static JObject BuildRecentHistoryResponse(JToken? result)
    {
        var normalized = NormalizeAppServerResult("thread/list", result) as JObject ?? new JObject();
        var source = normalized["data"] as JArray ?? new JArray();
        var items = new JArray();

        foreach (var thread in source.OfType<JObject>().Take(50))
        {
            var id = LimitHistoryText(thread["id"], 256);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var name = LimitHistoryText(thread["name"], 240);
            var preview = LimitHistoryText(thread["preview"], 240);
            var title = BuildLocalThreadTitle(name ?? preview);
            var item = new JObject
            {
                ["id"] = id,
                ["title"] = string.IsNullOrWhiteSpace(title) ? JValue.CreateNull() : title,
                ["workspaceName"] = GetHistoryWorkspaceName(thread["cwd"])
            };

            var timestamp = CopyHistoryTimestamp(
                thread["updatedAt"]
                ?? thread["updated_at"]
                ?? thread["createdAt"]
                ?? thread["created_at"]);
            if (timestamp is not null)
            {
                item["updatedAt"] = timestamp;
            }

            items.Add(item);
        }

        var nextCursor = normalized["nextCursor"] ?? normalized["cursor"];
        return new JObject
        {
            ["items"] = items,
            ["hasMore"] = nextCursor is not null
                && nextCursor.Type != JTokenType.Null
                && !string.IsNullOrWhiteSpace(nextCursor.Value<string>())
        };
    }

    internal static JObject BuildOpenAiApiKeyResponse()
    {
        return new JObject { ["value"] = JValue.CreateNull() };
    }

    internal static JObject CreateHistoryNavigationMessage(string type)
    {
        if (!string.Equals(type, "navigate-back", StringComparison.Ordinal)
            && !string.Equals(type, "navigate-forward", StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        return new JObject { ["type"] = type };
    }

    private static JObject UnwrapParameters(JToken? parameters)
    {
        if (parameters is not JObject record)
        {
            return new JObject();
        }

        return record["params"] as JObject ?? record;
    }

    private static JToken? ParseJsonBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JToken.Parse(body!);
        }
        catch
        {
            return body;
        }
    }

    private static string SerializeResult(JToken? result)
    {
        return (result ?? JValue.CreateNull()).ToString(Formatting.None);
    }

    private string ResolveWorkingDirectory()
    {
        var path = _viewModel.Settings.WorkingDirectory;
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private string GetLocale()
    {
        return string.IsNullOrWhiteSpace(_viewModel.Settings.LanguageOverride)
            ? CultureInfo.CurrentUICulture.Name
            : _viewModel.Settings.LanguageOverride;
    }

    private static string? ExtractKey(JObject values)
    {
        if (values["key"]?.Type == JTokenType.String)
        {
            return values["key"]!.Value<string>();
        }

        return (values["key"] as JObject)?["key"]?.Value<string>();
    }

    private static string ResolveRoute(JObject message, string fallback)
    {
        return message["path"]?.Value<string>()
            ?? message["route"]?.Value<string>()
            ?? (message["section"]?.Value<string>() is string section ? "/settings/" + section.TrimStart('/') : fallback);
    }

    internal static bool IsSettingsRoute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var route = path!;
        var suffixIndex = route.IndexOfAny(new[] { '?', '#' });
        if (suffixIndex >= 0)
        {
            route = route.Substring(0, suffixIndex);
        }

        return string.Equals(route, "/settings", StringComparison.OrdinalIgnoreCase)
            || route.StartsWith("/settings/", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolveSettingsSection(JObject message, string? settingsPath = null)
    {
        var section = message["section"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(section))
        {
            return section!.Trim().Trim('/');
        }

        var route = settingsPath
            ?? message["path"]?.Value<string>()
            ?? message["route"]?.Value<string>()
            ?? string.Empty;
        var suffixIndex = route.IndexOfAny(new[] { '?', '#' });
        if (suffixIndex >= 0)
        {
            route = route.Substring(0, suffixIndex);
        }

        const string prefix = "/settings";
        return IsSettingsRoute(route)
            ? route.Substring(prefix.Length).Trim('/')
            : string.Empty;
    }

    internal static JArray? NormalizeQueryKeyForBroadcast(JToken? queryKey)
    {
        if (queryKey is not JArray values || values.Count == 0 || values.Count > 32)
        {
            return null;
        }

        var normalized = (JArray)values.DeepClone();
        return normalized.ToString(Formatting.None).Length <= 8192 ? normalized : null;
    }

    internal static JObject CreateQueryInvalidationNotification(JToken queryKey)
    {
        return new JObject
        {
            ["type"] = "mcp-notification",
            ["hostId"] = "local",
            ["method"] = "query-cache-invalidate",
            ["params"] = new JObject { ["queryKey"] = queryKey.DeepClone() }
        };
    }

    private static string ResolveFilePath(JObject values)
    {
        var path = values["uri"]?.Value<string>() ?? values["path"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("A file path is required.");
        }

        return ResolveFilePath(path!);
    }

    private static string ResolveFilePath(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return Path.GetFullPath(path);
    }

    internal static JObject ToPickedFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return new JObject
        {
            ["label"] = Path.GetFileName(fullPath),
            ["path"] = fullPath,
            ["fsPath"] = fullPath
        };
    }

    private static void OpenInBrowser(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CodexVsix", ExtensionInfo.Version));
        return client;
    }

    private static void LogWebViewMessage(string? level, string message)
    {
        CodexDiagnosticLogger.Shared.Write(
            "webview.message",
            new JObject
            {
                ["level"] = level ?? "info",
                ["message"] = SanitizeLogText(message)
            });
    }

    private static void LogInformation(string message)
    {
        LogBridgeMessage("info", message);
    }

    private static void LogWarning(string message)
    {
        LogBridgeMessage("warning", message);
    }

    private static void LogError(string message)
    {
        LogBridgeMessage("error", message);
    }

    private static void LogBridgeMessage(string level, string message)
    {
        CodexDiagnosticLogger.Shared.Write(
            "bridge.message",
            new JObject
            {
                ["level"] = level,
                ["message"] = SanitizeLogText(message)
            });
    }

    private static string SanitizeLogText(string? text)
    {
        var sanitized = CodexDiagnosticLogger.SanitizeText(text)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return sanitized.Length <= 6000 ? sanitized : sanitized.Substring(0, 6000);
    }

    private static string DescribeFetchUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath;
        }

        var value = SanitizeLogText(url);
        var queryIndex = value.IndexOf('?');
        return queryIndex >= 0 ? value.Substring(0, queryIndex) : value;
    }

    private static string BuildLocalThreadTitle(string? prompt)
    {
        var normalized = string.Join(
            " ",
            (prompt ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= 60)
        {
            return normalized;
        }

        return normalized.Substring(0, 59).TrimEnd() + "…";
    }

    private static string? LimitHistoryText(JToken? token, int maximumLength)
    {
        if (token?.Type != JTokenType.String)
        {
            return null;
        }

        var value = token.Value<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Length <= maximumLength ? value : value.Substring(0, maximumLength);
    }

    private static JToken? CopyHistoryTimestamp(JToken? token)
    {
        if (token is null)
        {
            return null;
        }

        switch (token.Type)
        {
            case JTokenType.Integer:
            case JTokenType.Float:
                return token.DeepClone();
            case JTokenType.String:
                var value = LimitHistoryText(token, 64);
                return value is null ? null : JValue.CreateString(value);
            default:
                return null;
        }
    }

    private static JToken GetHistoryWorkspaceName(JToken? token)
    {
        var path = LimitHistoryText(token, 4096);
        if (path is null)
        {
            return JValue.CreateNull();
        }

        try
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? JValue.CreateNull() : JValue.CreateString(name);
        }
        catch (ArgumentException)
        {
            return JValue.CreateNull();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisableInteractiveServerRequests();
        _historyWindowController.Dispose();
        _autoCompactionCoordinator.Dispose();
        _appServerRequestRelay.Dispose();
        _processService.AppServerNotificationReceived -= OnAppServerNotificationReceived;
    }
}
