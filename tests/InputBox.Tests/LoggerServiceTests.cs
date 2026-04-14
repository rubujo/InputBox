using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// LoggerService 測試，確保測試環境的日誌不會污染正式執行記錄。
/// </summary>
public sealed class LoggerServiceTests : IDisposable
{
    /// <summary>
    /// 正式執行時的主要日誌檔路徑，用於驗證測試不會污染使用者平常檢視的記錄。
    /// </summary>
    private static readonly string MainLogPath = Path.Combine(LoggerService.LogDirectory, "InputBox.log");

    /// <summary>
    /// 測試執行時的專屬日誌檔路徑，供回歸測試驗證測試例外是否被正確分流。
    /// </summary>
    private static readonly string TestLogPath = Path.Combine(LoggerService.LogDirectory, "InputBox.test.log");

    /// <summary>
    /// 正式日誌的測試暫存備份路徑，確保測試後能完整還原使用者原始資料。
    /// </summary>
    private static readonly string MainLogBackupPath = MainLogPath + ".testbackup";

    /// <summary>
    /// 測試日誌的測試暫存備份路徑，避免測試互相污染既有記錄。
    /// </summary>
    private static readonly string TestLogBackupPath = TestLogPath + ".testbackup";

    /// <summary>
    /// 建構子：先備份既有正式／測試日誌，避免測試污染使用者現有記錄。
    /// </summary>
    public LoggerServiceTests()
    {
        Directory.CreateDirectory(LoggerService.LogDirectory);

        BackupIfExists(MainLogPath, MainLogBackupPath);
        BackupIfExists(TestLogPath, TestLogBackupPath);
    }

    /// <summary>
    /// 還原測試前的日誌狀態。
    /// </summary>
    public void Dispose()
    {
        RestoreOrDelete(MainLogPath, MainLogBackupPath);
        RestoreOrDelete(TestLogPath, TestLogBackupPath);
    }

    /// <summary>
    /// 在測試執行環境下記錄例外時，應寫入專屬的測試日誌檔，而不是污染正式的 InputBox.log。
    /// </summary>
    [Fact]
    public void LogException_UnderTestHost_WritesToDedicatedTestLogFile()
    {
        if (File.Exists(MainLogPath))
        {
            File.Delete(MainLogPath);
        }

        if (File.Exists(TestLogPath))
        {
            File.Delete(TestLogPath);
        }

        // 使用可辨識的 marker 例外訊息，確認內容確實寫入測試專屬日誌。
        InvalidOperationException exception = new("logger-test-marker");

        LoggerService.LogException(exception, "LoggerServiceTests");

        Assert.True(File.Exists(TestLogPath));
        Assert.False(File.Exists(MainLogPath));

        string content = File.ReadAllText(TestLogPath);
        Assert.Contains("logger-test-marker", content);
        Assert.Contains("LoggerServiceTests", content);
    }

    /// <summary>
    /// 若目標檔案存在則先備份到測試專用路徑。
    /// </summary>
    /// <param name="sourcePath">原始日誌檔路徑。</param>
    /// <param name="backupPath">測試期間使用的備份檔路徑。</param>
    private static void BackupIfExists(string sourcePath, string backupPath)
    {
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, backupPath, overwrite: true);
        }
    }

    /// <summary>
    /// 若有備份則還原，否則刪除測試產生的檔案。
    /// </summary>
    /// <param name="targetPath">要還原或刪除的目標檔案路徑。</param>
    /// <param name="backupPath">測試前保存的備份檔路徑。</param>
    private static void RestoreOrDelete(string targetPath, string backupPath)
    {
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        if (File.Exists(backupPath))
        {
            File.Move(backupPath, targetPath, overwrite: true);
        }
    }
}