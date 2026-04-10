using InputBox.Core.Configuration;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// AppSettings 關鍵常數驗證測試
/// <para>確保影響安全、A11y 與行為邊界的常數不會因重構而意外改變。</para>
/// </summary>
public class AppSettingsTests
{
    /// <summary>
    /// MaxInputLength 應為 500，對齊 FFXIV 聊天框上限，確保片語內容不超出遊戲限制。
    /// </summary>
    [Fact]
    public void MaxInputLength_Is500()
    {
        Assert.Equal(500, AppSettings.MaxInputLength);
    }

    /// <summary>
    /// MaxPhraseNameLength 應為 50，限制片語名稱長度以確保 UI 顯示空間充足。
    /// </summary>
    [Fact]
    public void MaxPhraseNameLength_Is50()
    {
        Assert.Equal(50, AppSettings.MaxPhraseNameLength);
    }

    /// <summary>
    /// MaxPhraseCount 應為 50，避免片語清單過長影響效能與可用性。
    /// </summary>
    [Fact]
    public void MaxPhraseCount_Is50()
    {
        Assert.Equal(50, AppSettings.MaxPhraseCount);
    }

    /// <summary>
    /// PhotoSafeFrequencyMs 應為 1000ms（1 秒），確保截圖觸發頻率不超過安全上限。
    /// </summary>
    [Fact]
    public void PhotoSafeFrequencyMs_Is1000()
    {
        Assert.Equal(1000, AppSettings.PhotoSafeFrequencyMs);
    }

    /// <summary>
    /// MaxConfigFileSizeBytes 應為 1MB（1,048,576 bytes），防止讀取過大的設定檔導致記憶體問題。
    /// </summary>
    [Fact]
    public void MaxConfigFileSizeBytes_Is1MB()
    {
        Assert.Equal(1 * 1024 * 1024, AppSettings.MaxConfigFileSizeBytes);
    }

    /// <summary>
    /// TaskbarFlashSafeCount 應為 3，限制工作列閃爍次數以避免對光敏感使用者造成不適（A11y）。
    /// </summary>
    [Fact]
    public void TaskbarFlashSafeCount_Is3()
    {
        Assert.Equal(3u, AppSettings.TaskbarFlashSafeCount);
    }

    /// <summary>
    /// TargetFrameTimeMs 應約為 16.6ms（≈60fps），確保遊戲控制器輪詢以目標幀率執行。
    /// </summary>
    [Fact]
    public void TargetFrameTimeMs_IsApproximately16_6()
    {
        Assert.Equal(16.6, AppSettings.TargetFrameTimeMs, precision: 1);
    }
}
