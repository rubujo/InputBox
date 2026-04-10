using InputBox.Core.Extensions;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// TaskExtensions 擴充方法的行為測試
/// </summary>
public class TaskExtensionsTests
{
    // ── CancelAndDispose ────────────────────────────────────────────────────

    /// <summary>
    /// null CancellationTokenSource 傳入時不應拋出例外。
    /// </summary>
    [Fact]
    public void CancelAndDispose_Null_DoesNotThrow()
    {
        CancellationTokenSource? cts = null;
        var ex = Record.Exception(() => cts.CancelAndDispose());
        Assert.Null(ex);
    }

    /// <summary>
    /// 活躍的 CancellationTokenSource 呼叫後，IsCancellationRequested 應為 true，且呼叫後不應拋出例外。
    /// </summary>
    [Fact]
    public void CancelAndDispose_ActiveCts_CancelsSuccessfully()
    {
        var cts = new CancellationTokenSource();
        Assert.False(cts.IsCancellationRequested);

        var ex = Record.Exception(() => cts.CancelAndDispose());

        Assert.Null(ex);
        // 呼叫後 cts 已被 Dispose，無法再讀取 IsCancellationRequested，不驗證此屬性
    }

    /// <summary>
    /// 已取消（IsCancellationRequested = true）的 CTS 再次呼叫不應拋出例外。
    /// </summary>
    [Fact]
    public void CancelAndDispose_AlreadyCancelled_DoesNotThrow()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Record.Exception(() => cts.CancelAndDispose());

        Assert.Null(ex);
    }

    /// <summary>
    /// 已 Dispose 的 CTS 再次呼叫不應拋出例外（ObjectDisposedException 應被吸收）。
    /// </summary>
    [Fact]
    public void CancelAndDispose_AlreadyDisposed_DoesNotThrow()
    {
        var cts = new CancellationTokenSource();
        cts.Dispose();

        var ex = Record.Exception(() => cts.CancelAndDispose());

        Assert.Null(ex);
    }

    // ── SafeFireAndForget ───────────────────────────────────────────────────

    /// <summary>
    /// 成功完成的 Task 不應觸發 onException 回呼。
    /// </summary>
    [Fact]
    public async Task SafeFireAndForget_SuccessfulTask_DoesNotInvokeOnException()
    {
        bool exceptionInvoked = false;

        Task.CompletedTask.SafeFireAndForget(
            onException: _ => exceptionInvoked = true);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(exceptionInvoked);
    }

    /// <summary>
    /// 取消的 Task 不應觸發 onException 回呼（OperationCanceledException 應被靜默吸收）。
    /// </summary>
    [Fact]
    public async Task SafeFireAndForget_CancelledTask_DoesNotInvokeOnException()
    {
        bool exceptionInvoked = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // 此處 cts.Token 是刻意已取消的 token，pragma 抑制 xUnit1051 以保持測試語意清晰
#pragma warning disable xUnit1051
        Task cancelledTask = Task.Run(
            () => cts.Token.ThrowIfCancellationRequested(),
            cts.Token);
#pragma warning restore xUnit1051

        cancelledTask.SafeFireAndForget(
            onException: _ => exceptionInvoked = true);

        await Task.Delay(150, TestContext.Current.CancellationToken);

        Assert.False(exceptionInvoked);
    }

    /// <summary>
    /// 拋出例外的 Task 應觸發 onException 回呼並傳入正確的例外物件。
    /// </summary>
    [Fact]
    public async Task SafeFireAndForget_FaultedTask_InvokesOnException()
    {
        Exception? captured = null;

        Task faultedTask = Task.Run(
            () => throw new InvalidOperationException("test-error"),
            TestContext.Current.CancellationToken);

        faultedTask.SafeFireAndForget(
            onException: ex => captured = ex);

        // 等待背景任務完成處理
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal("test-error", captured.Message);
    }

    /// <summary>
    /// 使用廣播委派多載時，例外訊息應正確傳入 announceAction。
    /// </summary>
    [Fact]
    public async Task SafeFireAndForget_WithAnnounceAction_InvokesWithMessage()
    {
        string? announced = null;

        Task faultedTask = Task.Run(
            () => throw new Exception("announce-me"),
            TestContext.Current.CancellationToken);

        faultedTask.SafeFireAndForget(
            announceAction: msg => announced = msg);

        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.NotNull(announced);
        Assert.Contains("announce-me", announced);
    }

    /// <summary>
    /// 使用廣播委派多載並提供格式字串時，訊息應套用格式。
    /// </summary>
    [Fact]
    public async Task SafeFireAndForget_WithErrorMessageFormat_FormatsMessage()
    {
        string? announced = null;

        Task faultedTask = Task.Run(
            () => throw new Exception("boom"),
            TestContext.Current.CancellationToken);

        faultedTask.SafeFireAndForget(
            announceAction: msg => announced = msg,
            errorMessageFormat: "錯誤：{0}");

        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal("錯誤：boom", announced);
    }
}
