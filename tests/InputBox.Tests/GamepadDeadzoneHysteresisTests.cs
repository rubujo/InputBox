using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// <see cref="GamepadDeadzoneHysteresis"/> 單元測試
/// <para>
/// 遲滯（Hysteresis）機制：進入死區使用較高的 enterThreshold，
/// 退出死區使用較低的 exitThreshold，以防止軸值在邊界附近頻繁跳動。
/// </para>
/// </summary>
public class GamepadDeadzoneHysteresisTests
{
    // ── ResolveDirection (int overload) ──────────────────────────────────

    /// <summary>
    /// 軸值為 0 且無先前方向時，應回傳中立值 0。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_AxisZero_ReturnsNeutral()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 0,
            wasNegative: false, wasPositive: false,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(0, result);
    }

    /// <summary>
    /// 未持有方向（wasPositive=false）且軸值超過 enterThreshold（8000）時，
    /// 應進入正方向並回傳 1。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_AboveEnterThreshold_ReturnsPositive()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 9000,
            wasNegative: false, wasPositive: false,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(1, result);
    }

    /// <summary>
    /// 未持有方向且軸值低於負 enterThreshold（-8000）時，應進入負方向並回傳 -1。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_BelowNegativeEnterThreshold_ReturnsNegative()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: -9000,
            wasNegative: false, wasPositive: false,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(-1, result);
    }

    /// <summary>
    /// 持有正方向（wasPositive=true）且軸值仍高於 exitThreshold（6000）時，
    /// 應保持正方向回傳 1（遲滯：使用較低的退出閾值）。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_WasPositive_StaysPositiveAboveExitThreshold()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 7000,
            wasNegative: false, wasPositive: true,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(1, result);
    }

    /// <summary>
    /// 持有正方向（wasPositive=true）但軸值低於 exitThreshold（6000）時，
    /// 應退出正方向並回傳中立值 0。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_WasPositive_ExitsBelowExitThreshold()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 5000,
            wasNegative: false, wasPositive: true,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(0, result);
    }

    /// <summary>
    /// 未持有方向且軸值介於 exitThreshold（6000）與 enterThreshold（8000）之間時，
    /// 不應進入正方向（回傳 0）。驗證進入需跨越較高的 enterThreshold。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_NotWasPositive_DoesNotEnterBelowEnterThreshold()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 7000,
            wasNegative: false, wasPositive: false,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(0, result);
    }

    /// <summary>
    /// 持有負方向（wasNegative=true）且軸值仍低於負 exitThreshold（-6000）時，
    /// 應保持負方向回傳 -1。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_WasNegative_StaysNegativeAboveExitThreshold()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: -7000,
            wasNegative: true, wasPositive: false,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(-1, result);
    }

    /// <summary>
    /// 持有負方向（wasNegative=true）但軸值高於負 exitThreshold（-6000）時，
    /// 應退出負方向並回傳中立值 0。
    /// </summary>
    [Fact]
    public void ResolveDirection_Int_WasNegative_ExitsBelowExitThreshold()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: -5000,
            wasNegative: true, wasPositive: false,
            enterThreshold: 8000, exitThreshold: 6000);

        Assert.Equal(0, result);
    }

    // ── ResolveDirection (float overload) ────────────────────────────────

    /// <summary>
    /// 浮點數多載：未持有方向且軸值超過 enterThreshold（0.8f）時，應回傳正方向 1。
    /// </summary>
    [Fact]
    public void ResolveDirection_Float_AboveEnterThreshold_ReturnsPositive()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 0.9f,
            wasNegative: false, wasPositive: false,
            enterThreshold: 0.8f, exitThreshold: 0.6f);

        Assert.Equal(1, result);
    }

    /// <summary>
    /// 浮點數多載：持有正方向且軸值介於 exitThreshold（0.6f）與 enterThreshold（0.8f）之間時，
    /// 應保持正方向回傳 1（遲滯效果）。
    /// </summary>
    [Fact]
    public void ResolveDirection_Float_WasPositive_StaysPositiveAboveExitThreshold()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 0.7f,
            wasNegative: false, wasPositive: true,
            enterThreshold: 0.8f, exitThreshold: 0.6f);

        Assert.Equal(1, result);
    }

    /// <summary>
    /// 浮點數多載：持有正方向但軸值低於 exitThreshold（0.6f）時，應退出並回傳 0。
    /// </summary>
    [Fact]
    public void ResolveDirection_Float_WasPositive_ExitsBelowExitThreshold()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: 0.5f,
            wasNegative: false, wasPositive: true,
            enterThreshold: 0.8f, exitThreshold: 0.6f);

        Assert.Equal(0, result);
    }

    /// <summary>
    /// 浮點數多載：未持有方向且軸值低於負 enterThreshold（-0.8f）時，應回傳負方向 -1。
    /// </summary>
    [Fact]
    public void ResolveDirection_Float_NegativeAxis_ReturnsNegative()
    {
        int result = GamepadDeadzoneHysteresis.ResolveDirection(
            axisValue: -0.9f,
            wasNegative: false, wasPositive: false,
            enterThreshold: 0.8f, exitThreshold: 0.6f);

        Assert.Equal(-1, result);
    }
}
