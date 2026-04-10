using InputBox.Core.Utilities;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// GaussianDelayHelper 的延遲邊界與分佈行為測試
/// </summary>
public class GaussianDelayHelperTests
{
    // ── NextDelay ──────────────────────────────────────────────────────────

    /// <summary>
    /// jitterRange 為 0 時，應直接回傳 baseDelay，不套用高斯分佈。
    /// </summary>
    [Fact]
    public void NextDelay_ZeroJitter_ReturnsBaseDelay()
    {
        int result = GaussianDelayHelper.NextDelay(100, 0);
        Assert.Equal(100, result);
    }

    /// <summary>
    /// jitterRange 為負數時，應直接回傳 baseDelay。
    /// </summary>
    [Fact]
    public void NextDelay_NegativeJitter_ReturnsBaseDelay()
    {
        int result = GaussianDelayHelper.NextDelay(200, -50);
        Assert.Equal(200, result);
    }

    /// <summary>
    /// 有效的 jitter 範圍下，結果應不低於 baseDelay。
    /// 多次執行以驗證邊界保證。
    /// </summary>
    [Fact]
    public void NextDelay_WithJitter_NeverBelowBaseDelay()
    {
        const int baseDelay = 50;
        const int jitter = 30;

        for (int i = 0; i < 500; i++)
        {
            int result = GaussianDelayHelper.NextDelay(baseDelay, jitter);
            Assert.True(result >= baseDelay,
                $"第 {i + 1} 次迭代：{result} < {baseDelay}");
        }
    }

    /// <summary>
    /// 有效的 jitter 範圍下，結果應不超過 baseDelay + jitter * 1.5 的上限。
    /// </summary>
    [Fact]
    public void NextDelay_WithJitter_NeverExceedsUpperBound()
    {
        const int baseDelay = 50;
        const int jitter = 30;
        int upperBound = (int)Math.Round(baseDelay + jitter * 1.5);

        for (int i = 0; i < 500; i++)
        {
            int result = GaussianDelayHelper.NextDelay(baseDelay, jitter);
            Assert.True(result <= upperBound,
                $"第 {i + 1} 次迭代：{result} > {upperBound}");
        }
    }

    // ── NextGaussian ───────────────────────────────────────────────────────

    /// <summary>
    /// NextGaussian 的回傳值應始終位於 [mean * 0.5, mean * 1.5] 的 Clamp 範圍內。
    /// </summary>
    [Fact]
    public void NextGaussian_AlwaysWithinClampBounds()
    {
        const double mean = 100.0;
        const double sd = 20.0;

        for (int i = 0; i < 500; i++)
        {
            double result = GaussianDelayHelper.NextGaussian(mean, sd);
            Assert.True(result >= mean * 0.5 && result <= mean * 1.5,
                $"第 {i + 1} 次迭代：{result} 超出 [{mean * 0.5}, {mean * 1.5}]");
        }
    }
}
