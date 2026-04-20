namespace InputBox.Core.Feedback;

/// <summary>
/// 震動設定結構。
/// <para>除了基礎強度與持續時間外，也可描述各顆馬達的相對比例，供 XInput / GameInput 做方向性回饋。</para>
/// </summary>
/// <param name="Strength">基礎強度（0~65535），會再受全域震動倍率調整。</param>
/// <param name="Duration">持續時間（毫秒）。</param>
/// <param name="LowFrequencyMotorScale">左側／低頻主馬達比例（0.0~1.0）。</param>
/// <param name="HighFrequencyMotorScale">右側／高頻主馬達比例（0.0~1.0）。</param>
/// <param name="LeftTriggerMotorScale">左板機馬達比例（0.0~1.0）。</param>
/// <param name="RightTriggerMotorScale">右板機馬達比例（0.0~1.0）。</param>
public readonly record struct VibrationProfile(
    ushort Strength,
    int Duration,
    float LowFrequencyMotorScale = 1.0f,
    float HighFrequencyMotorScale = 1.0f,
    float LeftTriggerMotorScale = 1.0f,
    float RightTriggerMotorScale = 1.0f)
{
    /// <summary>
    /// 套用全域震動倍率後，回傳新的設定檔副本。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 內部以 gamma = 0.55 做冪次曲線修正，使感知強度在低倍率區間（0.1–0.4）仍維持可感知，
    /// 避免線性映射在小數值時因馬達物理閾值而完全無感。
    /// 0.0 → 0（靜音），1.0 → 1（維持上限），中間值均往上提。
    /// </para>
    /// </remarks>
    /// <param name="multiplier">全域倍率（0.0 ~ 1.0）。</param>
    /// <returns>已套用曲線倍率的新設定。</returns>
    public VibrationProfile ApplyIntensityMultiplier(float multiplier)
    {
        float clampedMultiplier = Math.Max(0f, multiplier);
        float effectiveMultiplier = clampedMultiplier > 0f
            ? MathF.Pow(clampedMultiplier, 0.55f)
            : 0f;
        ushort scaledStrength = (ushort)Math.Clamp(
            Strength * effectiveMultiplier,
            ushort.MinValue,
            ushort.MaxValue);

        return this with { Strength = scaledStrength };
    }

    /// <summary>
    /// 取得所有馬達比例中對應的峰值強度，供安全限制器做保守保護。
    /// </summary>
    /// <returns>最大馬達強度。</returns>
    public ushort GetPeakMotorStrength()
    {
        float peakScale = Math.Max(
            Math.Max(ClampMotorScale(LowFrequencyMotorScale), ClampMotorScale(HighFrequencyMotorScale)),
            Math.Max(ClampMotorScale(LeftTriggerMotorScale), ClampMotorScale(RightTriggerMotorScale)));

        return (ushort)Math.Clamp(
            Strength * peakScale,
            ushort.MinValue,
            ushort.MaxValue);
    }

    /// <summary>
    /// 將單一馬達比例限制在安全範圍內。
    /// </summary>
    /// <param name="scale">原始比例。</param>
    /// <returns>0.0~1.0 間的安全比例。</returns>
    public static float ClampMotorScale(float scale) => Math.Clamp(scale, 0.0f, 1.0f);
}

/// <summary>
/// 控制器可用的震動馬達能力描述。
/// </summary>
[Flags]
internal enum VibrationMotorSupport
{
    None = 0,
    DualMain = 1 << 0,
    TriggerMotors = 1 << 1
}

/// <summary>
/// 多段式震動序列中的單一步驟。
/// </summary>
/// <param name="Profile">要播放的震動設定。</param>
/// <param name="PauseAfterMs">本步完成後額外等待的毫秒數。</param>
internal readonly record struct VibrationSequenceStep(
    VibrationProfile Profile,
    int PauseAfterMs = 0);