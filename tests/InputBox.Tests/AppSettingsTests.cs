using InputBox.Core.Configuration;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// AppSettings 關鍵常數驗證、屬性夾緊與 GamepadConfigSnapshot 快照邏輯測試
/// <para>確保影響安全、A11y 與行為邊界的常數及夾緊規則不會因重構而意外改變。</para>
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    /// <summary>
    /// 測試期間使用的設定檔路徑。
    /// </summary>
    private static readonly string ConfigPath = Path.Combine(
        AppSettings.ConfigDirectory, "appsettings.json");

    /// <summary>
    /// 備份原始設定檔的暫存路徑，用於測試結束後還原使用者資料。
    /// </summary>
    private static readonly string BackupPath = ConfigPath + ".testbackup";

    /// <summary>
    /// 建構子：若設定檔存在則先備份，確保測試不污染使用者原始資料。
    /// </summary>
    public AppSettingsTests()
    {
        Directory.CreateDirectory(AppSettings.ConfigDirectory);

        if (File.Exists(ConfigPath))
        {
            File.Copy(ConfigPath, BackupPath, overwrite: true);
        }
    }

    /// <summary>
    /// 還原測試期間的設定檔變更，並清除殘留的暫存檔。
    /// </summary>
    public void Dispose()
    {
        foreach (string tempFile in Directory.GetFiles(AppSettings.ConfigDirectory, "appsettings.json*.tmp"))
        {
            File.Delete(tempFile);
        }

        if (File.Exists(BackupPath))
        {
            File.Move(BackupPath, ConfigPath, overwrite: true);
        }
        else if (File.Exists(ConfigPath))
        {
            File.Delete(ConfigPath);
        }
    }

    // ── 整數屬性夾緊 ────────────────────────────────────────────────────────

    /// <summary>
    /// WindowRestoreDelay 低於下限 0 時應夾緊至 0。
    /// </summary>
    [Fact]
    public void WindowRestoreDelay_BelowMin_ClampsToZero()
    {
        var s = new AppSettings { WindowRestoreDelay = -1 };
        Assert.Equal(0, s.WindowRestoreDelay);
    }

    /// <summary>
    /// WindowRestoreDelay 超過上限 5000 時應夾緊至 5000。
    /// </summary>
    [Fact]
    public void WindowRestoreDelay_AboveMax_ClampsTo5000()
    {
        var s = new AppSettings { WindowRestoreDelay = 99999 };
        Assert.Equal(5000, s.WindowRestoreDelay);
    }

    /// <summary>
    /// HistoryCapacity 最小值為 1（不允許 0）。
    /// </summary>
    [Fact]
    public void HistoryCapacity_Zero_ClampsToOne()
    {
        var s = new AppSettings { HistoryCapacity = 0 };
        Assert.Equal(1, s.HistoryCapacity);
    }

    /// <summary>
    /// HistoryCapacity 上限為 1000。
    /// </summary>
    [Fact]
    public void HistoryCapacity_AboveMax_ClampsTo1000()
    {
        var s = new AppSettings { HistoryCapacity = 5000 };
        Assert.Equal(1000, s.HistoryCapacity);
    }

    /// <summary>
    /// ClipboardRetryDelay 下限為 0，上限為 1000。
    /// </summary>
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(1000, 1000)]
    [InlineData(9999, 1000)]
    public void ClipboardRetryDelay_ClampsCorrectly(int input, int expected)
    {
        var s = new AppSettings { ClipboardRetryDelay = input };
        Assert.Equal(expected, s.ClipboardRetryDelay);
    }

    /// <summary>
    /// TouchKeyboardDismissDelay 下限為 0，上限為 5000。
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(300, 300)]
    [InlineData(5001, 5000)]
    public void TouchKeyboardDismissDelay_ClampsCorrectly(int input, int expected)
    {
        var s = new AppSettings { TouchKeyboardDismissDelay = input };
        Assert.Equal(expected, s.TouchKeyboardDismissDelay);
    }

    // ── 浮點屬性夾緊 ────────────────────────────────────────────────────────

    /// <summary>
    /// WindowOpacity 低於 0.1f 時應夾緊至 0.1f（防止視窗完全隱形）。
    /// </summary>
    [Fact]
    public void WindowOpacity_BelowMin_ClampsTo01()
    {
        var s = new AppSettings { WindowOpacity = 0.0f };
        Assert.Equal(0.1f, s.WindowOpacity, precision: 5);
    }

    /// <summary>
    /// WindowOpacity 超過 1.0f 時應夾緊至 1.0f。
    /// </summary>
    [Fact]
    public void WindowOpacity_AboveMax_ClampsTo10()
    {
        var s = new AppSettings { WindowOpacity = 1.5f };
        Assert.Equal(1.0f, s.WindowOpacity, precision: 5);
    }

    /// <summary>
    /// VibrationIntensity 下限為 0.0f，上限為 1.0f。
    /// </summary>
    [Theory]
    [InlineData(-0.1f, 0.0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1.1f, 1.0f)]
    public void VibrationIntensity_ClampsCorrectly(float input, float expected)
    {
        var s = new AppSettings { VibrationIntensity = input };
        Assert.Equal(expected, s.VibrationIntensity, precision: 5);
    }

    // ── HotKeyKey null 守衛 ──────────────────────────────────────────────────

    /// <summary>
    /// HotKeyKey 設為 null 時應回退至預設值 "I"。
    /// </summary>
    [Fact]
    public void HotKeyKey_SetNull_FallsBackToDefaultI()
    {
        var s = new AppSettings { HotKeyKey = null! };
        Assert.Equal("I", s.HotKeyKey);
    }

    /// <summary>
    /// HotKeyKey 設為有效字串時應儲存原值。
    /// </summary>
    [Fact]
    public void HotKeyKey_ValidString_StoredAsIs()
    {
        var s = new AppSettings { HotKeyKey = "F12" };
        Assert.Equal("F12", s.HotKeyKey);
    }

    // ── GamepadConfigSnapshot 死區遲滯計算 ─────────────────────────────────

    /// <summary>
    /// 設定 ThumbDeadzoneEnter 後，快照中 ThumbDeadzoneEnter 應同步更新。
    /// </summary>
    [Fact]
    public void ThumbDeadzoneEnter_Set_SnapshotReflectsNewValue()
    {
        var s = new AppSettings { ThumbDeadzoneEnter = 12000 };
        Assert.Equal(12000, s.GamepadSettings.ThumbDeadzoneEnter);
    }

    /// <summary>
    /// Exit 閾值過高（超過 Enter - margin）時應自動下調，以確保遲滯緩衝空間。
    /// <para>Enter=10000, margin=Max(2000,3000)=3000 → Exit 上限為 7000。</para>
    /// </summary>
    [Fact]
    public void ThumbDeadzoneExit_TooHigh_IsAdjustedByHysteresis()
    {
        var s = new AppSettings
        {
            ThumbDeadzoneEnter = 10000,
            ThumbDeadzoneExit = 9000  // 9000 > 7000 → 下調至 7000
        };
        Assert.Equal(7000, s.GamepadSettings.ThumbDeadzoneExit);
    }

    /// <summary>
    /// Exit 閾值在緩衝空間內（低於 Enter - margin）時應保留原值。
    /// </summary>
    [Fact]
    public void ThumbDeadzoneExit_WithinHysteresis_KeptAsIs()
    {
        var s = new AppSettings
        {
            ThumbDeadzoneEnter = 10000,
            ThumbDeadzoneExit = 5000  // 5000 < 7000 → 維持
        };
        Assert.Equal(5000, s.GamepadSettings.ThumbDeadzoneExit);
    }

    /// <summary>
    /// Enter 值較小時，margin 使用最小值 2000，Exit 仍被正確下調。
    /// <para>Enter=3000, margin=Max(2000,900)=2000 → Exit 上限為 1000。</para>
    /// </summary>
    [Fact]
    public void ThumbDeadzoneExit_SmallEnter_UsesMinimumMargin()
    {
        var s = new AppSettings
        {
            ThumbDeadzoneEnter = 3000,
            ThumbDeadzoneExit = 2500  // 2500 > 1000 → 下調至 1000
        };
        Assert.Equal(1000, s.GamepadSettings.ThumbDeadzoneExit);
    }

    /// <summary>
    /// 使用者先調整 Exit，再降低 Enter 時，快照應重新夾緊 Exit 以維持遲滯安全區間。
    /// </summary>
    [Fact]
    public void ThumbDeadzoneEnter_LoweredAfterExitWasSet_ReclampsExit()
    {
        var s = new AppSettings
        {
            ThumbDeadzoneEnter = 12000,
            ThumbDeadzoneExit = 8000
        };

        Assert.Equal(8000, s.GamepadSettings.ThumbDeadzoneExit);

        s.ThumbDeadzoneEnter = 6000;

        Assert.Equal(4000, s.GamepadSettings.ThumbDeadzoneExit);
    }

    /// <summary>
    /// 模擬使用者在右鍵選單依序調整遊戲控制器數值時，快照應維持一致且保留所有設定。
    /// </summary>
    [Fact]
    public void GamepadSettings_SequentialMenuStyleUpdates_KeepSnapshotConsistent()
    {
        var s = new AppSettings
        {
            ThumbDeadzoneEnter = 11000,
            ThumbDeadzoneExit = 7000,
            RepeatInitialDelayFrames = 45,
            RepeatIntervalFrames = 6
        };

        AppSettings.GamepadConfigSnapshot snap = s.GamepadSettings;

        Assert.Equal(11000, snap.ThumbDeadzoneEnter);
        Assert.Equal(7000, snap.ThumbDeadzoneExit);
        Assert.Equal(45, snap.RepeatInitialDelayFrames);
        Assert.Equal(6, snap.RepeatIntervalFrames);
    }

    /// <summary>
    /// RepeatInitialDelayFrames 下限為 1，上限為 300，快照同步更新。
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(30, 30)]
    [InlineData(999, 300)]
    public void RepeatInitialDelayFrames_ClampsAndUpdatesSnapshot(int input, int expected)
    {
        var s = new AppSettings { RepeatInitialDelayFrames = input };
        Assert.Equal(expected, s.GamepadSettings.RepeatInitialDelayFrames);
    }

    /// <summary>
    /// RepeatIntervalFrames 下限為 1，上限為 100，快照同步更新。
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(200, 100)]
    public void RepeatIntervalFrames_ClampsAndUpdatesSnapshot(int input, int expected)
    {
        var s = new AppSettings { RepeatIntervalFrames = input };
        Assert.Equal(expected, s.GamepadSettings.RepeatIntervalFrames);
    }

    // ── GamepadConfigSnapshot record ────────────────────────────────────────

    /// <summary>
    /// GamepadConfigSnapshot record 各屬性應如實反映建構時傳入的值。
    /// </summary>
    [Fact]
    public void GamepadConfigSnapshot_Properties_MatchConstructorArgs()
    {
        var snap = new AppSettings.GamepadConfigSnapshot(7849, 2500, 30, 5);
        Assert.Equal(7849, snap.ThumbDeadzoneEnter);
        Assert.Equal(2500, snap.ThumbDeadzoneExit);
        Assert.Equal(30, snap.RepeatInitialDelayFrames);
        Assert.Equal(5, snap.RepeatIntervalFrames);
    }

    // ── WindowSwitchBufferBase 夾緊 ─────────────────────────────────────────

    /// <summary>
    /// WindowSwitchBufferBase 下限為 0，上限為 5000。
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(150, 150)]
    [InlineData(9999, 5000)]
    public void WindowSwitchBufferBase_ClampsCorrectly(int input, int expected)
    {
        var s = new AppSettings { WindowSwitchBufferBase = input };
        Assert.Equal(expected, s.WindowSwitchBufferBase);
    }

    // ── ThumbDeadzoneEnter 夾緊 ──────────────────────────────────────────────

    /// <summary>
    /// ThumbDeadzoneEnter 下限為 0，上限為 30000。
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(7849, 7849)]
    [InlineData(30001, 30000)]
    public void ThumbDeadzoneEnter_ClampsToRange(int input, int expected)
    {
        var s = new AppSettings { ThumbDeadzoneEnter = input };
        Assert.Equal(expected, s.GamepadSettings.ThumbDeadzoneEnter);
    }

    // ── 預設值驗證 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 新建 AppSettings 實例時，關鍵屬性應符合設計預設值。
    /// </summary>
    [Fact]
    public void NewInstance_HasExpectedDefaults()
    {
        var s = new AppSettings();
        Assert.Equal(50, s.WindowRestoreDelay);
        Assert.Equal(20, s.ClipboardRetryDelay);
        Assert.Equal(300, s.TouchKeyboardDismissDelay);
        Assert.Equal(150, s.WindowSwitchBufferBase);
        Assert.Equal(100, s.HistoryCapacity);
        Assert.Equal(1.0f, s.WindowOpacity, precision: 5);
        Assert.Equal(0.7f, s.VibrationIntensity, precision: 5);
        Assert.Equal("I", s.HotKeyKey);
        Assert.True(s.A11yInterruptEnabled);
        Assert.False(s.EnableAnimatedVisualAlerts);
    }

    /// <summary>
    /// 新建 AppSettings 實例時，GamepadConfigSnapshot 預設值應一致。
    /// </summary>
    [Fact]
    public void NewInstance_GamepadSnapshotHasExpectedDefaults()
    {
        var s = new AppSettings();
        Assert.Equal(7849, s.GamepadSettings.ThumbDeadzoneEnter);
        Assert.Equal(30, s.GamepadSettings.RepeatInitialDelayFrames);
        Assert.Equal(5, s.GamepadSettings.RepeatIntervalFrames);
    }

    /// <summary>
    /// 併發寫入設定檔時，應保留有效 JSON，且不殘留任何暫存檔。
    /// </summary>
    [Fact]
    public async Task WriteConfigToFile_ConcurrentWrites_LeavesValidJsonAndNoTempFiles()
    {
        MethodInfo writeMethod = typeof(AppSettings).GetMethod(
            "WriteConfigToFile",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("找不到 WriteConfigToFile 方法。");

        Task[] tasks = [..
            Enumerable.Range(0, 8)
                .Select(i => Task.Run(
                    () => writeMethod.Invoke(null, [$"{{\"HotKeyKey\":\"F{i}\"}}"]),
                    TestContext.Current.CancellationToken))];

        await Task.WhenAll(tasks);

        Assert.True(File.Exists(ConfigPath));

        string json = await File.ReadAllTextAsync(ConfigPath, TestContext.Current.CancellationToken);
        using JsonDocument _ = JsonDocument.Parse(json);

        string[] tempFiles = Directory.GetFiles(AppSettings.ConfigDirectory, "appsettings.json*.tmp");
        Assert.Empty(tempFiles);
    }

    /// <summary>
    /// 其他尚在進行中的新鮮暫存檔不應被目前這次儲存流程誤刪，避免併發寫入時後續 Move/Replace 因找不到檔案而失敗。
    /// </summary>
    [Fact]
    public void Save_WithFreshSiblingTempFile_PreservesPendingConcurrentWrite()
    {
        string pendingTempPath = Path.Combine(AppSettings.ConfigDirectory, $"appsettings.json.pending-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(pendingTempPath, "pending-temp", System.Text.Encoding.UTF8);

        try
        {
            AppSettings.Current.HotKeyKey = "F11";
            AppSettings.Save();

            Assert.True(File.Exists(pendingTempPath));
        }
        finally
        {
            if (File.Exists(pendingTempPath))
            {
                File.Delete(pendingTempPath);
            }
        }
    }

    /// <summary>
    /// 既有的殘留暫存檔應在下次成功儲存後被清理，避免設定目錄長期累積垃圾檔案。
    /// </summary>
    [Fact]
    public void Save_WithStaleTempFile_CleansUpOrphanedTempFiles()
    {
        string staleTempPath = Path.Combine(AppSettings.ConfigDirectory, "appsettings.json.stale-test.tmp");
        File.WriteAllText(staleTempPath, "stale-temp", System.Text.Encoding.UTF8);
        File.SetLastWriteTimeUtc(staleTempPath, DateTime.UtcNow.AddMinutes(-10));

        AppSettings.Current.HotKeyKey = "F10";
        AppSettings.Save();

        string[] tempFiles = Directory.GetFiles(AppSettings.ConfigDirectory, "appsettings.json*.tmp");
        Assert.Empty(tempFiles);
    }

    // ── 常數值驗證 ──────────────────────────────────────────────────────────

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