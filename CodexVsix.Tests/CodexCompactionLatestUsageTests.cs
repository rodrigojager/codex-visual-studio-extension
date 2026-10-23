using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CodexVsix.Tests;
public class CodexCompactionLatestUsageTests
{
    [Theory]
    [InlineData(false, 10, false)]
    [InlineData(true, 10, false)]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 10, true)]
    [InlineData(true, 10, true)]
    public async Task LatestLowUsageShouldCancelPendingCompaction(bool legacy, int latestUsage, bool lowAfterCompletion)
    {
        int calls = 0;
        using var coordinator = new CodexWebViewAutoCompactionCoordinator(
            () => true, (_, _) => { Interlocked.Increment(ref calls); return Task.CompletedTask; }, _ => {});
        Action<int> usage = n => coordinator.ObserveNotification(
            legacy ? "codex/event/token_count" : "thread/tokenUsage/updated",
            legacy ? JObject.Parse("{\"thread_id\":\"audit\",\"msg\":{\"info\":{\"last_token_usage\":{\"total_tokens\":"+n+"},\"model_context_window\":100}}}")
                   : JObject.Parse("{\"threadId\":\"audit\",\"tokenUsage\":{\"last\":{\"totalTokens\":"+n+"},\"modelContextWindow\":100}}"));
        usage(90);
        if (!lowAfterCompletion) usage(latestUsage);
        coordinator.ObserveNotification("turn/completed", JObject.Parse("{\"threadId\":\"audit\"}"));
        if (lowAfterCompletion) usage(latestUsage);
        await Task.Delay(700);
        Assert.Equal(0, calls);
    }
}
