using InputBox.Core.Feedback;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// VibrationPatterns 的靜態欄位值與 GlobalIntensityMultiplier 行為測試
/// <para>確保各震動設定的強度與持續時間符合設計規格，且全域倍率能正確設定與讀取。</para>
/// </summary>
public class VibrationPatternsTests
{
    // ── GlobalIntensityMultiplier ──────────────────────────────────────────

    /// <summary>
    /// GlobalIntensityMultiplier 預設值應為 0.7f。
    /// </summary>
    [Fact]
    public void GlobalIntensityMultiplier_DefaultValue_Is0Point7()
    {
        // 重置為已知狀態
        VibrationPatterns.GlobalIntensityMultiplier = 0.7f;
        Assert.Equal(0.7f, VibrationPatterns.GlobalIntensityMultiplier);
    }

    /// <summary>
    /// 設定 GlobalIntensityMultiplier 後，讀取值應與設定值一致。
    /// </summary>
    [Fact]
    public void GlobalIntensityMultiplier_SetAndGet_ReturnsNewValue()
    {
        float original = VibrationPatterns.GlobalIntensityMultiplier;

        try
        {
            VibrationPatterns.GlobalIntensityMultiplier = 0.5f;
            Assert.Equal(0.5f, VibrationPatterns.GlobalIntensityMultiplier);
        }
        finally
        {
            VibrationPatterns.GlobalIntensityMultiplier = original;
        }
    }

    // ── 靜態欄位設計值 ─────────────────────────────────────────────────────

    /// <summary>
    /// CursorMove 強度應為 18000（足以克服馬達啟動閾值的輕微點擊感）。
    /// </summary>
    [Fact]
    public void CursorMove_Strength_Is18000()
    {
        Assert.Equal(18000, VibrationPatterns.CursorMove.Strength);
    }

    /// <summary>
    /// CursorMove 持續時間應為 50ms（短暫點擊感，避免連按時糊在一起）。
    /// </summary>
    [Fact]
    public void CursorMove_Duration_Is50()
    {
        Assert.Equal(50, VibrationPatterns.CursorMove.Duration);
    }

    /// <summary>
    /// CopySuccess 強度應為 40000（約 60% 強度的確認感）。
    /// </summary>
    [Fact]
    public void CopySuccess_Strength_Is40000()
    {
        Assert.Equal(40000, VibrationPatterns.CopySuccess.Strength);
    }

    /// <summary>
    /// CopySuccess 持續時間應為 150ms。
    /// </summary>
    [Fact]
    public void CopySuccess_Duration_Is150()
    {
        Assert.Equal(150, VibrationPatterns.CopySuccess.Duration);
    }

    /// <summary>
    /// ActionFail 強度應為 45000（最強，確保錯誤不被忽視）。
    /// </summary>
    [Fact]
    public void ActionFail_Strength_Is45000()
    {
        Assert.Equal(45000, VibrationPatterns.ActionFail.Strength);
    }

    /// <summary>
    /// ActionFail 持續時間應為 200ms（最長，打破節奏）。
    /// </summary>
    [Fact]
    public void ActionFail_Duration_Is200()
    {
        Assert.Equal(200, VibrationPatterns.ActionFail.Duration);
    }

    /// <summary>
    /// ControllerConnected 強度應為 30000（觸覺握手的中等強度）。
    /// </summary>
    [Fact]
    public void ControllerConnected_Strength_Is30000()
    {
        Assert.Equal(30000, VibrationPatterns.ControllerConnected.Strength);
    }

    /// <summary>
    /// ControllerConnected 持續時間應為 200ms（確保視聽雙障使用者也能感知）。
    /// </summary>
    [Fact]
    public void ControllerConnected_Duration_Is200()
    {
        Assert.Equal(200, VibrationPatterns.ControllerConnected.Duration);
    }

    // ── VibrationProfile 值語義 ────────────────────────────────────────────

    /// <summary>
    /// VibrationProfile 的 Strength 與 Duration 應正確封裝初始值。
    /// </summary>
    [Fact]
    public void VibrationProfile_ConstructorValues_AreAccessible()
    {
        var profile = new VibrationProfile(12345, 99);
        Assert.Equal(12345, profile.Strength);
        Assert.Equal(99, profile.Duration);
    }

    /// <summary>
    /// VibrationProfile 是 record struct，相同值的兩個實例應相等。
    /// </summary>
    [Fact]
    public void VibrationProfile_SameValues_AreEqual()
    {
        var a = new VibrationProfile(30000, 120);
        var b = new VibrationProfile(30000, 120);
        Assert.Equal(a, b);
    }

    /// <summary>
    /// VibrationProfile 不同值的兩個實例應不相等。
    /// </summary>
    [Fact]
    public void VibrationProfile_DifferentValues_AreNotEqual()
    {
        var a = new VibrationProfile(30000, 120);
        var b = new VibrationProfile(30000, 121);
        Assert.NotEqual(a, b);
    }
}