using InputBox.Core.Utilities;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// DialogLayoutHelper 的 DPI 防抖與工作區計算邏輯測試
/// </summary>
public class DialogLayoutHelperTests
{
    // ── TryBeginDpiLayout ──────────────────────────────────────────────────

    /// <summary>
    /// DPI 沒有變化時（差值 &lt; 0.01），不應觸發重新計算，回傳 false 且快取不變。
    /// </summary>
    [Fact]
    public void TryBeginDpiLayout_SameDpi_ReturnsFalse()
    {
        float lastDpi = 96f;
        bool result = DialogLayoutHelper.TryBeginDpiLayout(96f, ref lastDpi);

        Assert.False(result);
        Assert.Equal(96f, lastDpi);
    }

    /// <summary>
    /// DPI 在 0.01 以內的微小差值不應視為變化，回傳 false。
    /// </summary>
    [Fact]
    public void TryBeginDpiLayout_TinyDiff_ReturnsFalse()
    {
        float lastDpi = 96f;
        bool result = DialogLayoutHelper.TryBeginDpiLayout(96.005f, ref lastDpi);

        Assert.False(result);
    }

    /// <summary>
    /// DPI 變化超過 0.01 時，應觸發重新計算，回傳 true 並更新快取值。
    /// </summary>
    [Fact]
    public void TryBeginDpiLayout_ChangedDpi_ReturnsTrueAndUpdatesCache()
    {
        float lastDpi = 96f;
        bool result = DialogLayoutHelper.TryBeginDpiLayout(120f, ref lastDpi);

        Assert.True(result);
        Assert.Equal(120f, lastDpi);
    }

    /// <summary>
    /// forceRecalculate = true 時，即使 DPI 相同也應強制重新計算並更新快取。
    /// </summary>
    [Fact]
    public void TryBeginDpiLayout_ForceRecalculate_ReturnsTrueEvenIfSameDpi()
    {
        float lastDpi = 96f;
        bool result = DialogLayoutHelper.TryBeginDpiLayout(96f, ref lastDpi, forceRecalculate: true);

        Assert.True(result);
        Assert.Equal(96f, lastDpi);
    }

    /// <summary>
    /// DPI 從高降至低也應觸發重新計算（確保縮小場景同樣被偵測）。
    /// </summary>
    [Fact]
    public void TryBeginDpiLayout_DpiDecreased_ReturnsTrueAndUpdatesCache()
    {
        float lastDpi = 144f;
        bool result = DialogLayoutHelper.TryBeginDpiLayout(96f, ref lastDpi);

        Assert.True(result);
        Assert.Equal(96f, lastDpi);
    }

    // ── GetMaxFitSize ──────────────────────────────────────────────────────

    /// <summary>
    /// 一般工作區應回傳 workArea 扣除 margin 的寬高。
    /// </summary>
    [Fact]
    public void GetMaxFitSize_NormalWorkArea_ReturnsWidthAndHeightMinusMargin()
    {
        Rectangle workArea = new(0, 0, 1920, 1080);
        var (w, h) = DialogLayoutHelper.GetMaxFitSize(workArea, margin: 40);

        Assert.Equal(1880, w);
        Assert.Equal(1040, h);
    }

    /// <summary>
    /// 不傳 margin 時應使用預設值 40，結果相同。
    /// </summary>
    [Fact]
    public void GetMaxFitSize_DefaultMargin_Is40()
    {
        Rectangle workArea = new(0, 0, 800, 600);
        var (w, h) = DialogLayoutHelper.GetMaxFitSize(workArea);

        Assert.Equal(760, w);
        Assert.Equal(560, h);
    }

    /// <summary>
    /// 工作區寬度小於或等於 margin 時，MaxFitWidth 應回傳最小值 1，不允許 &lt;= 0。
    /// </summary>
    [Fact]
    public void GetMaxFitSize_WorkAreaSmallerThanMargin_ReturnsAtLeastOne()
    {
        Rectangle workArea = new(0, 0, 30, 30);
        var (w, h) = DialogLayoutHelper.GetMaxFitSize(workArea, margin: 40);

        Assert.Equal(1, w);
        Assert.Equal(1, h);
    }

    /// <summary>
    /// margin = 0 時回傳值應等於工作區的實際寬高。
    /// </summary>
    [Fact]
    public void GetMaxFitSize_ZeroMargin_ReturnsExactWorkAreaSize()
    {
        Rectangle workArea = new(0, 0, 1280, 720);
        var (w, h) = DialogLayoutHelper.GetMaxFitSize(workArea, margin: 0);

        Assert.Equal(1280, w);
        Assert.Equal(720, h);
    }
}