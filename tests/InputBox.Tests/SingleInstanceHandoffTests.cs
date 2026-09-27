using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證程式內重啟的單一執行個體交接規則：交接參數解析、啟動動作與喚醒失敗後的 fallback 判斷。
/// <para>防止重啟時舊實例提早釋放 Mutex，被另外啟動的執行個體搶先取得而讓重啟失效，或在交接期間多開視窗。</para>
/// </summary>
public sealed class SingleInstanceHandoffTests
{
    /// <summary>
    /// 帶有有效交接參數時，應解析出舊執行個體的程序識別碼。
    /// </summary>
    [Fact]
    public void TryGetHandoffProcessId_WithValidArgument_ReturnsProcessId()
    {
        bool found = SingleInstanceHandoff.TryGetHandoffProcessId(
            ["InputBox.exe", "--other", "--restart-handoff=4321"],
            out int processId);

        Assert.True(found);
        Assert.Equal(4321, processId);
    }

    /// <summary>
    /// 沒有交接參數或交接參數的數值無效時，不應視為交接啟動。
    /// </summary>
    [Theory]
    [InlineData("--other")]
    [InlineData("--restart-handoff=")]
    [InlineData("--restart-handoff=abc")]
    [InlineData("--restart-handoff=-5")]
    [InlineData("--restart-handoff=0")]
    public void TryGetHandoffProcessId_WithoutValidArgument_ReturnsFalse(string argument)
    {
        bool found = SingleInstanceHandoff.TryGetHandoffProcessId(["InputBox.exe", argument], out int processId);

        Assert.False(found);
        Assert.Equal(0, processId);
    }

    /// <summary>
    /// 第一個元素是執行檔路徑，即使內容符合交接參數格式也不得被解析。
    /// </summary>
    [Fact]
    public void TryGetHandoffProcessId_IgnoresExecutablePathElement()
    {
        bool found = SingleInstanceHandoff.TryGetHandoffProcessId(["--restart-handoff=99"], out int processId);

        Assert.False(found);
        Assert.Equal(0, processId);
    }

    /// <summary>
    /// 移除交接參數時應保留執行檔路徑與其他引數的原本順序。
    /// </summary>
    [Fact]
    public void RemoveHandoffArguments_KeepsOtherArgumentsInOrder()
    {
        IReadOnlyList<string> result = SingleInstanceHandoff.RemoveHandoffArguments(
            ["InputBox.exe", "--a", "--restart-handoff=1", "--b", "--restart-handoff=2"]);

        Assert.Equal(["InputBox.exe", "--a", "--b"], result);
    }

    /// <summary>
    /// 啟動動作矩陣：取得 Mutex 即正常啟動；未取得時，交接啟動等待接手，其餘喚醒既有實例。
    /// </summary>
    [Theory]
    [InlineData(true, false, "Proceed")]
    [InlineData(true, true, "Proceed")]
    [InlineData(false, true, "WaitForHandoff")]
    [InlineData(false, false, "ActivateExisting")]
    public void ResolveStartupAction_ReturnsExpectedAction(
        bool createdNew,
        bool isHandoffLaunch,
        string expected)
    {
        Assert.Equal(expected, SingleInstanceHandoff.ResolveStartupAction(createdNew, isHandoffLaunch).ToString());
    }

    /// <summary>
    /// fallback 判斷矩陣：前景切換被阻擋時一律結束；重啟交接進行中時結束以免多開視窗；其餘允許 fallback。
    /// </summary>
    [Theory]
    [InlineData(false, false, "ExitForegroundBlocked")]
    [InlineData(false, true, "ExitForegroundBlocked")]
    [InlineData(true, true, "ExitRestartHandoffPending")]
    [InlineData(true, false, "TryFallback")]
    public void ResolveFallback_ReturnsExpectedDecision(
        bool fallbackPermitted,
        bool restartHandoffPending,
        string expected)
    {
        Assert.Equal(expected, SingleInstanceHandoff.ResolveFallback(fallbackPermitted, restartHandoffPending).ToString());
    }
}
