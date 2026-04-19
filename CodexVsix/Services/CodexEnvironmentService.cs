using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Models;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

public sealed class CodexEnvironmentService
{
    public const string FallbackInstallCommand = "npm install -g @openai/codex";

    public async Task<CodexEnvironmentStatus> InspectAsync(CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        var localization = new LocalizationService(settings.LanguageOverride);
        var configuredExecutablePath = CodexExecutableResolver.NormalizeConfiguredExecutablePath(settings.CodexExecutablePath);
        var status = new CodexEnvironmentStatus
        {
            Stage = CodexSetupStage.Checking,
            ConfiguredExecutablePath = configuredExecutablePath,
            AuthFilePath = GetAuthFilePath(settings.EnvironmentVariables)
        };

        try
        {
            var resolvedExecutablePath = CodexExecutableResolver.ResolveExecutableLocation(configuredExecutablePath, settings.EnvironmentVariables);
            if (string.IsNullOrWhiteSpace(resolvedExecutablePath))
            {
                status.Stage = CodexSetupStage.MissingExecutable;
                return status;
            }

            status.ResolvedExecutablePath = resolvedExecutablePath;

            var version = await TryGetVersionAsync(resolvedExecutablePath, localization, cancellationToken).ConfigureAwait(false);
            if (!version.Success)
            {
                status.Stage = CodexSetupStage.Error;
                status.ErrorDetail = version.Detail;
                return status;
            }

            status.Version = version.Detail;
            var appServer = await TryValidateAppServerAsync(resolvedExecutablePath, localization, cancellationToken).ConfigureAwait(false);
            if (!appServer.Success)
            {
                status.Stage = CodexSetupStage.Error;
                status.ErrorDetail = appServer.Detail;
                return status;
            }

            var authFileInspection = InspectAuthFile(status.AuthFilePath);
            var appServerAuth = await TryReadAppServerAuthStateAsync(resolvedExecutablePath, settings, cancellationToken).ConfigureAwait(false);
            var providerInspection = InspectConfiguredProvider(settings);
            var hasManagedLogin = string.Equals(appServerAuth.AccountType, "chatgpt", StringComparison.OrdinalIgnoreCase);

            status.HasAuthFile = authFileInspection.HasUsableAuthFile;
            status.HasApiKey = HasOpenAiApiKey(settings.EnvironmentVariables)
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                || authFileInspection.HasEmbeddedApiKey
                || string.Equals(appServerAuth.AccountType, "apiKey", StringComparison.OrdinalIgnoreCase);
            status.AccountEmail = FirstNonEmptyString(appServerAuth.AccountEmail, authFileInspection.AccountEmail);
            status.RequiresOpenaiAuth = appServerAuth.Success
                ? appServerAuth.RequiresOpenaiAuth
                : providerInspection.RequiresOpenaiAuthFallback;
            status.AuthenticationLabel = BuildAuthenticationLabel(localization, appServerAuth, providerInspection, hasManagedLogin);

            var hasOpenaiAuthentication = status.HasApiKey || status.HasAuthFile || hasManagedLogin;
            var hasProviderAuthentication = providerInspection.IsReady;

            status.Stage = status.RequiresOpenaiAuth
                ? (hasOpenaiAuthentication ? CodexSetupStage.Ready : CodexSetupStage.MissingAuthentication)
                : (hasProviderAuthentication ? CodexSetupStage.Ready : CodexSetupStage.MissingAuthentication);

            return status;
        }
        catch (Exception ex)
        {
            status.Stage = CodexSetupStage.Error;
            status.ErrorDetail = ex.Message;
            return status;
        }
    }

    public void LaunchLoginTerminal(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        if (IsPowerShellScript(executablePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ResolvePowerShellHost(),
                Arguments = "-NoExit -ExecutionPolicy Bypass -File " + QuoteArgument(executablePath) + " login",
                UseShellExecute = true
            });
            return;
        }

        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var commandShell = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandShell))
            {
                commandShell = "cmd.exe";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = commandShell,
                Arguments = "/k \"" + QuoteForCommandShell(executablePath) + " login\"",
                UseShellExecute = true
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = "login",
            UseShellExecute = true
        });
    }

    public void DeleteAuthFile(string? authFilePath = null)
    {
        var path = string.IsNullOrWhiteSpace(authFilePath)
            ? GetAuthFilePath()
            : authFilePath!;

        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }

    public string GetAuthFilePath(string? environmentVariables = null)
    {
        return Path.Combine(CodexEnvironmentPathHelper.GetCodexHomeDirectory(environmentVariables), "auth.json");
    }

    private static string BuildAuthenticationLabel(LocalizationService localization, AppServerAuthInspection appServerAuth, ConfigProviderInspection providerInspection, bool hasManagedLogin)
    {
        if (hasManagedLogin)
        {
            return localization.SetupManagedLoginLabel;
        }

        if (appServerAuth.Success && !appServerAuth.RequiresOpenaiAuth)
        {
            return BuildConfigProviderLabel(localization, providerInspection);
        }

        if (!appServerAuth.Success && providerInspection.HasActiveProvider && !providerInspection.RequiresOpenaiAuthFallback)
        {
            return BuildConfigProviderLabel(localization, providerInspection);
        }

        return string.Empty;
    }

    private static string BuildConfigProviderLabel(LocalizationService localization, ConfigProviderInspection providerInspection)
    {
        return !string.IsNullOrWhiteSpace(providerInspection.SelectedProfile)
            ? string.Format(localization.Culture, localization.SetupConfigProfileLabelFormat, providerInspection.SelectedProfile)
            : localization.SetupConfigProviderLabel;
    }

    private static async Task<(bool Success, string Detail)> TryGetVersionAsync(string executablePath, LocalizationService localization, CancellationToken cancellationToken)
    {
        var probe = await RunProbeAsync(executablePath, "--version", localization.SetupErrorSummary, cancellationToken).ConfigureAwait(false);
        if (!probe.Success)
        {
            return probe;
        }

        var versionText = FirstNonEmptyLine(probe.Detail);
        return (true, string.IsNullOrWhiteSpace(versionText) ? localization.CodexDetectedLabel : versionText);
    }

    private static async Task<(bool Success, string Detail)> TryValidateAppServerAsync(string executablePath, LocalizationService localization, CancellationToken cancellationToken)
    {
        var probe = await RunProbeAsync(
            executablePath,
            "app-server --help",
            localization.AppServerValidationFailed,
            cancellationToken).ConfigureAwait(false);

        if (probe.Success)
        {
            return probe;
        }

        return (false, localization.AppServerUnsupported + Environment.NewLine + probe.Detail);
    }

    private static async Task<(bool Success, string Detail)> RunProbeAsync(string executablePath, string arguments, string fallbackError, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateProbeStartInfo(executablePath, arguments)
        };

        try
        {
            if (!process.Start())
            {
                return (false, fallbackError);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var exited = await WaitForExitAsync(process, 5000, cancellationToken).ConfigureAwait(false);

        if (!exited)
        {
            TryTerminateProcess(process);
            return (false, fallbackError);
        }

        var output = await outputTask.ConfigureAwait(false);
        var errorText = await errorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(errorText) ? output : errorText;
            return (false, string.IsNullOrWhiteSpace(error) ? fallbackError : error.Trim());
        }

        return (true, string.IsNullOrWhiteSpace(output) ? errorText : output);
    }

    private static async Task<AppServerAuthInspection> TryReadAppServerAuthStateAsync(string executablePath, CodexExtensionSettings settings, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateServerProbeStartInfo(executablePath, settings)
        };

        ApplyEnvironmentVariables(process.StartInfo, settings.EnvironmentVariables);

        try
        {
            if (!process.Start())
            {
                return AppServerAuthInspection.Empty;
            }
        }
        catch
        {
            return AppServerAuthInspection.Empty;
        }

        try
        {
            using var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false), 1024, true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            await WriteJsonRpcMessageAsync(writer, new
            {
                id = 1,
                method = "initialize",
                @params = new
                {
                    clientInfo = new { name = "codex-vsix", version = "1.0" },
                    capabilities = new { experimentalApi = true }
                }
            }).ConfigureAwait(false);

            var initializeResponse = await ReadJsonRpcResponseAsync(process.StandardOutput, 1, cancellationToken).ConfigureAwait(false);
            if (initializeResponse is null || initializeResponse["error"] is not null)
            {
                return AppServerAuthInspection.Empty;
            }

            await WriteJsonRpcMessageAsync(writer, new
            {
                method = "initialized",
                @params = new { }
            }).ConfigureAwait(false);

            await WriteJsonRpcMessageAsync(writer, new
            {
                id = 2,
                method = "account/read",
                @params = new { refreshToken = false }
            }).ConfigureAwait(false);

            var accountResponse = await ReadJsonRpcResponseAsync(process.StandardOutput, 2, cancellationToken).ConfigureAwait(false);
            var result = accountResponse?["result"] as JObject;
            if (result is null)
            {
                return AppServerAuthInspection.Empty;
            }

            var account = result["account"] as JObject;
            return new AppServerAuthInspection(
                success: true,
                requiresOpenaiAuth: result["requiresOpenaiAuth"]?.Value<bool>() ?? true,
                accountType: account?["type"]?.Value<string>() ?? string.Empty,
                accountEmail: account?["email"]?.Value<string>() ?? string.Empty);
        }
        catch
        {
            return AppServerAuthInspection.Empty;
        }
        finally
        {
            TryTerminateProcess(process);
        }
    }

    private static async Task WriteJsonRpcMessageAsync(StreamWriter writer, object payload)
    {
        var json = JObject.FromObject(payload).ToString(Newtonsoft.Json.Formatting.None);
        await writer.WriteLineAsync(json).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<JObject?> ReadJsonRpcResponseAsync(StreamReader reader, int requestId, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(5);
        while (true)
        {
            var line = await ReadLineWithTimeoutAsync(reader, timeout, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JObject? message;
            try
            {
                message = JObject.Parse(line);
            }
            catch
            {
                continue;
            }

            if (message["id"]?.Value<int?>() == requestId)
            {
                return message;
            }
        }
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var readTask = reader.ReadLineAsync();
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
        if (completedTask != readTask)
        {
            return null;
        }

        return await readTask.ConfigureAwait(false);
    }

    private static ProcessStartInfo CreateProbeStartInfo(string executablePath, string arguments)
    {
        if (IsPowerShellScript(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = ResolvePowerShellHost(),
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        if (!RequiresCommandShell(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
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
            Arguments = "/d /s /c \"" + QuoteForCommandShell(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments) + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static ProcessStartInfo CreateServerProbeStartInfo(string executablePath, CodexExtensionSettings settings)
    {
        var arguments = BuildServerProbeArguments(settings);
        var workingDirectory = ResolveWorkingDirectory(settings.WorkingDirectory);
        if (IsPowerShellScript(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = ResolvePowerShellHost(),
                WorkingDirectory = workingDirectory,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        if (!RequiresCommandShell(executablePath))
        {
            return new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
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
            WorkingDirectory = workingDirectory,
            Arguments = "/d /s /c \"" + QuoteForCommandShell(executablePath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments) + "\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private static string BuildServerProbeArguments(CodexExtensionSettings settings)
    {
        var args = new List<string> { "app-server", "--listen", "stdio://" };

        if (!HasProfileArgument(settings.AdditionalArguments) && !string.IsNullOrWhiteSpace(settings.Profile))
        {
            args.Add("--profile");
            args.Add(settings.Profile.Trim());
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

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => process.WaitForExit(timeoutMilliseconds), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process);
            throw;
        }
    }

    private static ConfigProviderInspection InspectConfiguredProvider(CodexExtensionSettings settings)
    {
        var parsedConfig = ParseEffectiveConfig(settings);
        var selectedProfile = ResolveSelectedProfile(settings, parsedConfig.DefaultProfile);
        var providerId = string.Empty;

        if (!string.IsNullOrWhiteSpace(selectedProfile)
            && parsedConfig.Profiles.TryGetValue(selectedProfile, out var profileConfig)
            && !string.IsNullOrWhiteSpace(profileConfig.ModelProvider))
        {
            providerId = profileConfig.ModelProvider;
        }

        if (string.IsNullOrWhiteSpace(providerId))
        {
            providerId = parsedConfig.RootModelProvider;
        }

        if (string.IsNullOrWhiteSpace(providerId))
        {
            return new ConfigProviderInspection(string.Empty, selectedProfile, hasActiveProvider: false, hasExplicitCredentialRequirement: false, hasConfiguredCredentials: true);
        }

        if (!parsedConfig.Providers.TryGetValue(providerId, out var providerConfig))
        {
            return new ConfigProviderInspection(providerId, selectedProfile, hasActiveProvider: true, hasExplicitCredentialRequirement: false, hasConfiguredCredentials: true);
        }

        var hasExplicitCredentialRequirement =
            !string.IsNullOrWhiteSpace(providerConfig.EnvKey)
            || !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
            || providerConfig.HasAuthSection
            || providerConfig.EnvHeaderVariables.Count > 0;

        if (!hasExplicitCredentialRequirement)
        {
            return new ConfigProviderInspection(providerId, selectedProfile, hasActiveProvider: true, hasExplicitCredentialRequirement: false, hasConfiguredCredentials: true);
        }

        var envRequirementsSatisfied =
            (string.IsNullOrWhiteSpace(providerConfig.EnvKey) || IsEnvironmentVariableConfigured(providerConfig.EnvKey, settings.EnvironmentVariables))
            && providerConfig.EnvHeaderVariables.All(envVar => IsEnvironmentVariableConfigured(envVar, settings.EnvironmentVariables));

        var hasConfiguredCredentials = envRequirementsSatisfied
            && (providerConfig.HasAuthSection
                || !string.IsNullOrWhiteSpace(providerConfig.ApiKey)
                || !string.IsNullOrWhiteSpace(providerConfig.EnvKey)
                || providerConfig.EnvHeaderVariables.Count > 0);

        return new ConfigProviderInspection(providerId, selectedProfile, hasActiveProvider: true, hasExplicitCredentialRequirement: true, hasConfiguredCredentials: hasConfiguredCredentials);
    }

    private static ParsedCodexConfig ParseEffectiveConfig(CodexExtensionSettings settings)
    {
        var parsedConfig = new ParsedCodexConfig();
        var configPath = Path.Combine(CodexEnvironmentPathHelper.GetCodexHomeDirectory(settings.EnvironmentVariables), "config.toml");
        if (File.Exists(configPath))
        {
            ParseTomlInto(parsedConfig, File.ReadAllLines(configPath));
        }

        if (!string.IsNullOrWhiteSpace(settings.RawTomlOverrides))
        {
            ParseTomlInto(parsedConfig, settings.RawTomlOverrides.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        return parsedConfig;
    }

    private static void ParseTomlInto(ParsedCodexConfig config, IEnumerable<string> lines)
    {
        var currentSection = Array.Empty<string>();
        foreach (var rawLine in lines)
        {
            var line = StripTomlComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection = SplitTomlPath(line.Substring(1, line.Length - 2)).ToArray();
                continue;
            }

            var separatorIndex = FindTomlAssignmentSeparator(line);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separatorIndex).Trim();
            var value = line.Substring(separatorIndex + 1).Trim();
            ApplyTomlAssignment(config, currentSection, key, value);
        }
    }

    private static void ApplyTomlAssignment(ParsedCodexConfig config, IReadOnlyList<string> currentSection, string key, string value)
    {
        if (currentSection.Count == 0)
        {
            if (string.Equals(key, "profile", StringComparison.OrdinalIgnoreCase))
            {
                config.DefaultProfile = UnquoteTomlString(value);
            }
            else if (string.Equals(key, "model_provider", StringComparison.OrdinalIgnoreCase))
            {
                config.RootModelProvider = UnquoteTomlString(value);
            }

            return;
        }

        if (currentSection.Count == 2 && string.Equals(currentSection[0], "profiles", StringComparison.OrdinalIgnoreCase))
        {
            var profile = config.GetOrCreateProfile(currentSection[1]);
            if (string.Equals(key, "model_provider", StringComparison.OrdinalIgnoreCase))
            {
                profile.ModelProvider = UnquoteTomlString(value);
            }

            return;
        }

        if (currentSection.Count < 2 || !string.Equals(currentSection[0], "model_providers", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var provider = config.GetOrCreateProvider(currentSection[1]);
        if (currentSection.Count == 2)
        {
            if (string.Equals(key, "env_key", StringComparison.OrdinalIgnoreCase))
            {
                provider.EnvKey = UnquoteTomlString(value);
            }
            else if (string.Equals(key, "api_key", StringComparison.OrdinalIgnoreCase))
            {
                provider.ApiKey = UnquoteTomlString(value);
            }
            else if (string.Equals(key, "env_http_headers", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var envVar in ParseInlineTableValues(value))
                {
                    if (!string.IsNullOrWhiteSpace(envVar))
                    {
                        provider.EnvHeaderVariables.Add(envVar);
                    }
                }
            }
            else if (string.Equals(key, "auth", StringComparison.OrdinalIgnoreCase) && value.StartsWith("{", StringComparison.Ordinal))
            {
                provider.HasAuthSection = true;
            }

            return;
        }

        if (currentSection.Count == 3 && string.Equals(currentSection[2], "auth", StringComparison.OrdinalIgnoreCase))
        {
            provider.HasAuthSection = true;
        }
    }

    private static IEnumerable<string> ParseInlineTableValues(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (var entry in SplitTopLevelCommaList(trimmed.Substring(1, trimmed.Length - 2)))
        {
            var separatorIndex = FindTomlAssignmentSeparator(entry);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var entryValue = entry.Substring(separatorIndex + 1).Trim();
            var envVar = UnquoteTomlString(entryValue);
            if (!string.IsNullOrWhiteSpace(envVar))
            {
                yield return envVar;
            }
        }
    }

    private static IEnumerable<string> SplitTopLevelCommaList(string text)
    {
        var current = new StringBuilder();
        var inDoubleQuotes = false;
        var inSingleQuotes = false;
        var braceDepth = 0;

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '"' when !inSingleQuotes:
                    inDoubleQuotes = !inDoubleQuotes;
                    current.Append(ch);
                    continue;
                case '\'' when !inDoubleQuotes:
                    inSingleQuotes = !inSingleQuotes;
                    current.Append(ch);
                    continue;
                case '{' when !inDoubleQuotes && !inSingleQuotes:
                    braceDepth++;
                    current.Append(ch);
                    continue;
                case '}' when !inDoubleQuotes && !inSingleQuotes && braceDepth > 0:
                    braceDepth--;
                    current.Append(ch);
                    continue;
                case ',' when !inDoubleQuotes && !inSingleQuotes && braceDepth == 0:
                    yield return current.ToString().Trim();
                    current.Clear();
                    continue;
                default:
                    current.Append(ch);
                    continue;
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }

    private static IEnumerable<string> SplitTomlPath(string text)
    {
        var current = new StringBuilder();
        var inDoubleQuotes = false;
        var inSingleQuotes = false;

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '"' when !inSingleQuotes:
                    inDoubleQuotes = !inDoubleQuotes;
                    current.Append(ch);
                    continue;
                case '\'' when !inDoubleQuotes:
                    inSingleQuotes = !inSingleQuotes;
                    current.Append(ch);
                    continue;
                case '.' when !inDoubleQuotes && !inSingleQuotes:
                    var segment = current.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        yield return UnquoteTomlString(segment);
                    }

                    current.Clear();
                    continue;
                default:
                    current.Append(ch);
                    continue;
            }
        }

        if (current.Length > 0)
        {
            var segment = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(segment))
            {
                yield return UnquoteTomlString(segment);
            }
        }
    }

    private static int FindTomlAssignmentSeparator(string text)
    {
        var inDoubleQuotes = false;
        var inSingleQuotes = false;
        var braceDepth = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (ch == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (inDoubleQuotes || inSingleQuotes)
            {
                continue;
            }

            if (ch == '{')
            {
                braceDepth++;
                continue;
            }

            if (ch == '}' && braceDepth > 0)
            {
                braceDepth--;
                continue;
            }

            if (ch == '=' && braceDepth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string StripTomlComment(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var inDoubleQuotes = false;
        var inSingleQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (ch == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (ch == '#' && !inDoubleQuotes && !inSingleQuotes)
            {
                return line.Substring(0, index);
            }
        }

        return line;
    }

    private static string UnquoteTomlString(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length >= 2)
        {
            if ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                || (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\''))
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }
        }

        return trimmed
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }

    private static string ResolveSelectedProfile(CodexExtensionSettings settings, string defaultProfile)
    {
        return FirstNonEmptyString(
            GetProfileArgument(settings.AdditionalArguments),
            settings.Profile,
            defaultProfile);
    }

    private static string GetProfileArgument(string? commandLine)
    {
        var awaitingProfileValue = false;
        foreach (var token in SplitArguments(commandLine ?? string.Empty))
        {
            if (awaitingProfileValue)
            {
                return token.Trim();
            }

            if (string.Equals(token, "--profile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "-p", StringComparison.OrdinalIgnoreCase))
            {
                awaitingProfileValue = true;
                continue;
            }

            if (token.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
            {
                return token.Substring("--profile=".Length).Trim();
            }
        }

        return string.Empty;
    }

    private static bool HasProfileArgument(string? commandLine)
    {
        return !string.IsNullOrWhiteSpace(GetProfileArgument(commandLine));
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

    private static string JoinArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteCommandLineArgument));
    }

    private static string QuoteCommandLineArgument(string value)
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

    private static string ResolveWorkingDirectory(string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            return workingDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static void ApplyEnvironmentVariables(ProcessStartInfo psi, string? environmentVariables)
    {
        foreach (var entry in CodexEnvironmentPathHelper.ParseEnvironmentVariables(environmentVariables))
        {
            psi.EnvironmentVariables[entry.Key] = entry.Value;
        }
    }

    private static bool IsEnvironmentVariableConfigured(string envVarName, string? environmentVariables)
    {
        return !string.IsNullOrWhiteSpace(CodexEnvironmentPathHelper.GetEffectiveEnvironmentVariable(envVarName, environmentVariables));
    }

    private static string FirstNonEmptyLine(string? text)
    {
        return (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? string.Empty;
    }

    private static bool HasOpenAiApiKey(string environmentVariables)
    {
        foreach (var line in (environmentVariables ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separatorIndex).Trim();
            var value = line.Substring(separatorIndex + 1).Trim();
            if (string.Equals(key, "OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    private static AuthFileInspection InspectAuthFile(string authFilePath)
    {
        if (string.IsNullOrWhiteSpace(authFilePath) || !File.Exists(authFilePath))
        {
            return AuthFileInspection.Empty;
        }

        try
        {
            var document = JObject.Parse(File.ReadAllText(authFilePath));
            var embeddedApiKey = document["OPENAI_API_KEY"]?.Value<string>();
            var tokens = document["tokens"] as JObject;
            var accessToken = tokens?["access_token"]?.Value<string>();
            var refreshToken = tokens?["refresh_token"]?.Value<string>();
            var idToken = tokens?["id_token"]?.Value<string>();
            var hasEmbeddedApiKey = !string.IsNullOrWhiteSpace(embeddedApiKey);
            var hasUsableAuthFile = hasEmbeddedApiKey
                || !string.IsNullOrWhiteSpace(accessToken)
                || !string.IsNullOrWhiteSpace(refreshToken);

            if (string.IsNullOrWhiteSpace(idToken))
            {
                return new AuthFileInspection(hasUsableAuthFile, hasEmbeddedApiKey, string.Empty);
            }

            var payload = ParseJwtPayload(idToken!);
            var accountEmail = FirstNonEmptyString(
                payload.TryGetValue("email", out var email) ? email?.ToString() : null,
                payload.TryGetValue("preferred_username", out var preferredUsername) ? preferredUsername?.ToString() : null,
                tokens?["account_id"]?.ToString());

            return new AuthFileInspection(hasUsableAuthFile, hasEmbeddedApiKey, accountEmail);
        }
        catch
        {
            return AuthFileInspection.Empty;
        }
    }

    private static string FirstNonEmptyString(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static JObject ParseJwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return new JObject();
        }

        var payloadBytes = DecodeBase64Url(parts[1]);
        var payloadJson = Encoding.UTF8.GetString(payloadBytes);
        return JObject.Parse(payloadJson);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (normalized.Length % 4);
        if (padding is > 0 and < 4)
        {
            normalized = normalized.PadRight(normalized.Length + padding, '=');
        }

        return Convert.FromBase64String(normalized);
    }

    private static string QuoteForCommandShell(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static bool RequiresCommandShell(string executablePath)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
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
        return Environment.OSVersion.Platform == PlatformID.Win32NT
            && Path.GetExtension(executablePath).Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1000);
            }
        }
        catch
        {
        }
    }

    private static string ResolvePowerShellHost()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPowerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(windowsPowerShell)
            ? windowsPowerShell
            : "powershell.exe";
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private sealed class AuthFileInspection
    {
        public static AuthFileInspection Empty { get; } = new(false, false, string.Empty);

        public AuthFileInspection(bool hasUsableAuthFile, bool hasEmbeddedApiKey, string accountEmail)
        {
            HasUsableAuthFile = hasUsableAuthFile;
            HasEmbeddedApiKey = hasEmbeddedApiKey;
            AccountEmail = accountEmail ?? string.Empty;
        }

        public bool HasUsableAuthFile { get; }

        public bool HasEmbeddedApiKey { get; }

        public string AccountEmail { get; }
    }

    private sealed class AppServerAuthInspection
    {
        public static AppServerAuthInspection Empty { get; } = new(false, true, string.Empty, string.Empty);

        public AppServerAuthInspection(bool success, bool requiresOpenaiAuth, string accountType, string accountEmail)
        {
            Success = success;
            RequiresOpenaiAuth = requiresOpenaiAuth;
            AccountType = accountType ?? string.Empty;
            AccountEmail = accountEmail ?? string.Empty;
        }

        public bool Success { get; }

        public bool RequiresOpenaiAuth { get; }

        public string AccountType { get; }

        public string AccountEmail { get; }
    }

    private sealed class ConfigProviderInspection
    {
        public ConfigProviderInspection(string providerId, string selectedProfile, bool hasActiveProvider, bool hasExplicitCredentialRequirement, bool hasConfiguredCredentials)
        {
            ProviderId = providerId ?? string.Empty;
            SelectedProfile = selectedProfile ?? string.Empty;
            HasActiveProvider = hasActiveProvider;
            HasExplicitCredentialRequirement = hasExplicitCredentialRequirement;
            HasConfiguredCredentials = hasConfiguredCredentials;
        }

        public string ProviderId { get; }

        public string SelectedProfile { get; }

        public bool HasActiveProvider { get; }

        public bool HasExplicitCredentialRequirement { get; }

        public bool HasConfiguredCredentials { get; }

        public bool IsReady => !HasExplicitCredentialRequirement || HasConfiguredCredentials;

        public bool RequiresOpenaiAuthFallback => !HasActiveProvider || string.Equals(ProviderId, "openai", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ParsedCodexConfig
    {
        public string DefaultProfile { get; set; } = string.Empty;

        public string RootModelProvider { get; set; } = string.Empty;

        public Dictionary<string, ParsedProfileConfig> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ParsedProviderConfig> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ParsedProfileConfig GetOrCreateProfile(string id)
        {
            if (!Profiles.TryGetValue(id, out var profile))
            {
                profile = new ParsedProfileConfig();
                Profiles[id] = profile;
            }

            return profile;
        }

        public ParsedProviderConfig GetOrCreateProvider(string id)
        {
            if (!Providers.TryGetValue(id, out var provider))
            {
                provider = new ParsedProviderConfig();
                Providers[id] = provider;
            }

            return provider;
        }
    }

    private sealed class ParsedProfileConfig
    {
        public string ModelProvider { get; set; } = string.Empty;
    }

    private sealed class ParsedProviderConfig
    {
        public string EnvKey { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public bool HasAuthSection { get; set; }

        public HashSet<string> EnvHeaderVariables { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
