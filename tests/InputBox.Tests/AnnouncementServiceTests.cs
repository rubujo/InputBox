using InputBox.Core.Services;
using System.Diagnostics;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// AnnouncementService 的入隊守衛邏輯與生命週期行為測試
/// <para>驗證空字串過濾、Dispose 後不再處理，以及有效訊息最終被播報的基本合約。</para>
/// </summary>
public class AnnouncementServiceTests : IDisposable
{
    private AnnouncementService? _svc;

    /// <summary>
    /// 在每個測試後確保服務被正確釋放。
    /// </summary>
    public void Dispose()
    {
        _svc?.Dispose();
        _svc = null;
        GC.SuppressFinalize(this);
    }

    // ── Enqueue 守衛邏輯 ────────────────────────────────────────────────────

    /// <summary>
    /// Enqueue 空字串時，委派不應被呼叫（守衛提前返回）。
    /// </summary>
    [Fact]
    public async Task Enqueue_EmptyString_DoesNotInvokeDelegate()
    {
        bool invoked = false;

        _svc = new AnnouncementService(async (msg, interrupt, ct) =>
        {
            invoked = true;
            await Task.CompletedTask;
        });

        _svc.Enqueue(string.Empty);

        // 等待足夠時間讓背景工作有機會執行
        await Task.Delay(600, TestContext.Current.CancellationToken);

        Assert.False(invoked);
    }

    /// <summary>
    /// Dispose 後 Enqueue 有效訊息，委派不應被呼叫（已設置 disposeSignaled）。
    /// </summary>
    [Fact]
    public async Task Enqueue_AfterDispose_DoesNotInvokeDelegate()
    {
        bool invoked = false;

        _svc = new AnnouncementService(async (msg, interrupt, ct) =>
        {
            invoked = true;
            await Task.CompletedTask;
        });

        _svc.Dispose();
        _svc.Enqueue("hello after dispose");

        await Task.Delay(600, TestContext.Current.CancellationToken);

        Assert.False(invoked);
    }

    /// <summary>
    /// 有效訊息應在背景延遲後被委派接收並播報。
    /// </summary>
    [Fact]
    public async Task Enqueue_ValidMessage_EventuallyInvokesDelegate()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _svc = new AnnouncementService(async (msg, interrupt, ct) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });

        _svc.Enqueue("測試廣播");

        // 等待最多 2 秒讓背景工作完成（含 ducking 延遲與 jitter）
        Task<string> completionTask = tcs.Task;
        Task timeoutTask = Task.Delay(2000, TestContext.Current.CancellationToken);
        Task finished = await Task.WhenAny(completionTask, timeoutTask);
        string? received = finished == completionTask ? await completionTask : null;

        Assert.Equal("測試廣播", received);
    }

    /// <summary>
    /// Dispose 應可安全呼叫多次而不拋出例外（冪等保護）。
    /// </summary>
    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        _svc = new AnnouncementService(async (_, _, _) => await Task.CompletedTask);

        var capturedException = Record.Exception(() =>
        {
            _svc.Dispose();
            _svc.Dispose();
        });

        Assert.Null(capturedException);
    }

    /// <summary>
    /// Dispose 應等待進行中的廣播工作完成取消與退出，避免關閉流程留下背景競態。
    /// </summary>
    [Fact]
    public async Task Dispose_InFlightAnnouncement_WaitsForWorkerToExit()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWorker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _svc = new AnnouncementService(async (_, _, ct) =>
        {
            started.TrySetResult(true);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                await releaseWorker.Task.WaitAsync(TestContext.Current.CancellationToken);
                finished.TrySetResult(true);
            }
        });

        _svc.Enqueue("shutdown-race");

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(150, TestContext.Current.CancellationToken);
            releaseWorker.TrySetResult(true);
        }, TestContext.Current.CancellationToken);

        var stopwatch = Stopwatch.StartNew();

        _svc.Dispose();

        stopwatch.Stop();

        Assert.True(finished.Task.IsCompleted, "Dispose 應等待背景廣播工作完成取消與退出。");
        Assert.True(stopwatch.ElapsedMilliseconds >= 100, "Dispose 不應在背景工作仍未退出時立即返回。");
    }
}