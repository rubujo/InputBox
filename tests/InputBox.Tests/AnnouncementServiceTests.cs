using InputBox.Core.Services;
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
        await Task.Delay(600);

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

        await Task.Delay(600);

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
        string? received = await Task.WhenAny(tcs.Task, Task.Delay(2000)) == tcs.Task
            ? tcs.Task.Result
            : null;

        Assert.Equal("測試廣播", received);
    }

    /// <summary>
    /// Dispose 應可安全呼叫多次而不拋出例外（冪等保護）。
    /// </summary>
    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        _svc = new AnnouncementService(async (_, _, _) => await Task.CompletedTask);

        var ex = Record.Exception(() =>
        {
            _svc.Dispose();
            _svc.Dispose();
        });

        Assert.Null(ex);
    }
}
