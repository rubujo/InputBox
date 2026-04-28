using InputBox.Core.Controls;
using InputBox.Core.Input;
using InputBox.Resources;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證校準視覺化的座標正規化與死區半徑換算，避免診斷圖超出邊界或錯誤縮放。
/// </summary>
public sealed class GamepadCalibrationVisualizerMapperTests
{
    [Theory]
    [InlineData(-2.0f, -1.0f)]
    [InlineData(-1.0f, -1.0f)]
    [InlineData(-0.25f, -0.25f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(2.0f, 1.0f)]
    public void ClampNormalized_RestrictsToVisibleRange(float input, float expected)
    {
        float actual = GamepadCalibrationVisualizerMapper.ClampNormalized(input);

        Assert.Equal(expected, actual, 3);
    }

    /// <summary>
    /// 死區半徑應依完整軸向範圍換算成正規化比例，避免校準圖縮放錯誤。
    /// </summary>
    [Fact]
    public void CalculateDeadzoneRadius_UsesFullScaleRatioAndClamps()
    {
        float actual = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(2500, 32768f);

        Assert.InRange(actual, 0.07f, 0.08f);
    }

    /// <summary>
    /// 死區值超過完整軸向範圍時應夾至最大半徑，避免繪製超出校準圖邊界。
    /// </summary>
    [Fact]
    public void CalculateDeadzoneRadius_WhenInputExceedsFullScale_ReturnsOne()
    {
        float actual = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(40000, 32768f);

        Assert.Equal(1.0f, actual, 3);
    }

    /// <summary>
    /// 控制器已連線的廣播文字應包含裝置名稱，且不應殘留格式化預留位置。
    /// </summary>
    [Fact]
    public void FormatConnectionAnnouncement_WhenConnected_IncludesDeviceName()
    {
        string actual = GamepadCalibrationDialog.FormatConnectionAnnouncement(true, "Xbox Wireless Controller");

        Assert.Contains("Xbox Wireless Controller", actual);
        Assert.DoesNotContain("{0}", actual);
    }

    /// <summary>
    /// 控制器未連線的廣播文字應使用斷線範本並帶入裝置名稱，避免播報未格式化字串。
    /// </summary>
    [Fact]
    public void FormatConnectionAnnouncement_WhenDisconnected_UsesDisconnectedTemplate()
    {
        string actual = GamepadCalibrationDialog.FormatConnectionAnnouncement(false, "DualSense");

        Assert.Contains("DualSense", actual);
        Assert.DoesNotContain("{0}", actual);
    }

    /// <summary>
    /// 已連線狀態文字應包含左右搖桿座標與死區數值，讓校準診斷資訊完整可讀。
    /// </summary>
    [Fact]
    public void FormatStatusText_WhenConnected_IncludesLeftAndRightStickValues()
    {
        GamepadCalibrationSnapshot snapshot = new()
        {
            IsConnected = true,
            RawLeftX = 0.10f,
            RawLeftY = -0.20f,
            CorrectedLeftX = 0.08f,
            CorrectedLeftY = -0.18f,
            RawRightX = 0.30f,
            RawRightY = -0.40f,
            CorrectedRightX = 0.28f,
            CorrectedRightY = -0.35f,
            ThumbDeadzoneEnter = 7849,
            ThumbDeadzoneExit = 2500
        };

        string actual = GamepadCalibrationDialog.FormatStatusText(snapshot);

        Assert.Contains("LS", actual);
        Assert.Contains("RS", actual);
        Assert.Contains("7849", actual);
        Assert.Contains("2500", actual);
    }

    /// <summary>
    /// 未連線狀態文字應回到資源檔中的斷線訊息，避免顯示空白或過期座標。
    /// </summary>
    [Fact]
    public void FormatStatusText_WhenDisconnected_UsesDisconnectedMessage()
    {
        string actual = GamepadCalibrationDialog.FormatStatusText(new GamepadCalibrationSnapshot { IsConnected = false });

        Assert.Equal(Strings.Dialog_GamepadCalibrationVisualizer_StatusDisconnected, actual);
    }

    /// <summary>
    /// 左搖桿位於中心區域時應允許 D-Pad 導覽焦點，避免校準對話框失去基本可操作性。
    /// </summary>
    [Fact]
    public void ShouldHandleDirectionalFocusNavigation_WhenStickIsCentered_ReturnsTrue()
    {
        bool actual = GamepadCalibrationDialog.ShouldHandleDirectionalFocusNavigation(new GamepadCalibrationSnapshot
        {
            IsConnected = true,
            RawLeftX = 0.01f,
            RawLeftY = -0.01f,
            CorrectedLeftX = 0.00f,
            CorrectedLeftY = 0.00f
        });

        Assert.True(actual);
    }

    /// <summary>
    /// 左搖桿已明顯偏移時應阻止 D-Pad 焦點導覽，避免同一輸入同時觸發兩種移動。
    /// </summary>
    [Fact]
    public void ShouldHandleDirectionalFocusNavigation_WhenLeftStickIsActive_ReturnsFalse()
    {
        bool actual = GamepadCalibrationDialog.ShouldHandleDirectionalFocusNavigation(new GamepadCalibrationSnapshot
        {
            IsConnected = true,
            RawLeftX = 0.62f,
            RawLeftY = 0.00f,
            CorrectedLeftX = 0.58f,
            CorrectedLeftY = 0.00f
        });

        Assert.False(actual);
    }

    /// <summary>
    /// 左搖桿剛跨越進入死區時應阻止焦點導覽，避免 stick-to-DPad 映射後又重複移動焦點。
    /// </summary>
    [Fact]
    public void ShouldHandleDirectionalFocusNavigation_WhenStickJustCrossesEnterDeadzone_ReturnsFalse()
    {
        // ThumbDeadzoneEnter = 7849 → normalizedDeadzone ≈ 0.2395
        // navigationThreshold = 0.2395 * 0.75 ≈ 0.1796
        // LS at -0.24 just crossed the enter threshold → stick→D-Pad mapping has fired.
        // Guard must block focus navigation in this case.
        bool actual = GamepadCalibrationDialog.ShouldHandleDirectionalFocusNavigation(new GamepadCalibrationSnapshot
        {
            IsConnected = true,
            ThumbDeadzoneEnter = 7849,
            ThumbDeadzoneExit = 2500,
            RawLeftX = -0.24f,
            RawLeftY = 0.00f,
            CorrectedLeftX = -0.24f,
            CorrectedLeftY = 0.00f
        });

        Assert.False(actual);
    }
}
