using System.Threading.Tasks;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexWebViewAutoCompactionCoordinatorTests
{
    private static JObject Usage(string thread, long tokens) => new JObject
    {
        ["threadId"] = thread,
        ["tokenUsage"] = new JObject { ["last"] = new JObject { ["totalTokens"] = tokens }, ["modelContextWindow"] = 100 }
    };

    [Theory]
    [InlineData("disabled")]
    [InlineData("new-turn")]
    [InlineData("disposed")]
    public async Task ScheduledWorkHonorsStateChanges(string change)
    {
        var enabled = true;
        var calls = 0;
        using var coordinator = new CodexWebViewAutoCompactionCoordinator(() => enabled,
            (_, _) => { calls++; return Task.CompletedTask; }, _ => { });
        coordinator.ObserveNotification("thread/tokenUsage/updated", Usage("a", 90));
        coordinator.ObserveNotification("turn/completed", new JObject { ["threadId"] = "a" });
        if (change == "disabled") enabled = false;
        if (change == "new-turn") coordinator.ObserveNotification("turn/started", new JObject { ["threadId"] = "a" });
        if (change == "disposed") coordinator.Dispose();
        await Task.Delay(600);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task HighLowHighCanRescheduleWithoutDuplicatingAndThreadsStayIndependent()
    {
        var calls = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var coordinator = new CodexWebViewAutoCompactionCoordinator(() => true,
            (id, _) => { calls.Enqueue(id); return Task.CompletedTask; }, _ => { });
        coordinator.ObserveNotification("thread/tokenUsage/updated", Usage("a", 90));
        coordinator.ObserveNotification("turn/completed", new JObject { ["threadId"] = "a" });
        coordinator.ObserveNotification("thread/tokenUsage/updated", Usage("a", 10));
        coordinator.ObserveNotification("thread/tokenUsage/updated", Usage("a", 90));
        coordinator.ObserveNotification("thread/tokenUsage/updated", Usage("b", 0));
        coordinator.ObserveNotification("turn/completed", new JObject { ["threadId"] = "a" });
        coordinator.ObserveNotification("turn/completed", new JObject { ["threadId"] = "a" });
        await Task.Delay(700);
        Assert.Equal("a", Assert.Single(calls));
        coordinator.ObserveNotification("turn/completed", new JObject { ["threadId"] = "a" });
        await Task.Delay(500);
        Assert.Single(calls); // Cooldown still applies.
    }

    [Fact]
    public void MissingOrMalformedUsageIsNotAnExplicitZero()
    {
        var malformed = Usage("a", 0);
        malformed["tokenUsage"]!["last"] = JValue.CreateNull();
        Assert.False(CodexWebViewAutoCompactionCoordinator.TryReadContextUsage("thread/tokenUsage/updated", malformed, out _, out _, out _));
        Assert.False(CodexWebViewAutoCompactionCoordinator.TryReadContextUsage("thread/tokenUsage/updated", new JValue("invalid"), out _, out _, out _));
        Assert.True(CodexWebViewAutoCompactionCoordinator.TryReadContextUsage("thread/tokenUsage/updated", Usage("a", 0), out _, out var used, out _));
        Assert.Equal(0, used);
    }

    [Fact]
    public async Task CompactsAfterTheTurnCompletesWhenContextUsageCrossesTheThreshold()
    {
        var compacted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new CodexWebViewAutoCompactionCoordinator(
            () => true,
            (threadId, _) =>
            {
                compacted.TrySetResult(threadId);
                return Task.CompletedTask;
            },
            _ => { });

        coordinator.ObserveNotification(
            "thread/tokenUsage/updated",
            new JObject
            {
                ["threadId"] = "thread-compact",
                ["tokenUsage"] = new JObject
                {
                    ["last"] = new JObject { ["totalTokens"] = 90 },
                    ["modelContextWindow"] = 100
                }
            });
        coordinator.ObserveNotification(
            "turn/completed",
            new JObject { ["threadId"] = "thread-compact", ["turnId"] = "turn-1" });

        var completed = await Task.WhenAny(compacted.Task, Task.Delay(3000));
        Assert.Same(compacted.Task, completed);
        Assert.Equal("thread-compact", await compacted.Task);
    }

    [Fact]
    public void ReadsTheSupportedTokenUsageShape()
    {
        Assert.True(CodexWebViewAutoCompactionCoordinator.TryReadContextUsage(
            "thread/tokenUsage/updated",
            new JObject
            {
                ["threadId"] = "thread-1",
                ["tokenUsage"] = new JObject
                {
                    ["lastTokenUsage"] = new JObject { ["totalTokens"] = 850 },
                    ["modelContextWindow"] = 1000
                }
            },
            out var threadId,
            out var used,
            out var window));

        Assert.Equal("thread-1", threadId);
        Assert.Equal(850, used);
        Assert.Equal(1000, window);
    }
}
