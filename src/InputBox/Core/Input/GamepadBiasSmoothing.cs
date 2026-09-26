namespace InputBox.Core.Input;

/// <summary>
/// XInput 與 GameInput 共用的自適應 EMA 搖桿偏移補償係數與學習率公式
/// </summary>
/// <remarks>
/// <para>每個軸保有獨立的「基礎值」與「最大值」：估計誤差（原始值 − 目前偏移）落在誤差範圍內時，
/// 學習率由基礎值線性插值至最大值，誤差越大收斂越快；誤差接近 0 時退回基礎值，避免把有效輸入誤學成硬體偏移。</para>
/// <para>兩個後端的輸入數值尺度不同（XInput 為 short、GameInput 為 -1～1 浮點數），
/// 因此誤差範圍由各後端自行提供；平滑係數則必須完全一致，集中於此避免兩邊各自漂移。</para>
/// </remarks>
internal static class GamepadBiasSmoothing
{
    /// <summary>
    /// 左搖桿 X 軸：偏移估計的最低保守學習率（誤差接近 0 時使用）。
    /// </summary>
    public const float LeftStickBiasXBaseSmoothing = 0.03f;

    /// <summary>
    /// 左搖桿 X 軸：偏移估計的最高學習率（誤差達到誤差範圍時使用）。
    /// </summary>
    public const float LeftStickBiasXMaxSmoothing = 0.15f;

    /// <summary>
    /// 左搖桿 Y 軸：偏移估計的最低保守學習率。
    /// </summary>
    public const float LeftStickBiasYBaseSmoothing = 0.03f;

    /// <summary>
    /// 左搖桿 Y 軸：偏移估計的最高學習率。
    /// </summary>
    public const float LeftStickBiasYMaxSmoothing = 0.12f;

    /// <summary>
    /// 右搖桿 X 軸：偏移估計的最低保守學習率。
    /// </summary>
    public const float RightStickBiasBaseSmoothing = 0.05f;

    /// <summary>
    /// 右搖桿 X 軸：偏移估計的最高學習率。
    /// 右搖桿無 D-Pad 閘門，低最大值可防止快速劃過中立區時累積偏移。
    /// </summary>
    public const float RightStickBiasMaxSmoothing = 0.07f;

    /// <summary>
    /// 右搖桿 Y 軸：偏移估計的最低保守學習率。
    /// </summary>
    public const float RightStickBiasYBaseSmoothing = 0.05f;

    /// <summary>
    /// 右搖桿 Y 軸：偏移估計的最高學習率。
    /// </summary>
    public const float RightStickBiasYMaxSmoothing = 0.07f;

    /// <summary>
    /// 依偏移誤差計算自適應學習率：α = base + (max − base) × clamp(|error| / errorRange, 0, 1)。
    /// </summary>
    /// <param name="error">目前估計誤差（與 <paramref name="errorRange"/> 使用相同尺度）。</param>
    /// <param name="errorRange">觸發最大學習率的誤差範圍；必須大於 0。</param>
    /// <param name="baseSmoothing">誤差接近 0 時使用的最低學習率。</param>
    /// <param name="maxSmoothing">誤差達到範圍上限時使用的最高學習率。</param>
    /// <returns>本幀使用的學習率。</returns>
    public static float ComputeAdaptiveSmoothing(
        float error,
        float errorRange,
        float baseSmoothing,
        float maxSmoothing)
    {
        float t = Math.Clamp(MathF.Abs(error) / errorRange, 0f, 1f);

        return baseSmoothing + (maxSmoothing - baseSmoothing) * t;
    }
}
