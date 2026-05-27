using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// LoggerService 測試專用 Collection 名稱，並強制關閉平行執行，避免與其他測試共享日誌檔時互相干擾。
/// </summary>
internal static class LoggerServiceTestRequirements
{
    /// <summary>
    /// LoggerService 測試專用 Collection 名稱。
    /// </summary>
    public const string CollectionName = "Logger Service";
}

/// <summary>
/// 禁止 LoggerService 測試與其他測試平行執行，降低共用日誌檔案被背景工作暫時占用的機率。
/// </summary>
[CollectionDefinition(LoggerServiceTestRequirements.CollectionName, DisableParallelization = true)]
public sealed class LoggerServiceTestCollection;

/// <summary>
/// LoggerService 測試，確保測試環境的日誌不會污染正式執行記錄。
/// </summary>
[Collection(LoggerServiceTestRequirements.CollectionName)]
public sealed class LoggerServiceTests : IDisposable
{
    private readonly List<string> _temporaryLogPaths = [];
    private readonly string? _originalLogFileName;
    private readonly string? _originalLogLevel;

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
        _originalLogFileName = Environment.GetEnvironmentVariable("INPUTBOX_LOG_FILE_NAME");
        _originalLogLevel = Environment.GetEnvironmentVariable("INPUTBOX_LOG_LEVEL");

        Environment.SetEnvironmentVariable("INPUTBOX_LOG_FILE_NAME", null);
        Environment.SetEnvironmentVariable("INPUTBOX_LOG_LEVEL", null);

        Directory.CreateDirectory(LoggerService.LogDirectory);

        BackupIfExists(MainLogPath, MainLogBackupPath);
        BackupIfExists(TestLogPath, TestLogBackupPath);
    }

    /// <summary>
    /// 還原測試前的日誌狀態。
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("INPUTBOX_LOG_FILE_NAME", _originalLogFileName);
        Environment.SetEnvironmentVariable("INPUTBOX_LOG_LEVEL", _originalLogLevel);

        foreach (string temporaryLogPath in _temporaryLogPaths)
        {
            DeleteIfExists(temporaryLogPath);
        }

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
    /// Release 且非測試主機時，預設日誌門檻應為 Warning，避免一般診斷污染正式日誌。
    /// </summary>
    [Fact]
    public void ResolveMinimumLogLevel_ReleaseNonTestHost_ReturnsWarning()
    {
        Assert.Equal(
            LoggerService.LogLevel.Warning,
            LoggerService.ResolveMinimumLogLevelForTests(
                isRunningUnderTestHost: false,
                isDebugBuild: false,
                overrideValue: null));
    }

    /// <summary>
    /// Debug 或測試主機應保留 Info 診斷，方便開發與回歸測試追蹤問題。
    /// </summary>
    [Fact]
    public void ResolveMinimumLogLevel_DebugOrTestHost_ReturnsInfo()
    {
        Assert.Equal(
            LoggerService.LogLevel.Info,
            LoggerService.ResolveMinimumLogLevelForTests(
                isRunningUnderTestHost: false,
                isDebugBuild: true,
                overrideValue: null));
        Assert.Equal(
            LoggerService.LogLevel.Info,
            LoggerService.ResolveMinimumLogLevelForTests(
                isRunningUnderTestHost: true,
                isDebugBuild: false,
                overrideValue: null));
    }

    /// <summary>
    /// 有效的 INPUTBOX_LOG_LEVEL 應覆寫建置組態預設門檻，方便使用者臨時開啟診斷。
    /// </summary>
    [Fact]
    public void ResolveMinimumLogLevel_ValidOverride_ReturnsOverride()
    {
        Assert.Equal(
            LoggerService.LogLevel.Info,
            LoggerService.ResolveMinimumLogLevelForTests(
                isRunningUnderTestHost: false,
                isDebugBuild: false,
                overrideValue: "Info"));
        Assert.Equal(
            LoggerService.LogLevel.Error,
            LoggerService.ResolveMinimumLogLevelForTests(
                isRunningUnderTestHost: true,
                isDebugBuild: true,
                overrideValue: "error"));
    }

    /// <summary>
    /// 無效的 INPUTBOX_LOG_LEVEL 不應改變預設門檻，避免拼錯值導致正式日誌過度輸出。
    /// </summary>
    [Fact]
    public void ResolveMinimumLogLevel_InvalidOverride_ReturnsDefault()
    {
        Assert.Equal(
            LoggerService.LogLevel.Warning,
            LoggerService.ResolveMinimumLogLevelForTests(
                isRunningUnderTestHost: false,
                isDebugBuild: false,
                overrideValue: "Verbose"));
        Assert.Equal(
            LoggerService.LogLevel.Warning,
            LoggerService.ResolveMinimumLogLevelForTests(
                isRunningUnderTestHost: false,
                isDebugBuild: false,
                overrideValue: "3"));
    }

    /// <summary>
    /// Warning 門檻下，Info 診斷不應寫入檔案。
    /// </summary>
    [Fact]
    public void LogInfo_WhenMinimumLevelIsWarning_DoesNotWrite()
    {
        string logPath = UseTemporaryLogFile();
        Environment.SetEnvironmentVariable("INPUTBOX_LOG_LEVEL", "Warning");

        LoggerService.LogInfo("logger-info-marker");

        Assert.False(File.Exists(logPath));
    }

    /// <summary>
    /// Warning 門檻下，Warning、Error 與例外都應寫入檔案。
    /// </summary>
    [Fact]
    public void LogWarningErrorAndException_WhenMinimumLevelIsWarning_Write()
    {
        string logPath = UseTemporaryLogFile();
        Environment.SetEnvironmentVariable("INPUTBOX_LOG_LEVEL", "Warning");

        LoggerService.LogWarning("logger-warning-marker");
        LoggerService.LogError("logger-error-marker");
        LoggerService.LogException(new InvalidOperationException("logger-exception-marker"), "LoggerServiceTests");

        string content = File.ReadAllText(logPath);
        Assert.Contains("[WARNING]", content);
        Assert.Contains("logger-warning-marker", content);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("logger-error-marker", content);
        Assert.Contains("[EXCEPTION]", content);
        Assert.Contains("logger-exception-marker", content);
    }

    /// <summary>
    /// 使用 INPUTBOX_LOG_LEVEL=Info 時，Info 診斷應可臨時寫入檔案。
    /// </summary>
    [Fact]
    public void LogInfo_WhenEnvironmentOverrideIsInfo_Writes()
    {
        string logPath = UseTemporaryLogFile();
        Environment.SetEnvironmentVariable("INPUTBOX_LOG_LEVEL", "Info");

        LoggerService.LogInfo("logger-info-override-marker");

        string content = File.ReadAllText(logPath);
        Assert.Contains("[INFO]", content);
        Assert.Contains("logger-info-override-marker", content);
    }

    /// <summary>
    /// 若目標檔案存在則先備份到測試專用路徑。
    /// </summary>
    /// <param name="sourcePath">原始日誌檔路徑。</param>
    /// <param name="backupPath">測試期間使用的備份檔路徑。</param>
    private static void BackupIfExists(string sourcePath, string backupPath)
    {
        RetryFileOperation(() =>
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, backupPath, overwrite: true);
            }
        });
    }

    /// <summary>
    /// 刪除檔案（若存在）。
    /// </summary>
    /// <param name="path">檔案路徑。</param>
    private static void DeleteIfExists(string path)
    {
        RetryFileOperation(() =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        });
    }

    /// <summary>
    /// 使用本測試專屬暫存日誌檔。
    /// </summary>
    /// <returns>暫存日誌檔完整路徑。</returns>
    private string UseTemporaryLogFile()
    {
        string logFileName = $"InputBox.logger-test-{Guid.NewGuid():N}.log";
        string logPath = Path.Combine(LoggerService.LogDirectory, logFileName);
        _temporaryLogPaths.Add(logPath);
        Environment.SetEnvironmentVariable("INPUTBOX_LOG_FILE_NAME", logFileName);

        return logPath;
    }

    /// <summary>
    /// 若有備份則還原，否則刪除測試產生的檔案。
    /// </summary>
    /// <param name="targetPath">要還原或刪除的目標檔案路徑。</param>
    /// <param name="backupPath">測試前保存的備份檔路徑。</param>
    private static void RestoreOrDelete(string targetPath, string backupPath)
    {
        RetryFileOperation(() =>
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (File.Exists(backupPath))
            {
                File.Move(backupPath, targetPath, overwrite: true);
            }
        });
    }

    /// <summary>
    /// 針對測試主機下可能尚未釋放的暫時性檔案鎖，提供有限次數的重試，避免清理階段偶發失敗。
    /// </summary>
    /// <param name="fileOperation">要重試的檔案操作。</param>
    private static void RetryFileOperation(Action fileOperation)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                fileOperation();
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(50);
        }

        throw lastException ?? new IOException("檔案操作在重試後仍失敗。");
    }
}
