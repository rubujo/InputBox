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

    [Fact]
    public void CalculateDeadzoneRadius_UsesFullScaleRatioAndClamps()
    {
        float actual = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(2500, 32768f);

        Assert.InRange(actual, 0.07f, 0.08f);
    }

    [Fact]
    public void CalculateDeadzoneRadius_WhenInputExceedsFullScale_ReturnsOne()
    {
        float actual = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(40000, 32768f);

        Assert.Equal(1.0f, actual, 3);
    }

    [Fact]
    public void FormatConnectionAnnouncement_WhenConnected_IncludesDeviceName()
    {
        string actual = GamepadCalibrationDialog.FormatConnectionAnnouncement(true, "Xbox Wireless Controller");

        Assert.Contains("Xbox Wireless Controller", actual);
        Assert.DoesNotContain("{0}", actual);
    }

    [Fact]
    public void FormatConnectionAnnouncement_WhenDisconnected_UsesDisconnectedTemplate()
    {
        string actual = GamepadCalibrationDialog.FormatConnectionAnnouncement(false, "DualSense");

        Assert.Contains("DualSense", actual);
        Assert.DoesNotContain("{0}", actual);
    }

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

    [Fact]
    public void FormatStatusText_WhenDisconnected_UsesDisconnectedMessage()
    {
        string actual = GamepadCalibrationDialog.FormatStatusText(new GamepadCalibrationSnapshot { IsConnected = false });

        Assert.Equal(Strings.Dialog_GamepadCalibrationVisualizer_StatusDisconnected, actual);
    }

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
}
