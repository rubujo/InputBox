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
        /// <summary>無錯誤（成功）</summary>
        None,
        /// <summary>檔案不存在</summary>
        FileNotFound,
        /// <summary>檔案超過允許大小</summary>
        FileTooLarge,
        /// <summary>JSON 格式無效或反序列化失敗</summary>
        InvalidJson,
        /// <summary>片語已匯入記憶體，但寫入磁碟失敗（已回退）</summary>
        PersistenceFailed,
        /// <summary>未預期的例外</summary>
        Unknown
    }

    /// <summary>
    /// 片語匯出錯誤代碼
    /// </summary>
    public enum ExportError
    {
        /// <summary>無錯誤（成功）</summary>
        None,
        /// <summary>寫入使用者選定路徑失敗</summary>
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
    /// 片語存取鎖
    /// </summary>
    private readonly Lock PhraseLock = new();

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
    /// 嘗試將片語儲存至磁碟，回傳是否成功
    /// </summary>
    private bool TrySave()
    {
        string json;

        lock (PhraseLock)
        {
            json = JsonSerializer.Serialize(_phrases, Options);
        }

        try
        {
            if (!Directory.Exists(AppSettings.ConfigDirectory))
            {
                Directory.CreateDirectory(AppSettings.ConfigDirectory);
            }

            string tempPath = PhrasePath + ".tmp";

            File.WriteAllText(tempPath, json, Utf8NoBom);

            int retries = 3;

            while (retries > 0)
            {
                try
                {
                    File.Move(tempPath, PhrasePath, overwrite: true);

                    break;
                }
                catch (IOException) when (retries > 1)
                {
                    retries--;

                    Thread.Sleep(1);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "片語檔儲存失敗");

            Debug.WriteLine($"[片語] 儲存失敗：{ex.Message}");

            // 嘗試清理可能殘留的暫存檔，與 AppSettings.WriteConfigToFile 保持一致。
            try
            {
                string tempPathForCleanup = PhrasePath + ".tmp";

                if (File.Exists(tempPathForCleanup))
                {
                    File.Delete(tempPathForCleanup);
                }
            }
            catch (Exception cleanupEx)
            {
                Debug.WriteLine($"[片語] 暫存檔清理失敗，已忽略：{cleanupEx.Message}");
            }

            return false;
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

        lock (PhraseLock)
        {
            if (_phrases.Count >= AppSettings.MaxPhraseCount)
            {
                return false;
            }

            _phrases.Add(new PhraseEntry(
                name.Length > AppSettings.MaxPhraseNameLength ? name[..AppSettings.MaxPhraseNameLength] : name,
                content.Length > AppSettings.MaxInputLength ? content[..AppSettings.MaxInputLength] : content));
        }

        Save();

        return true;
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

        lock (PhraseLock)
        {
            if (index < 0 ||
                index >= _phrases.Count)
            {
                return false;
            }

            _phrases[index] = new PhraseEntry(
                name.Length > AppSettings.MaxPhraseNameLength ? name[..AppSettings.MaxPhraseNameLength] : name,
                content.Length > AppSettings.MaxInputLength ? content[..AppSettings.MaxInputLength] : content);
        }

        Save();

        return true;
    }

    /// <summary>
    /// 移除指定索引的片語
    /// </summary>
    /// <param name="index">索引</param>
    /// <returns>是否成功</returns>
    public bool Remove(int index)
    {
        lock (PhraseLock)
        {
            if (index < 0 ||
                index >= _phrases.Count)
            {
                return false;
            }

            _phrases.RemoveAt(index);
        }

        Save();

        return true;
    }

    /// <summary>
    /// 將片語向上移動一個位置
    /// </summary>
    /// <param name="index">索引</param>
    /// <returns>是否成功</returns>
    public bool MoveUp(int index)
    {
        lock (PhraseLock)
        {
            if (index <= 0 ||
                index >= _phrases.Count)
            {
                return false;
            }

            (_phrases[index - 1], _phrases[index]) = (_phrases[index], _phrases[index - 1]);
        }

        Save();

        return true;
    }

    /// <summary>
    /// 將片語向下移動一個位置
    /// </summary>
    /// <param name="index">索引</param>
    /// <returns>是否成功</returns>
    public bool MoveDown(int index)
    {
        lock (PhraseLock)
        {
            if (index < 0 ||
                index >= _phrases.Count - 1)
            {
                return false;
            }

            (_phrases[index], _phrases[index + 1]) = (_phrases[index + 1], _phrases[index]);
        }

        Save();

        return true;
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
    /// 使用 .tmp 暫存檔 + File.Move 確保寫入原子性。
    /// </summary>
    /// <param name="filePath">目標檔案路徑</param>
    /// <returns>匯出結果</returns>
    public ExportOutcome ExportToFile(string filePath)
    {
        string json;
        int count;

        lock (PhraseLock)
        {
            count = _phrases.Count;
            json = JsonSerializer.Serialize(_phrases, Options);
        }

        string tempPath = filePath + ".tmp";

        try
        {
            string? dir = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(tempPath, json, Utf8NoBom);

            int retries = 3;

            while (retries > 0)
            {
                try
                {
                    File.Move(tempPath, filePath, overwrite: true);
                    break;
                }
                catch (IOException) when (retries > 1)
                {
                    retries--;
                    Thread.Sleep(1);
                }
            }

            return new ExportOutcome(true, ExportError.None, count);
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "片語匯出失敗");
            Debug.WriteLine($"[片語] 匯出失敗：{ex.Message}");

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx)
            {
                Debug.WriteLine($"[片語] 匯出暫存檔清理失敗，已忽略：{cleanupEx.Message}");
            }

            return new ExportOutcome(false, ExportError.Unknown);
        }
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
