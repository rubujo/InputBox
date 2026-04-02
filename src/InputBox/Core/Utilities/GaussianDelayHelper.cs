namespace InputBox.Core.Utilities;

/// <summary>
/// 提供基於高斯分佈（常態分佈）的隨機數產生器
/// 用於系統操作的自然緩衝延遲，改善 A11y 音訊避讓與 UI 執行緒排程穩定性
/// </summary>
internal static class GaussianDelayHelper
{
    /// <summary>
    /// 隨機數生成器實例
    /// </summary>
    private static readonly Random _rng = new();

    /// <summary>
    /// 產生符合高斯分佈的隨機值
    /// </summary>
    /// <param name="mean">期望的平均值（μ）</param>
    /// <param name="standardDeviation">標準差（σ），代表波動幅度。建議設為平均值的 15%~20%</param>
    /// <returns>產生的隨機值</returns>
    public static double NextGaussian(
        double mean,
        double standardDeviation)
    {
        // 使用 Box-Muller 轉換。
        // 確保不為 0。
        double u1 = 1.0 - _rng.NextDouble(),
            u2 = 1.0 - _rng.NextDouble(),
            randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2),
            // 映射到目標分佈。
            result = mean + standardDeviation * randStdNormal;

        // 強制邊界檢查：確保結果不會小於平均值的 50% 或大於 150%，防止極端值破壞交互體驗。
        return Math.Clamp(result, mean * 0.5, mean * 1.5);
    }

    /// <summary>
    /// 產生符合人類反應特徵的毫秒延遲
    /// </summary>
    /// <param name="baseDelay">基礎延遲（毫秒）</param>
    /// <param name="jitterRange">抖動範圍（毫秒）</param>
    /// <returns>加上生理擾動後的延遲時間</returns>
    public static int NextDelay(
        int baseDelay,
        int jitterRange)
    {
        // 若沒有抖動範圍，直接返回基礎值。
        if (jitterRange <= 0)
        {
            return baseDelay;
        }

        // 映射邏輯：
        // 1. 平均值 (μ) 設在原本均勻分佈的中點。
        // 2. 標準差 (σ) 設為範圍的 1/6，確保 99.7% 的值落在 [base, base + jitter] 內。
        double mean = baseDelay + (jitterRange / 2.0),
            sd = jitterRange / 6.0;

        // 若 jitter 過小導致 sd 太低，補上一個極小生理抖動（平均值的 5%）。
        sd = Math.Max(sd, mean * 0.05);

        double result = NextGaussian(mean, sd);

        // 嚴格邊界檢查：延遲不得低於 baseDelay，也不應無限膨脹。
        return (int)Math.Round(Math.Clamp(result, baseDelay, baseDelay + jitterRange * 1.5));
    }
}