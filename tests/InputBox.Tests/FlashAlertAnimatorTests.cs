using InputBox.Core.Feedback;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證主視窗、數值輸入與片語編輯共用的閃爍警示配色與動畫強度計算。
/// </summary>
public sealed class FlashAlertAnimatorTests
{
    /// <summary>
    /// 一般模式下，深色與淺色主題應分別使用 Firebrick 與 DarkOrange，高對比模式使用系統醒目提示色。
    /// </summary>
    [Fact]
    public void GetAlertColor_ReturnsThemeSpecificColor()
    {
        Assert.Equal(Color.Firebrick, FlashAlertAnimator.GetAlertColor(isDark: true, highContrast: false));
        Assert.Equal(Color.DarkOrange, FlashAlertAnimator.GetAlertColor(isDark: false, highContrast: false));
        Assert.Equal(SystemColors.Highlight, FlashAlertAnimator.GetAlertColor(isDark: true, highContrast: true));
    }

    /// <summary>
    /// 強度為 0 時背景應為純淨底色（深色用白、淺色用黑），強度為 1 時背景應等於警示色。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComputeFrameColors_InterpolatesFromPureBaseToAlertColor(bool isDark)
    {
        Color alert = FlashAlertAnimator.GetAlertColor(isDark, highContrast: false);

        (Color start, _) = FlashAlertAnimator.ComputeFrameColors(0f, isDark, alert, highContrast: false);
        (Color end, _) = FlashAlertAnimator.ComputeFrameColors(1f, isDark, alert, highContrast: false);

        Assert.Equal((isDark ? Color.White : Color.Black).ToArgb(), start.ToArgb());
        Assert.Equal(alert.ToArgb(), end.ToArgb());
    }

    /// <summary>
    /// 整個閃爍過程中，每一幀的文字與背景對比都必須維持 WCAG AA（≥4.5:1），避免在切換帶跌破可讀門檻。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComputeFrameColors_KeepsTextContrastAtLeastAaThroughoutPulse(bool isDark)
    {
        Color alert = FlashAlertAnimator.GetAlertColor(isDark, highContrast: false);

        for (int step = 0; step <= 100; step++)
        {
            float intensity = step / 100f;

            (Color back, Color fore) = FlashAlertAnimator.ComputeFrameColors(intensity, isDark, alert, highContrast: false);

            float lighter = Math.Max(FlashAlertAnimator.GetRelativeLuminance(back), FlashAlertAnimator.GetRelativeLuminance(fore));
            float darker = Math.Min(FlashAlertAnimator.GetRelativeLuminance(back), FlashAlertAnimator.GetRelativeLuminance(fore));
            float contrast = (lighter + 0.05f) / (darker + 0.05f);

            Assert.True(contrast >= 4.5f, $"isDark={isDark} intensity={intensity} contrast={contrast:F2}");
        }
    }

    /// <summary>
    /// 高對比模式不插值，強度過半即切換為醒目提示配色，否則維持系統視窗配色。
    /// </summary>
    [Fact]
    public void ComputeFrameColors_HighContrast_SwitchesAtHalfIntensity()
    {
        Color alert = FlashAlertAnimator.GetAlertColor(isDark: false, highContrast: true);

        (Color lowBack, Color lowFore) = FlashAlertAnimator.ComputeFrameColors(0.4f, isDark: false, alert, highContrast: true);
        (Color highBack, Color highFore) = FlashAlertAnimator.ComputeFrameColors(0.6f, isDark: false, alert, highContrast: true);

        Assert.Equal(SystemColors.Window, lowBack);
        Assert.Equal(SystemColors.WindowText, lowFore);
        Assert.Equal(alert, highBack);
        Assert.Equal(SystemColors.HighlightText, highFore);
    }

    /// <summary>
    /// 正弦脈衝應從 0 開始、在週期中點達到最大值 1，並在週期結束時回到 0。
    /// </summary>
    [Fact]
    public void ComputeIntensity_ProducesSinglePulsePerPeriod()
    {
        Assert.Equal(0f, FlashAlertAnimator.ComputeIntensity(0, 1000), 0.0001f);
        Assert.Equal(1f, FlashAlertAnimator.ComputeIntensity(500, 1000), 0.0001f);
        Assert.Equal(0f, FlashAlertAnimator.ComputeIntensity(1000, 1000), 0.0001f);
    }
}
