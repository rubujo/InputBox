using InputBox.Core.Configuration;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputBox.Core.Services;

/// <summary>
/// 片語管理服務，負責片語的載入、儲存與 CRUD 操作
/// <para>片語持久化至 %AppData%\InputBox\phrases.json。</para>
/// </summary>
internal sealed class PhraseService
{
    /// <summary>
    /// 片語資料模型
    /// </summary>
    /// <param name="Name">片語名稱（顯示於選單）</param>
    /// <param name="Content">片語內容（插入至輸入框）</param>
    public sealed record PhraseEntry(string Name, string Content);

    /// <summary>
    /// 片語匯入錯誤代碼
    /// </summary>
    public enum ImportError
    {
        /// <summary>
        /// 無錯誤（成功）
        /// </summary>
        None,
        /// <summary>
        /// 檔案不存在
        /// </summary>
        FileNotFound,
        /// <summary>
        /// 檔案超過允許大小
        /// </summary>
        FileTooLarge,
        /// <summary>
        /// JSON 格式無效或反序列化失敗
        /// </summary>
        InvalidJson,
        /// <summary>
        /// 片語已匯入記憶體，但寫入磁碟失敗（已回退）
        /// </summary>
        PersistenceFailed,
        /// <summary>
        /// 未預期的例外
        /// </summary>
        Unknown
    }

    /// <summary>
    /// 片語匯出錯誤代碼
    /// </summary>
    public enum ExportError
    {
        /// <summary>
        /// 無錯誤（成功）
        /// </summary>
        None,
        /// <summary>
        /// 寫入使用者選定路徑失敗
        /// </summary>
        Unknown
    }

    /// <summary>
    /// 片語匯入結果
    /// </summary>
    /// <param name="Success">是否成功</param>
    /// <param name="Error">失敗時的錯誤代碼</param>
    /// <param name="Imported">成功匯入的片語數</param>
    /// <param name="Total">檔案中的原始片語總數</param>
    public sealed record ImportOutcome(
        bool Success,
        ImportError Error = ImportError.None,
        int Imported = 0,
        int Total = 0);

    /// <summary>
    /// 片語匯出結果
    /// </summary>
    /// <param name="Success">是否成功</param>
    /// <param name="Error">失敗時的錯誤代碼</param>
    /// <param name="Exported">已匯出的片語數</param>
    public sealed record ExportOutcome(
        bool Success,
        ExportError Error = ExportError.None,
        int Exported = 0);

    /// <summary>
    /// 片語檔允許讀取的最大位元組數（512 KB）。
    /// 依據 AppSettings.MaxPhraseCount（50）× AppSettings.MaxInputLength（500）× UTF-8 CJK 最大 3 bytes/字元計算，
    /// 理論上限約 82 KB；設為 512 KB 作為防惡意或損壞大檔的安全守衛。
    /// </summary>
    private const long MaxPhraseFileSizeBytes = 512 * 1024;

    /// <summary>
    /// 片語檔案路徑
    /// </summary>
    private static readonly string PhrasePath = Path.Combine(
        AppSettings.ConfigDirectory,
        "phrases.json");

    /// <summary>
    /// JSON 序列化選項
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        MaxDepth = 16,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 片語暫存檔搜尋樣式，用於清理先前失敗流程殘留的暫存檔。
    /// </summary>
    private static readonly string PhraseTempFilePattern = $"{Path.GetFileName(PhrasePath)}*.tmp";

    /// <summary>
    /// 片語暫存檔清理前的保留寬限時間，避免刪除其他仍在併發寫入中的新鮮暫存檔。
    /// </summary>
    private static readonly TimeSpan PhraseTempCleanupGracePeriod = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 追蹤目前處於寫入流程中的片語暫存檔，避免跨執行緒誤刪。
    /// </summary>
    private static readonly Lock PhraseTempRegistryLock = new();

    /// <summary>
    /// 目前進行中的片語暫存檔集合。
    /// </summary>
    private static readonly HashSet<string> ActivePhraseTempFiles = [];

    /// <summary>
    /// 片語存取鎖
    /// </summary>
    private readonly Lock PhraseLock = new();

    /// <summary>
    /// 序列化片語異動與持久化流程的交易鎖，避免多個寫入交錯造成回滾覆蓋。
    /// </summary>
    private readonly Lock PhrasePersistenceLock = new();

    /// <summary>
    /// 片語清單
    /// </summary>
    private readonly List<PhraseEntry> _phrases = [];

    /// <summary>
    /// 取得片語清單的唯讀副本
    /// </summary>
    public IReadOnlyList<PhraseEntry> Phrases
    {
        get
        {
            lock (PhraseLock)
            {
                return [.. _phrases];
            }
        }
    }

    /// <summary>
    /// 取得片語數量
    /// </summary>
    public int Count
    {
        get
        {
            lock (PhraseLock)
            {
                return _phrases.Count;
            }
        }
    }

    /// <summary>
    /// 從磁碟載入片語
    /// </summary>
    public void Load()
    {
        lock (PhraseLock)
        {
            _phrases.Clear();

            if (!File.Exists(PhrasePath))
            {
                return;
            }

            try
            {
                if (new FileInfo(PhrasePath).Length > MaxPhraseFileSizeBytes)
                {
                    Debug.WriteLine("[片語] 片語檔超過允許的最大大小，拒絕讀取。");

                    return;
                }

                string json;

                using (FileStream fs = new(PhrasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new(fs, Utf8NoBom, detectEncodingFromByteOrderMarks: true))
                {
                    json = reader.ReadToEnd();
                }

                List<PhraseEntry>? loaded = JsonSerializer.Deserialize<List<PhraseEntry>>(json, Options);

                if (loaded != null)
                {
                    foreach (PhraseEntry entry in loaded.Take(AppSettings.MaxPhraseCount))
                    {
                        if (TryNormalizeEntry(entry, out PhraseEntry normalized))
                        {
                            _phrases.Add(normalized);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "片語檔讀取失敗");

                Debug.WriteLine($"[片語] 讀取失敗：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 將片語儲存至磁碟
    /// </summary>
    public void Save() => TrySave();

    /// <summary>
    /// 嘗試將片語儲存至磁碟，回傳是否成功。
    /// </summary>
    /// <returns>若成功寫入片語檔則為 true，否則為 false。</returns>
    private bool TrySave()
    {
        lock (PhrasePersistenceLock)
        {
            string json;

            lock (PhraseLock)
            {
                json = JsonSerializer.Serialize(_phrases, Options);
            }

            return TryWriteJsonToPath(PhrasePath, json, "片語檔儲存失敗");
        }
    }

    /// <summary>
    /// 在單一交易內套用片語異動並嘗試持久化；若持久化失敗則回滾記憶體狀態。
    /// </summary>
    /// <param name="mutation">對片語清單套用的異動函式；回傳 false 表示前置條件不符。</param>
    /// <returns>若異動與持久化皆成功則為 true，否則為 false。</returns>
    private bool TryMutateAndPersist(Func<List<PhraseEntry>, bool> mutation)
    {
        lock (PhrasePersistenceLock)
        {
            List<PhraseEntry> snapshot;
            string json;

            lock (PhraseLock)
            {
                snapshot = [.. _phrases];

                if (!mutation(_phrases))
                {
                    return false;
                }

                json = JsonSerializer.Serialize(_phrases, Options);
            }

            if (TryWriteJsonToPath(PhrasePath, json, "片語檔儲存失敗"))
            {
                return true;
            }

            lock (PhraseLock)
            {
                _phrases.Clear();
                _phrases.AddRange(snapshot);
            }

            return false;
        }
    }

    /// <summary>
    /// 將指定 JSON 內容原子寫入目標路徑，並在失敗時回傳 false。
    /// </summary>
    /// <param name="filePath">目標檔案完整路徑。</param>
    /// <param name="json">要寫入的 JSON 內容。</param>
    /// <param name="logContext">記錄例外時使用的描述文字。</param>
    /// <returns>若成功寫入則為 true，否則為 false。</returns>
    private static bool TryWriteJsonToPath(string filePath, string json, string logContext)
    {
        string tempPath = Path.Combine(
            Path.GetDirectoryName(filePath) ?? AppSettings.ConfigDirectory,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        string registeredTempPath = tempPath;

        RegisterActivePhraseTempFile(registeredTempPath);

        try
        {
            string? directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, json, Utf8NoBom);

            Exception? lastIoException = null;
            int delayMs = 20;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Replace(tempPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, filePath);
                    }

                    tempPath = string.Empty;
                    return true;
                }
                catch (IOException ex) when (attempt < 5)
                {
                    lastIoException = ex;
                    Thread.Sleep(delayMs);
                    delayMs *= 2;
                }
                catch (IOException ex)
                {
                    lastIoException = ex;
                    break;
                }
            }

            throw lastIoException ?? new IOException("JSON 檔替換失敗。");
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, logContext);
            Debug.WriteLine($"[片語] {logContext}：{ex.Message}");
            return false;
        }
        finally
        {
            // 無論成功或失敗，都要解除註冊最初建立的暫存檔；
            // 成功路徑中 tempPath 可能已清成空字串，不能拿來當作解除註冊依據。
            UnregisterActivePhraseTempFile(registeredTempPath);
            TryDeletePhraseTempFile(tempPath);
            CleanupPhraseTempFiles(filePath);
        }
    }

    /// <summary>
    /// 將正在寫入中的片語暫存檔註冊到追蹤集合。
    /// </summary>
    /// <param name="tempPath">暫存檔路徑。</param>
    private static void RegisterActivePhraseTempFile(string tempPath)
    {
        lock (PhraseTempRegistryLock)
        {
            _ = ActivePhraseTempFiles.Add(tempPath);
        }
    }

    /// <summary>
    /// 將已完成或失敗的片語暫存檔自追蹤集合移除。
    /// </summary>
    /// <param name="tempPath">暫存檔路徑。</param>
    private static void UnregisterActivePhraseTempFile(string tempPath)
    {
        if (string.IsNullOrEmpty(tempPath))
        {
            return;
        }

        lock (PhraseTempRegistryLock)
        {
            _ = ActivePhraseTempFiles.Remove(tempPath);
        }
    }

    /// <summary>
    /// 判斷指定片語暫存檔是否仍處於活躍寫入流程中。
    /// </summary>
    /// <param name="tempPath">暫存檔路徑。</param>
    /// <returns>若仍在寫入流程中則為 true。</returns>
    private static bool IsActivePhraseTempFile(string tempPath)
    {
        lock (PhraseTempRegistryLock)
        {
            return ActivePhraseTempFiles.Contains(tempPath);
        }
    }

    /// <summary>
    /// 判斷指定片語暫存檔是否符合本服務建立的唯一暫存檔命名格式。
    /// </summary>
    /// <param name="baseFilePath">原始片語 JSON 檔路徑，用於產生暫存檔名前綴。</param>
    /// <param name="tempPath">待驗證的暫存檔路徑。</param>
    /// <returns>若為本服務建立的 GUID 暫存檔則為 true。</returns>
    private static bool IsManagedPhraseTempFile(string baseFilePath, string tempPath)
    {
        string fileName = Path.GetFileName(tempPath),
            prefix = $"{Path.GetFileName(baseFilePath)}.";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidate = fileName[prefix.Length..^4];

        return Guid.TryParseExact(candidate, "N", out _);
    }

    /// <summary>
    /// 嘗試刪除指定的片語暫存檔，供失敗收尾時使用。
    /// </summary>
    /// <param name="tempPath">暫存檔路徑。</param>
    private static void TryDeletePhraseTempFile(string tempPath)
    {
        if (string.IsNullOrEmpty(tempPath) ||
            !File.Exists(tempPath))
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch (IOException)
        {

        }
        catch (UnauthorizedAccessException)
        {

        }
    }

    /// <summary>
    /// 清理片語目錄內殘留的片語暫存檔。
    /// </summary>
    private static void CleanupPhraseTempFiles(string? baseFilePath = null)
    {
        try
        {
            string targetPath = string.IsNullOrWhiteSpace(baseFilePath) ? PhrasePath : baseFilePath;
            string fullTargetPath = Path.GetFullPath(targetPath);
            string directory = Path.GetDirectoryName(fullTargetPath) ?? AppSettings.ConfigDirectory;
            string tempFilePattern = $"{Path.GetFileName(fullTargetPath)}*.tmp";

            if (!Directory.Exists(directory))
            {
                return;
            }

            DateTime utcNow = DateTime.UtcNow;

            foreach (string tempPathForCleanup in Directory.GetFiles(directory, tempFilePattern))
            {
                try
                {
                    if (IsActivePhraseTempFile(tempPathForCleanup))
                    {
                        continue;
                    }

                    bool isManagedTempFile = IsManagedPhraseTempFile(fullTargetPath, tempPathForCleanup);

                    if (!isManagedTempFile &&
                        utcNow - File.GetLastWriteTimeUtc(tempPathForCleanup) < PhraseTempCleanupGracePeriod)
                    {
                        continue;
                    }

                    File.Delete(tempPathForCleanup);
                }
                catch (IOException)
                {

                }
                catch (UnauthorizedAccessException)
                {

                }
            }
        }
        catch (Exception cleanupEx)
        {
            Debug.WriteLine($"[片語] 暫存檔清理失敗，已忽略：{cleanupEx.Message}");
        }
    }

    /// <summary>
    /// 新增片語
    /// </summary>
    /// <param name="name">名稱</param>
    /// <param name="content">內容</param>
    /// <returns>是否成功</returns>
    public bool Add(string name, string content)
    {
        if (string.IsNullOrEmpty(name) ||
            string.IsNullOrEmpty(content))
        {
            return false;
        }

        return TryMutateAndPersist(phrases =>
        {
            if (phrases.Count >= AppSettings.MaxPhraseCount)
            {
                return false;
            }

            phrases.Add(new PhraseEntry(
                name.Length > AppSettings.MaxPhraseNameLength ? name[..AppSettings.MaxPhraseNameLength] : name,
                content.Length > AppSettings.MaxInputLength ? content[..AppSettings.MaxInputLength] : content));

            return true;
        });
    }

    /// <summary>
    /// 更新指定索引的片語
    /// </summary>
    /// <param name="index">索引</param>
    /// <param name="name">新名稱</param>
    /// <param name="content">新內容</param>
    /// <returns>是否成功</returns>
    public bool Update(int index, string name, string content)
    {
        if (string.IsNullOrEmpty(name) ||
            string.IsNullOrEmpty(content))
        {
            return false;
        }

        return TryMutateAndPersist(phrases =>
        {
            if (index < 0 ||
                index >= phrases.Count)
            {
                return false;
            }

            phrases[index] = new PhraseEntry(
                name.Length > AppSettings.MaxPhraseNameLength ? name[..AppSettings.MaxPhraseNameLength] : name,
                content.Length > AppSettings.MaxInputLength ? content[..AppSettings.MaxInputLength] : content);

            return true;
        });
    }

    /// <summary>
    /// 移除指定索引的片語
    /// </summary>
    /// <param name="index">索引</param>
    /// <returns>是否成功</returns>
    public bool Remove(int index)
    {
        return TryMutateAndPersist(phrases =>
        {
            if (index < 0 ||
                index >= phrases.Count)
            {
                return false;
            }

            phrases.RemoveAt(index);
            return true;
        });
    }

    /// <summary>
    /// 將片語向上移動一個位置
    /// </summary>
    /// <param name="index">索引</param>
    /// <returns>是否成功</returns>
    public bool MoveUp(int index)
    {
        return TryMutateAndPersist(phrases =>
        {
            if (index <= 0 ||
                index >= phrases.Count)
            {
                return false;
            }

            (phrases[index - 1], phrases[index]) = (phrases[index], phrases[index - 1]);
            return true;
        });
    }

    /// <summary>
    /// 將片語向下移動一個位置
    /// </summary>
    /// <param name="index">索引</param>
    /// <returns>是否成功</returns>
    public bool MoveDown(int index)
    {
        return TryMutateAndPersist(phrases =>
        {
            if (index < 0 ||
                index >= phrases.Count - 1)
            {
                return false;
            }

            (phrases[index], phrases[index + 1]) = (phrases[index + 1], phrases[index]);
            return true;
        });
    }

    /// <summary>
    /// 驗證並正規化片語項目。名稱為空白或內容為空時回傳 false。
    /// </summary>
    /// <param name="entry">原始項目</param>
    /// <param name="normalized">正規化後的項目（名稱與內容截斷至上限）</param>
    private static bool TryNormalizeEntry(PhraseEntry entry, out PhraseEntry normalized)
    {
        if (string.IsNullOrWhiteSpace(entry.Name) ||
            string.IsNullOrEmpty(entry.Content))
        {
            normalized = default!;
            return false;
        }

        normalized = new PhraseEntry(
            entry.Name.Length > AppSettings.MaxPhraseNameLength
                ? entry.Name[..AppSettings.MaxPhraseNameLength]
                : entry.Name,
            entry.Content.Length > AppSettings.MaxInputLength
                ? entry.Content[..AppSettings.MaxInputLength]
                : entry.Content);

        return true;
    }

    /// <summary>
    /// 將目前片語清單匯出至使用者指定路徑。
    /// <para>使用唯一暫存檔、原子替換與退避重試，避免併發匯出時發生檔案碰撞或殘留暫存檔。</para>
    /// </summary>
    /// <param name="filePath">要輸出的目標檔案完整路徑。</param>
    /// <returns>包含成功狀態、錯誤類型與匯出筆數的匯出結果。</returns>
    public ExportOutcome ExportToFile(string filePath)
    {
        string json;
        int count;

        lock (PhraseLock)
        {
            count = _phrases.Count;
            json = JsonSerializer.Serialize(_phrases, Options);
        }

        return TryWriteJsonToPath(filePath, json, "片語匯出失敗")
            ? new ExportOutcome(true, ExportError.None, count)
            : new ExportOutcome(false, ExportError.Unknown);
    }

    /// <summary>
    /// 從使用者指定路徑匯入片語，取代目前清單。
    /// 若持久化失敗，會回退至匯入前的狀態。
    /// </summary>
    /// <param name="filePath">來源檔案路徑</param>
    /// <returns>匯入結果</returns>
    public ImportOutcome ImportFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ImportOutcome(false, ImportError.FileNotFound);
        }

        if (new FileInfo(filePath).Length > MaxPhraseFileSizeBytes)
        {
            return new ImportOutcome(false, ImportError.FileTooLarge);
        }

        string json;

        try
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new(fs, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "片語匯入讀取失敗");
            Debug.WriteLine($"[片語] 匯入讀取失敗：{ex.Message}");
            return new ImportOutcome(false, ImportError.Unknown);
        }

        List<PhraseEntry>? loaded;

        try
        {
            loaded = JsonSerializer.Deserialize<List<PhraseEntry>>(json, Options);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[片語] 匯入 JSON 解析失敗：{ex.Message}");
            return new ImportOutcome(false, ImportError.InvalidJson);
        }

        if (loaded == null)
        {
            return new ImportOutcome(false, ImportError.InvalidJson);
        }

        int total = loaded.Count;
        List<PhraseEntry> valid = [];

        foreach (PhraseEntry entry in loaded.Take(AppSettings.MaxPhraseCount))
        {
            if (TryNormalizeEntry(entry, out PhraseEntry normalized))
            {
                valid.Add(normalized);
            }
        }

        // 備份現有清單，供持久化失敗時回退。
        List<PhraseEntry> snapshot;

        lock (PhraseLock)
        {
            snapshot = [.. _phrases];
            _phrases.Clear();
            _phrases.AddRange(valid);
        }

        if (!TrySave())
        {
            lock (PhraseLock)
            {
                _phrases.Clear();
                _phrases.AddRange(snapshot);
            }

            return new ImportOutcome(false, ImportError.PersistenceFailed);
        }

        return new ImportOutcome(true, ImportError.None, valid.Count, total);
    }
}