using InputBox.Core.Feedback;
using InputBox.Core.Input;
using System.Reflection;
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

    /// <summary>
    /// 馬達數量倍率應以雙主馬達為基準正規化，並限制在 1 到 4 顆馬達的範圍內。
    /// </summary>
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1, 0.5)]
    [InlineData(2, 1.0)]
    [InlineData(4, 2.0)]
    [InlineData(8, 2.0)]
    public void GetMotorThermalCostMultiplier_NormalizesToDualMotorBaseline(int motorCount, double expected)
    {
        Assert.Equal(expected, VibrationSafetyLimiter.GetMotorThermalCostMultiplier(motorCount));
    }

    /// <summary>
    /// 回歸保護：限制器剛啟動、尚無熱負載時，所有內建震動模式在各種強度與馬達數量下都必須能送出，
    /// 且強度不得低於 Normal 優先級保底比例。先前「複製成功」等重要回饋會因單次熱成本超過硬上限而被直接拒絕。
    /// </summary>
    [Fact]
    public void TryApply_AllBuiltInPatternsFromColdState_AreNeverBlocked()
    {
        FieldInfo[] patternFields = [.. typeof(VibrationPatterns)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(VibrationProfile))];

        Assert.NotEmpty(patternFields);

        float[] intensities = [0.3f, 0.7f, 1.0f];
        int[] motorCounts = [1, 2, 4];

        foreach (FieldInfo field in patternFields)
        {
            VibrationProfile pattern = (VibrationProfile)field.GetValue(null)!;

            foreach (float intensity in intensities)
            {
                VibrationProfile profile = pattern.ApplyIntensityMultiplier(intensity);

                if (profile.Strength == 0)
                {
                    continue;
                }

                foreach (int motorCount in motorCounts)
                {
                    var limiter = new VibrationSafetyLimiter();

                    bool accepted = limiter.TryApplyWithDiagnostics(
                        profile.Strength,
                        profile.Duration,
                        VibrationPriority.Normal,
                        nowMs: 1,
                        out ushort adjustedStrength,
                        out _,
                        out VibrationLimiterDebugInfo diagnostics,
                        thermalCostMultiplier: VibrationSafetyLimiter.GetMotorThermalCostMultiplier(motorCount));

                    Assert.True(
                        accepted,
                        $"{field.Name} intensity={intensity} motors={motorCount} flags={diagnostics.Flags}");
                    Assert.True(
                        adjustedStrength >= (Math.Min((int)profile.Strength, 60_000) * 0.35) - 1,
                        $"{field.Name} intensity={intensity} motors={motorCount} strength={adjustedStrength}");
                }
            }
        }
    }

    /// <summary>
    /// 單次請求超出剩餘熱預算時，Normal 優先級應降低強度送出並標記 ScaledByThermalOverflow，而不是直接拒絕。
    /// </summary>
    [Fact]
    public void TryApply_Normal_WhenSingleRequestExceedsBudget_ShouldScaleInsteadOfBlock()
    {
        var limiter = new VibrationSafetyLimiter();

        bool accepted = limiter.TryApplyWithDiagnostics(
            60_000,
            150,
            VibrationPriority.Normal,
            nowMs: 1,
            out ushort adjustedStrength,
            out _,
            out VibrationLimiterDebugInfo diagnostics,
            thermalCostMultiplier: VibrationSafetyLimiter.GetMotorThermalCostMultiplier(4));

        Assert.True(accepted);
        Assert.True(adjustedStrength < 60_000);
        Assert.True(diagnostics.Flags.HasFlag(VibrationLimiterFlags.ScaledByThermalOverflow));
        Assert.True(diagnostics.ThermalLoad <= 180.0 * 1.05);
    }

    /// <summary>
    /// 熱負載已超過溢出上限、剩餘預算不足以維持保底強度時，Normal 優先級仍應被拒絕以保護馬達。
    /// </summary>
    [Fact]
    public void TryApply_Normal_WhenAlreadyOverheated_ShouldStillBeBlocked()
    {
        var limiter = new VibrationSafetyLimiter(thermalTauMs: 1_000_000);

        // Critical 不受溢出拒絕限制，用來把熱負載推高到溢出上限之上。
        for (int i = 0; i < 10; i++)
        {
            limiter.TryApply(60_000, 200, VibrationPriority.Critical, nowMs: 1 + i, out _, out _);
        }

        bool accepted = limiter.TryApplyWithDiagnostics(
            60_000,
            200,
            VibrationPriority.Normal,
            nowMs: 20,
            out _,
            out _,
            out VibrationLimiterDebugInfo diagnostics);

        Assert.False(accepted);
        Assert.True(diagnostics.Flags.HasFlag(VibrationLimiterFlags.BlockedByThermalOverflow));
    }
}
