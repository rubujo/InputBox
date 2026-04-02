using System.Diagnostics;
using System.Text;

namespace InputBox.Core.Services;

/// <summary>
/// 日誌服務（Logger Service）
/// </summary>
/// <remarks>
/// 專門用於記錄應用程式中的例外錯誤與重要資訊。
/// 具備執行緒安全、自動輪替 (Rotation）與檔案大小限制功能。
/// </remarks>
internal static class LoggerService
{
    /// <summary>
    /// 記錄鎖定物件（使用 C# 13 System.Threading.Lock）
    /// </summary>
    private static readonly Lock _logLock = new();

    /// <summary>
    /// 日誌存放目錄：LocalAppData/InputBox/Logs
    /// </summary>
    private static readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InputBox",
        "Logs");

    /// <summary>
    /// 主要日誌檔名
    /// </summary>
    private const string LogFileName = "InputBox.log";

    /// <summary>
    /// 單一檔案最大容量（預設 5MB）
    /// </summary>
    private const long MaxFileSize = 5 * 1024 * 1024;

    /// <summary>
    /// 最大備份檔案數量
    /// </summary>
    private const int MaxBackupFiles = 5;

    /// <summary>
    /// 記錄例外錯誤
    /// </summary>
    /// <param name="ex">例外物件</param>
    /// <param name="context">發生情境描述（選填）</param>
    public static void LogException(Exception? ex, string context = "")
    {
        if (ex == null)
        {
            return;
        }

        StringBuilder sb = new();

        sb.AppendLine("================================================================");
        sb.AppendLine($"[EXCEPTION] {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");

        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine($"Context: {context}");
        }

        int depth = 0;

        Exception? current = ex;

        // 遞迴記錄所有內部例外（Inner Exceptions），深度限制為 10 層以防無限循環。
        while (current != null &&
            depth < 10)
        {
            sb.AppendLine($"--- Level {depth} ---");
            sb.AppendLine($"Type: {current.GetType().FullName}");
            sb.AppendLine($"Message: {current.Message}");
            sb.AppendLine($"StackTrace:\n{current.StackTrace}");

            current = current.InnerException;

            depth++;
        }

        sb.AppendLine("================================================================");
        sb.AppendLine();

        WriteToFile(sb.ToString());
    }

    /// <summary>
    /// 記錄一般資訊
    /// </summary>
    /// <param name="message">訊息內容</param>
    public static void LogInfo(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string logEntry = $"[INFO] {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} - {message}{Environment.NewLine}";

        WriteToFile(logEntry);
    }

    /// <summary>
    /// 執行實體寫入動作（執行緒安全）
    /// </summary>
    /// <param name="content">要寫入的字串內容</param>
    private static void WriteToFile(string content)
    {
        // 使用 C# 13 Lock 物件確保多執行緒下的寫入完整性與輪替安全。
        lock (_logLock)
        {
            try
            {
                // 確保目錄存在。
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                string filePath = Path.Combine(_logDirectory, LogFileName);

                // 檢查是否需要執行檔案輪替。
                if (File.Exists(filePath))
                {
                    FileInfo fileInfo = new(filePath);

                    if (fileInfo.Length > MaxFileSize)
                    {
                        RotateLogs();
                    }
                }

                // 以 UTF-8 編碼附加內容。
                // AppendAllText 內部會處理 FileStream 的開啟與關閉。
                File.AppendAllText(filePath, content, Encoding.UTF8);
            }
            catch
            {
                // 靜默失敗：日誌服務不應拋出例外，以免造成應用程式二度崩潰或無窮遞迴。
                // 實務上可在此輸出至 Debug 控制台，但不影響程式運行。
                Debug.WriteLine("LoggerService 寫入失敗。");
            }
        }
    }

    /// <summary>
    /// 執行日誌輪替（Rotation）
    /// </summary>
    /// <remarks>
    /// 策略：InputBox.log -> InputBox.1.log -> ... -> InputBox.5.log
    /// 舊檔案會被往後推移，超過 MaxBackupFiles 的檔案將被刪除。
    /// </remarks>
    private static void RotateLogs()
    {
        try
        {
            // 1. 刪除最舊的備份檔案。
            string oldestBackup = Path.Combine(_logDirectory, $"InputBox.{MaxBackupFiles}.log");

            if (File.Exists(oldestBackup))
            {
                File.Delete(oldestBackup);
            }

            // 2. 將現有備份檔案往後推（e.g., 4.log -> 5.log）。
            for (int i = MaxBackupFiles - 1; i >= 1; i--)
            {
                string source = Path.Combine(_logDirectory, $"InputBox.{i}.log"),
                    target = Path.Combine(_logDirectory, $"InputBox.{i + 1}.log");

                if (File.Exists(source))
                {
                    File.Move(source, target, overwrite: true);
                }
            }

            // 3. 將當前日誌檔更名為第一個備份檔案（InputBox.log -> InputBox.1.log）。
            string currentFile = Path.Combine(_logDirectory, LogFileName);

            if (File.Exists(currentFile))
            {
                File.Move(currentFile, Path.Combine(_logDirectory, "InputBox.1.log"), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"日誌輪替失敗：{ex.Message}");
        }
    }
}