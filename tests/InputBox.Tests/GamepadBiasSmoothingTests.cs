using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證 XInput 與 GameInput 共用的自適應 EMA 學習率公式。
/// </summary>
public sealed class GamepadBiasSmoothingTests
{
    /// <summary>
    /// 誤差為 0 時應使用最低保守學習率，避免把有效輸入誤學成硬體偏移。
    /// </summary>
    [Fact]
    public void ComputeAdaptiveSmoothing_ZeroError_ReturnsBaseSmoothing()
    {
        float alpha = GamepadBiasSmoothing.ComputeAdaptiveSmoothing(0f, 0.05f, 0.03f, 0.15f);

        Assert.Equal(0.03f, alpha, 0.0001f);
    }

    /// <summary>
    /// 誤差達到或超過範圍時應使用最高學習率，且正負誤差結果相同。
    /// </summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(-0.05f)]
    [InlineData(0.5f)]
    public void ComputeAdaptiveSmoothing_ErrorAtOrBeyondRange_ReturnsMaxSmoothing(float error)
    {
        float alpha = GamepadBiasSmoothing.ComputeAdaptiveSmoothing(error, 0.05f, 0.03f, 0.15f);

        Assert.Equal(0.15f, alpha, 0.0001f);
    }

    /// <summary>
    /// 相同比例的誤差在 XInput（short 尺度）與 GameInput（浮點尺度）下必須得到相同學習率，確保兩個後端行為一致。
    /// </summary>
    [Fact]
    public void ComputeAdaptiveSmoothing_SameRelativeErrorAcrossScales_ReturnsSameAlpha()
    {
        float xinputAlpha = GamepadBiasSmoothing.ComputeAdaptiveSmoothing(
            819f,
            1638f,
            GamepadBiasSmoothing.LeftStickBiasXBaseSmoothing,
            GamepadBiasSmoothing.LeftStickBiasXMaxSmoothing);
        float gameInputAlpha = GamepadBiasSmoothing.ComputeAdaptiveSmoothing(
            0.025f,
            0.05f,
            GamepadBiasSmoothing.LeftStickBiasXBaseSmoothing,
            GamepadBiasSmoothing.LeftStickBiasXMaxSmoothing);

        Assert.Equal(xinputAlpha, gameInputAlpha, 0.0001f);
        Assert.Equal(0.09f, gameInputAlpha, 0.0001f);
    }
}
