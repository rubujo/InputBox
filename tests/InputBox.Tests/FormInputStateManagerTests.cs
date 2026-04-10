using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// FormInputStateManager 的 Interlocked 狀態旗標管理邏輯測試
/// <para>驗證所有「嘗試進入 / 結束」的互斥保護行為，確保並行狀態機正確防止重入。</para>
/// </summary>
public class FormInputStateManagerTests
{
    // ── TryBeginReturning / EndReturning ───────────────────────────────────

    /// <summary>
    /// 初始狀態下 TryBeginReturning 應成功（回傳 true）。
    /// </summary>
    [Fact]
    public void TryBeginReturning_WhenIdle_ReturnsTrue()
    {
        var mgr = new FormInputStateManager();
        Assert.True(mgr.TryBeginReturning());
    }

    /// <summary>
    /// 已進入流程時，再次 TryBeginReturning 應失敗（回傳 false），防止重入。
    /// </summary>
    [Fact]
    public void TryBeginReturning_WhenAlreadyReturning_ReturnsFalse()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginReturning();
        Assert.False(mgr.TryBeginReturning());
    }

    /// <summary>
    /// EndReturning 後，TryBeginReturning 應再次成功。
    /// </summary>
    [Fact]
    public void EndReturning_ReleasesLock_AllowsNextTryBegin()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginReturning();
        mgr.EndReturning();
        Assert.True(mgr.TryBeginReturning());
    }

    // ── TryBeginTouchKeyboard / EndTouchKeyboard ───────────────────────────

    /// <summary>
    /// 初始狀態下 TryBeginTouchKeyboard 應成功。
    /// </summary>
    [Fact]
    public void TryBeginTouchKeyboard_WhenIdle_ReturnsTrue()
    {
        var mgr = new FormInputStateManager();
        Assert.True(mgr.TryBeginTouchKeyboard());
    }

    /// <summary>
    /// 已進入流程時，再次嘗試應失敗。
    /// </summary>
    [Fact]
    public void TryBeginTouchKeyboard_WhenBusy_ReturnsFalse()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginTouchKeyboard();
        Assert.False(mgr.TryBeginTouchKeyboard());
    }

    /// <summary>
    /// EndTouchKeyboard 後 IsShowingTouchKeyboard 屬性應為 false。
    /// </summary>
    [Fact]
    public void EndTouchKeyboard_ResetsIsShowingTouchKeyboard()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginTouchKeyboard();
        mgr.EndTouchKeyboard();
        Assert.False(mgr.IsShowingTouchKeyboard);
    }

    /// <summary>
    /// TryBeginTouchKeyboard 成功後，IsShowingTouchKeyboard 應為 true。
    /// </summary>
    [Fact]
    public void IsShowingTouchKeyboard_AfterTryBegin_IsTrue()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginTouchKeyboard();
        Assert.True(mgr.IsShowingTouchKeyboard);
    }

    // ── TryBeginFlashing / EndFlashing ─────────────────────────────────────

    /// <summary>
    /// 閃爍流程的互斥保護：進入後再次嘗試應失敗。
    /// </summary>
    [Fact]
    public void TryBeginFlashing_WhenBusy_ReturnsFalse()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginFlashing();
        Assert.False(mgr.TryBeginFlashing());
    }

    /// <summary>
    /// EndFlashing 後應允許再次進入閃爍流程。
    /// </summary>
    [Fact]
    public void EndFlashing_AllowsNextTryBeginFlashing()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginFlashing();
        mgr.EndFlashing();
        Assert.True(mgr.TryBeginFlashing());
    }

    // ── TryBeginProcessingActivated / EndProcessingActivated ───────────────

    /// <summary>
    /// Activated 事件處理流程的互斥保護：進入後再次嘗試應失敗。
    /// </summary>
    [Fact]
    public void TryBeginProcessingActivated_WhenBusy_ReturnsFalse()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginProcessingActivated();
        Assert.False(mgr.TryBeginProcessingActivated());
    }

    /// <summary>
    /// EndProcessingActivated 後應允許再次進入。
    /// </summary>
    [Fact]
    public void EndProcessingActivated_AllowsNextTryBegin()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginProcessingActivated();
        mgr.EndProcessingActivated();
        Assert.True(mgr.TryBeginProcessingActivated());
    }

    // ── BeginHotkeyCapture / EndHotkeyCapture / IsHotkeyCaptureActive ──────

    /// <summary>
    /// 初始狀態下 IsHotkeyCaptureActive 應為 false。
    /// </summary>
    [Fact]
    public void IsHotkeyCaptureActive_Initially_IsFalse()
    {
        var mgr = new FormInputStateManager();
        Assert.False(mgr.IsHotkeyCaptureActive);
    }

    /// <summary>
    /// BeginHotkeyCapture 後 IsHotkeyCaptureActive 應為 true。
    /// </summary>
    [Fact]
    public void BeginHotkeyCapture_SetsIsHotkeyCaptureActiveTrue()
    {
        var mgr = new FormInputStateManager();
        mgr.BeginHotkeyCapture();
        Assert.True(mgr.IsHotkeyCaptureActive);
    }

    /// <summary>
    /// EndHotkeyCapture 後 IsHotkeyCaptureActive 應為 false。
    /// </summary>
    [Fact]
    public void EndHotkeyCapture_ClearsIsHotkeyCaptureActive()
    {
        var mgr = new FormInputStateManager();
        mgr.BeginHotkeyCapture();
        mgr.EndHotkeyCapture();
        Assert.False(mgr.IsHotkeyCaptureActive);
    }

    // ── 各流程互不干擾 ──────────────────────────────────────────────────────

    /// <summary>
    /// 各個狀態旗標應彼此獨立：進入 Returning 不影響 Flashing 的 TryBegin。
    /// </summary>
    [Fact]
    public void MultipleLocks_AreIndependent()
    {
        var mgr = new FormInputStateManager();
        mgr.TryBeginReturning();
        mgr.TryBeginFlashing();

        // 各自被鎖定
        Assert.False(mgr.TryBeginReturning());
        Assert.False(mgr.TryBeginFlashing());

        // 釋放其中一個不影響另一個
        mgr.EndReturning();
        Assert.True(mgr.TryBeginReturning());
        Assert.False(mgr.TryBeginFlashing());
    }
}
