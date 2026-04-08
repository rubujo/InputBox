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
    /// 片語清單最大容量
    /// </summary>
    public const int MaxPhraseCount = 50;

    /// <summary>
    /// 片語名稱最大長度
    /// </summary>
    public const int MaxPhraseNameLength = 50;

    /// <summary>
    /// 片語內容最大長度
    /// </summary>
    public const int MaxPhraseContentLength = 10000;

    /// <summary>
    /// 片語檔允許讀取的最大位元組數（512 KB）
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
    private static readonly Lock PhraseLock = new();

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
                    // 驗證並截斷。
                    foreach (PhraseEntry entry in loaded.Take(MaxPhraseCount))
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Name) &&
                            !string.IsNullOrEmpty(entry.Content))
                        {
                            _phrases.Add(new PhraseEntry(
                                entry.Name.Length > MaxPhraseNameLength ?
                                    entry.Name[..MaxPhraseNameLength] :
                                    entry.Name,
                                entry.Content.Length > MaxPhraseContentLength ?
                                    entry.Content[..MaxPhraseContentLength] :
                                    entry.Content));
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
    public void Save()
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
            if (_phrases.Count >= MaxPhraseCount)
            {
                return false;
            }

            _phrases.Add(new PhraseEntry(
                name.Length > MaxPhraseNameLength ? name[..MaxPhraseNameLength] : name,
                content.Length > MaxPhraseContentLength ? content[..MaxPhraseContentLength] : content));
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
                name.Length > MaxPhraseNameLength ? name[..MaxPhraseNameLength] : name,
                content.Length > MaxPhraseContentLength ? content[..MaxPhraseContentLength] : content);
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
}