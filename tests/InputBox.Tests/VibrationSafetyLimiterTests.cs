using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// VibrationSafetyLimiter 的硬體保護策略測試。
/// <para>這些測試不依賴實體手把，可在 CI 與無手把環境穩定執行。</para>
/// </summary>
public class VibrationSafetyLimiterTests
{
    /// <summary>
    /// 當占空比已超過上限時，Ambient 優先級應被阻擋以保護硬體。
    /// </summary>
    [Fact]
    public void TryApply_Ambient_WhenDutyCycleExceeded_ShouldBeBlocked()
    {
        var limiter = new VibrationSafetyLimiter(
            windowMs: 1000,
            maxDutyCycle: 0.20,
            thermalSoftBudget: 1000,
            thermalHardBudget: 2000,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        bool first = limiter.TryApply(40_000, 300, VibrationPriority.Ambient, nowMs: 0, out _, out _);
        bool second = limiter.TryApply(40_000, 300, VibrationPriority.Ambient, nowMs: 10, out _, out _);

        Assert.True(first);
        Assert.False(second);
    }

    /// <summary>
    /// 當占空比超限時，Critical 優先級仍應保留可用，避免 A11y 關鍵回饋中斷。
    /// </summary>
    [Fact]
    public void TryApply_Critical_WhenDutyCycleExceeded_ShouldStillPass()
    {
        var limiter = new VibrationSafetyLimiter(
            windowMs: 1000,
            maxDutyCycle: 0.20,
            thermalSoftBudget: 1000,
            thermalHardBudget: 2000,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        bool first = limiter.TryApply(40_000, 300, VibrationPriority.Ambient, nowMs: 0, out _, out _);
        bool critical = limiter.TryApply(45_000, 200, VibrationPriority.Critical, nowMs: 10, out ushort adjustedStrength, out _);

        Assert.True(first);
        Assert.True(critical);
        Assert.True(adjustedStrength > 0);
    }

    /// <summary>
    /// 熱負載達高水位後，Normal 優先級應被降級而非直接關閉。
    /// </summary>
    [Fact]
    public void TryApply_Normal_WhenThermalLoadHigh_ShouldBeScaledDown()
    {
        var limiter = new VibrationSafetyLimiter(
            windowMs: 5000,
            maxDutyCycle: 0.95,
            thermalSoftBudget: 10,
            thermalHardBudget: 35,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        bool warmup = limiter.TryApply(ushort.MaxValue, 20, VibrationPriority.Normal, nowMs: 0, out _, out _);
        bool scaled = limiter.TryApply(ushort.MaxValue, 20, VibrationPriority.Normal, nowMs: 1, out ushort adjustedStrength, out int adjustedDuration);

        Assert.True(warmup);
        Assert.True(scaled);
        Assert.True(adjustedStrength < ushort.MaxValue);
        Assert.InRange(adjustedDuration, 20, 20);
    }

    /// <summary>
    /// 當 Ambient 因熱軟限制被降級時，仍應保留可感知的最低強度與時長。
    /// </summary>
    [Fact]
    public void TryApply_Ambient_WhenThermalSoftScaled_ShouldKeepPerceptibleFloor()
    {
        var limiter = new VibrationSafetyLimiter(
            windowMs: 5000,
            maxDutyCycle: 0.95,
            thermalSoftBudget: 10,
            thermalHardBudget: 200,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        bool warmup = limiter.TryApply(ushort.MaxValue, 20, VibrationPriority.Normal, nowMs: 0, out _, out _);
        bool scaledAmbient = limiter.TryApply(12_600, 50, VibrationPriority.Ambient, nowMs: 1, out ushort adjustedStrength, out int adjustedDuration);

        Assert.True(warmup);
        Assert.True(scaledAmbient);
        Assert.InRange(adjustedStrength, 10_000, 12_600);
        Assert.InRange(adjustedDuration, 35, 50);
    }

    /// <summary>
    /// 當熱成本倍率提高時，限制器應更早進入降級狀態，模擬多馬達同時驅動。
    /// </summary>
    [Fact]
    public void TryApply_WhenThermalCostMultiplierHigher_ShouldScaleEarlier()
    {
        var limiterNormal = new VibrationSafetyLimiter(
            windowMs: 5000,
            maxDutyCycle: 0.95,
            thermalSoftBudget: 120,
            thermalHardBudget: 180,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        var limiterFourMotor = new VibrationSafetyLimiter(
            windowMs: 5000,
            maxDutyCycle: 0.95,
            thermalSoftBudget: 120,
            thermalHardBudget: 180,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        bool normalAccepted = limiterNormal.TryApply(
            45_000,
            200,
            VibrationPriority.Critical,
            nowMs: 0,
            out ushort normalStrength,
            out _,
            thermalCostMultiplier: 1.0);

        bool fourMotorAccepted = limiterFourMotor.TryApply(
            45_000,
            200,
            VibrationPriority.Critical,
            nowMs: 0,
            out ushort fourMotorStrength,
            out _,
            thermalCostMultiplier: 4.0);

        bool normalSecond = limiterNormal.TryApply(
            45_000,
            200,
            VibrationPriority.Critical,
            nowMs: 1,
            out ushort normalSecondStrength,
            out _);

        bool fourMotorSecond = limiterFourMotor.TryApply(
            45_000,
            200,
            VibrationPriority.Critical,
            nowMs: 1,
            out ushort fourMotorSecondStrength,
            out _,
            thermalCostMultiplier: 4.0);

        Assert.True(normalAccepted);
        Assert.True(fourMotorAccepted);
        Assert.True(normalSecond);
        Assert.True(fourMotorSecond);
        Assert.True(normalStrength >= fourMotorStrength);
        Assert.True(normalSecondStrength > fourMotorSecondStrength);
    }

    /// <summary>
    /// 斷線重連場景下，Reset 後應清空熱狀態，避免延續舊負載造成過度保守。
    /// </summary>
    [Fact]
    public void Reset_AfterHighThermalLoad_ShouldRestoreHeadroom()
    {
        var limiter = new VibrationSafetyLimiter(
            windowMs: 5000,
            maxDutyCycle: 0.95,
            thermalSoftBudget: 20,
            thermalHardBudget: 40,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        bool warmup = limiter.TryApply(
            65_535,
            250,
            VibrationPriority.Critical,
            nowMs: 0,
            out ushort beforeResetStrength,
            out _);

        limiter.Reset();

        bool afterReset = limiter.TryApply(
            65_535,
            250,
            VibrationPriority.Critical,
            nowMs: 1,
            out ushort afterResetStrength,
            out _);

        Assert.True(warmup);
        Assert.True(afterReset);
        Assert.True(afterResetStrength >= beforeResetStrength);
    }

    /// <summary>
    /// 極短震動脈衝也應安全處理，不可因內部下限計算而拋出例外。
    /// </summary>
    [Fact]
    public void TryApply_WhenDurationIsShort_ShouldStayWithinRequestedUpperBound()
    {
        var limiter = new VibrationSafetyLimiter();

        bool accepted = limiter.TryApply(
            30_000,
            10,
            VibrationPriority.Normal,
            nowMs: 0,
            out ushort adjustedStrength,
            out int adjustedDuration);

        Assert.True(accepted);
        Assert.True(adjustedStrength > 0);
        Assert.InRange(adjustedDuration, 1, 10);
    }

    /// <summary>
    /// 即使收到極端強度請求，限制器也應保留保守的硬體安全上限。
    /// </summary>
    [Fact]
    public void TryApply_WhenStrengthIsMaxValue_ShouldClampToSafetyCeiling()
    {
        var limiter = new VibrationSafetyLimiter(
            windowMs: 5000,
            maxDutyCycle: 0.95,
            thermalSoftBudget: 1000,
            thermalHardBudget: 2000,
            thermalTauMs: 10_000,
            ambientCooldownMs: 50);

        bool accepted = limiter.TryApply(
            ushort.MaxValue,
            100,
            VibrationPriority.Critical,
            nowMs: 0,
            out ushort adjustedStrength,
            out _);

        Assert.True(accepted);
        Assert.InRange(adjustedStrength, 1, 60_000);
    }
}