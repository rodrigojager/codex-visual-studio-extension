using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace CodexVsix.Services;
internal sealed class CodexWebViewAutoCompactionCoordinator : IDisposable
{
    internal const double ContextUsageThreshold = 0.85d;
    private static readonly TimeSpan CompactionCooldown = TimeSpan.FromMinutes(5);
    private readonly object _syncRoot = new();
    private readonly Func<bool> _isEnabled;
    private readonly Func<string, CancellationToken, Task> _compactThreadAsync;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Dictionary<string, ThreadState> _threads = new(StringComparer.Ordinal);
    private bool _disposed;
    public CodexWebViewAutoCompactionCoordinator(Func<bool> isEnabled,
        Func<string, CancellationToken, Task> compactThreadAsync, Action<string> log)
    {
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _compactThreadAsync = compactThreadAsync ?? throw new ArgumentNullException(nameof(compactThreadAsync));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }
    public void ObserveNotification(string method, JToken? parameters)
    {
        var hasUsage = TryReadContextUsage(method, parameters, out var threadId, out var used, out var window);
        var started = method == "turn/started" || method == "codex/event/task_started";
        var completed = method == "turn/completed" || method == "codex/event/task_complete";
        if (!hasUsage && !started && !completed) return;
        if (string.IsNullOrWhiteSpace(threadId)) return;
        CancellationTokenSource? scheduled = null;
        ThreadState state;
        lock (_syncRoot)
        {
            if (_disposed) return;
            if (!_threads.TryGetValue(threadId, out state))
                _threads.Add(threadId, state = new ThreadState());
            if (hasUsage)
            {
                state.HighUsage = used / (double)window >= ContextUsageThreshold;
                if (!state.HighUsage || !_isEnabled()) CancelScheduled(state);
                return;
            }
            state.TurnActive = started;
            if (started || !_isEnabled()) { CancelScheduled(state); return; }
            if (state.HighUsage && !state.Running && state.Scheduled is null
                && DateTime.UtcNow - state.LastCompactionUtc >= CompactionCooldown)
            {
                scheduled = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                state.Scheduled = scheduled;
            }
        }
        if (scheduled is not null) _ = RunCompactionAsync(threadId, state, scheduled);
    }
    internal static bool TryReadContextUsage(string method, JToken? parameters,
        out string threadId, out long usedTokens, out long contextWindow)
    {
        threadId = ReadString(parameters, "threadId", "thread_id") ?? string.Empty;
        usedTokens = -1; // Missing usage must not be mistaken for an explicit zero.
        contextWindow = 0;
        JToken? usage;
        if (method == "thread/tokenUsage/updated")
        {
            usage = (parameters as JObject)?["tokenUsage"];
            usedTokens = ReadLong(usage, "last", "totalTokens")
                ?? ReadLong(usage, "lastTokenUsage", "totalTokens")
                ?? ReadLong(usage, "last_token_usage", "total_tokens")
                ?? ReadLong(usage, "total", "totalTokens")
                ?? ReadLong(usage, "totalTokenUsage", "totalTokens")
                ?? ReadLong(usage, "total_token_usage", "total_tokens") ?? -1;
        }
        else if (method == "codex/event/token_count")
        {
            usage = ((parameters as JObject)?["msg"] as JObject)?["info"];
            usedTokens = ReadLong(usage, "last_token_usage", "total_tokens")
                ?? ReadLong(usage, "lastTokenUsage", "totalTokens")
                ?? ReadLong(usage, "total_token_usage", "total_tokens")
                ?? ReadLong(usage, "totalTokenUsage", "totalTokens") ?? -1;
        }
        else return false;
        contextWindow = ReadLong(usage, "modelContextWindow") ?? ReadLong(usage, "model_context_window") ?? 0;
        return threadId.Length > 0 && usedTokens >= 0 && contextWindow > 0;
    }
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetimeCts.Cancel();
            _threads.Clear();
        }
        _lifetimeCts.Dispose();
    }
    private static void CancelScheduled(ThreadState state)
    {
        var scheduled = state.Scheduled;
        state.Scheduled = null;
        scheduled?.Cancel(); // The scheduled task owns disposal.
    }
    private async Task RunCompactionAsync(string threadId, ThreadState state, CancellationTokenSource scheduled)
    {
        var token = scheduled.Token;
        var running = false;
        try
        {
            await Task.Delay(250, token).ConfigureAwait(false);
            lock (_syncRoot)
            {
                // Usage, settings and turn state may have changed during the delay.
                if (_disposed || token.IsCancellationRequested || state.Scheduled != scheduled
                    || !state.HighUsage || state.TurnActive || !_isEnabled()) return;
                state.Scheduled = null;
                state.Running = running = true;
            }
            await _compactThreadAsync(threadId, token).ConfigureAwait(false);
            lock (_syncRoot) state.LastCompactionUtc = DateTime.UtcNow;
            _log("Automatically compacted a long conversation at 85% context usage.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // A future completion can retry only if the latest usage still warrants it.
            _log("Automatic conversation compaction failed: " + ex.Message);
        }
        finally
        {
            lock (_syncRoot)
            {
                if (state.Scheduled == scheduled) state.Scheduled = null;
                if (running) state.Running = false;
            }
            scheduled.Dispose();
        }
    }
    private static long? ReadLong(JToken? token, params string[] path)
    {
        foreach (var segment in path) token = (token as JObject)?[segment];
        if (token?.Type != JTokenType.Integer) return null;
        try { return token.Value<long>(); }
        catch (Exception ex) when (ex is OverflowException || ex is FormatException) { return null; }
    }
    private static string? ReadString(JToken? token, params string[] names)
    {
        if (token is not JObject record) return null;
        foreach (var name in names)
            if (record[name]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(record[name]!.Value<string>()))
                return record[name]!.Value<string>();
        foreach (var property in record.Properties())
        {
            var nested = ReadString(property.Value, names);
            if (nested is not null) return nested;
        }
        return null;
    }
    private sealed class ThreadState
    {
        public bool HighUsage;
        public bool TurnActive;
        public bool Running;
        public DateTime LastCompactionUtc = DateTime.MinValue;
        public CancellationTokenSource? Scheduled;
    }
}
