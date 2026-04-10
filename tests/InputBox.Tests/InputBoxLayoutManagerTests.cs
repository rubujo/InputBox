using InputBox.Core.Utilities;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// InputBoxLayoutManager.UpdateMinimumSize DPI 防抖邏輯測試
/// </summary>
public class InputBoxLayoutManagerTests
{
    // ── UpdateMinimumSize ──────────────────────────────────────────────────

    /// <summary>
    /// DPI 沒有變化（差值 &lt; 0.01）時，不應呼叫任何回呼，並回傳原始快取值。
    /// </summary>
    [Fact]
    public void UpdateMinimumSize_SameDpi_DoesNotInvokeCallbacks()
    {
        bool layoutCalled = false;
        bool opacityCalled = false;

        float result = InputBoxLayoutManager.UpdateMinimumSize(
            currentDpi: 96f,
            lastAppliedDpi: 96f,
            updateLayoutConstraints: () => layoutCalled = true,
            updateOpacityWhenHighContrast: () => opacityCalled = true);

        Assert.False(layoutCalled);
        Assert.False(opacityCalled);
        Assert.Equal(96f, result);
    }

    /// <summary>
    /// DPI 在 0.01 閾值以內的微小差距不應觸發更新。
    /// </summary>
    [Fact]
    public void UpdateMinimumSize_TinyDiffBelowThreshold_DoesNotInvokeCallbacks()
    {
        bool layoutCalled = false;

        float result = InputBoxLayoutManager.UpdateMinimumSize(
            currentDpi: 96.005f,
            lastAppliedDpi: 96f,
            updateLayoutConstraints: () => layoutCalled = true,
            updateOpacityWhenHighContrast: () => { });

        Assert.False(layoutCalled);
        // 快取不應被更新（小於閾值回傳 lastAppliedDpi）
        Assert.Equal(96f, result);
    }

    /// <summary>
    /// DPI 有明確變化時，應呼叫 updateLayoutConstraints 回呼並回傳新的 DPI 值。
    /// </summary>
    [Fact]
    public void UpdateMinimumSize_ChangedDpi_InvokesLayoutCallback()
    {
        bool layoutCalled = false;

        float result = InputBoxLayoutManager.UpdateMinimumSize(
            currentDpi: 120f,
            lastAppliedDpi: 96f,
            updateLayoutConstraints: () => layoutCalled = true,
            updateOpacityWhenHighContrast: () => { });

        Assert.True(layoutCalled);
        Assert.Equal(120f, result);
    }

    /// <summary>
    /// DPI 從高降至低也應觸發 updateLayoutConstraints（縮小場景同樣被偵測）。
    /// </summary>
    [Fact]
    public void UpdateMinimumSize_DpiDecreased_InvokesLayoutCallback()
    {
        bool layoutCalled = false;

        float result = InputBoxLayoutManager.UpdateMinimumSize(
            currentDpi: 96f,
            lastAppliedDpi: 144f,
            updateLayoutConstraints: () => layoutCalled = true,
            updateOpacityWhenHighContrast: () => { });

        Assert.True(layoutCalled);
        Assert.Equal(96f, result);
    }
}
