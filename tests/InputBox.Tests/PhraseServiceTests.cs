using InputBox.Core.Configuration;
using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// PhraseService 單元測試
/// <para>
/// 注意：PhraseService 的 CRUD 方法會呼叫 Save()，Save() 會寫入
/// %AppData%\InputBox\phrases.json。本測試使用備份／還原策略確保
/// 使用者的原始片語不受測試影響。
/// </para>
/// </summary>
public sealed class PhraseServiceTests : IDisposable
{
    private static readonly string PhrasePath = Path.Combine(
        AppSettings.ConfigDirectory, "phrases.json");

    private static readonly string BackupPath = PhrasePath + ".testbackup";

    /// <summary>
    /// 建構子：若 <c>phrases.json</c> 存在，備份至 <c>.testbackup</c>，確保測試不污染使用者資料。
    /// </summary>
    public PhraseServiceTests()
    {
        Directory.CreateDirectory(AppSettings.ConfigDirectory);

        if (File.Exists(PhrasePath))
        {
            File.Copy(PhrasePath, BackupPath, overwrite: true);
        }
    }

    /// <summary>
    /// 還原備份：若備份存在則移回原路徑；若無備份則刪除測試產生的 <c>phrases.json</c>。
    /// </summary>
    public void Dispose()
    {
        if (File.Exists(BackupPath))
        {
            File.Move(BackupPath, PhrasePath, overwrite: true);
        }
        else if (File.Exists(PhrasePath))
        {
            File.Delete(PhrasePath);
        }
    }

    // ── Add ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 名稱為空字串時，Add() 應回傳 false 且不增加片語數量。
    /// </summary>
    [Fact]
    public void Add_EmptyName_ReturnsFalse()
    {
        var svc = new PhraseService();

        bool result = svc.Add("", "some content");

        Assert.False(result);
        Assert.Equal(0, svc.Count);
    }

    /// <summary>
    /// 內容為空字串時，Add() 應回傳 false 且不增加片語數量。
    /// </summary>
    [Fact]
    public void Add_EmptyContent_ReturnsFalse()
    {
        var svc = new PhraseService();

        bool result = svc.Add("name", "");

        Assert.False(result);
        Assert.Equal(0, svc.Count);
    }

    /// <summary>
    /// 名稱與內容皆有效時，Add() 應回傳 true 並將 Count 增加為 1。
    /// </summary>
    [Fact]
    public void Add_ValidEntry_ReturnsTrueAndIncreasesCount()
    {
        var svc = new PhraseService();

        bool result = svc.Add("Hello", "Hello World");

        Assert.True(result);
        Assert.Equal(1, svc.Count);
    }

    /// <summary>
    /// 新增後，Phrases[0] 應正確反映傳入的名稱與內容。
    /// </summary>
    [Fact]
    public void Add_ValidEntry_PhrasesContainsEntry()
    {
        var svc = new PhraseService();
        svc.Add("TestName", "TestContent");

        var entry = svc.Phrases[0];

        Assert.Equal("TestName", entry.Name);
        Assert.Equal("TestContent", entry.Content);
    }

    /// <summary>
    /// 名稱超過 MaxPhraseNameLength 時，應自動截斷至上限長度，不拋出例外。
    /// </summary>
    [Fact]
    public void Add_NameExceedsMaxLength_TruncatesName()
    {
        var svc = new PhraseService();
        string longName = new('A', AppSettings.MaxPhraseNameLength + 10);

        svc.Add(longName, "content");

        Assert.Equal(AppSettings.MaxPhraseNameLength, svc.Phrases[0].Name.Length);
    }

    /// <summary>
    /// 內容超過 MaxInputLength 時，應自動截斷至上限長度，不拋出例外。
    /// </summary>
    [Fact]
    public void Add_ContentExceedsMaxLength_TruncatesContent()
    {
        var svc = new PhraseService();
        string longContent = new('B', AppSettings.MaxInputLength + 10);

        svc.Add("name", longContent);

        Assert.Equal(AppSettings.MaxInputLength, svc.Phrases[0].Content.Length);
    }

    /// <summary>
    /// 片語數量已達 MaxPhraseCount 上限時，Add() 應回傳 false 且不超出上限。
    /// </summary>
    [Fact]
    public void Add_AtMaxCount_ReturnsFalse()
    {
        var svc = new PhraseService();

        for (int i = 0; i < AppSettings.MaxPhraseCount; i++)
        {
            svc.Add($"name{i}", $"content{i}");
        }

        bool result = svc.Add("overflow", "overflow content");

        Assert.False(result);
        Assert.Equal(AppSettings.MaxPhraseCount, svc.Count);
    }

    // ── Update ───────────────────────────────────────────────────────────

    /// <summary>
    /// 清單為空時使用索引 0 呼叫 Update()，應回傳 false（索引越界）。
    /// </summary>
    [Fact]
    public void Update_InvalidIndex_ReturnsFalse()
    {
        var svc = new PhraseService();

        bool result = svc.Update(0, "name", "content");

        Assert.False(result);
    }

    /// <summary>
    /// 使用負數索引呼叫 Update()，應回傳 false。
    /// </summary>
    [Fact]
    public void Update_NegativeIndex_ReturnsFalse()
    {
        var svc = new PhraseService();
        svc.Add("initial", "initial content");

        bool result = svc.Update(-1, "name", "content");

        Assert.False(result);
    }

    /// <summary>
    /// 更新時名稱為空字串，應回傳 false 且不修改現有片語。
    /// </summary>
    [Fact]
    public void Update_EmptyName_ReturnsFalse()
    {
        var svc = new PhraseService();
        svc.Add("initial", "initial content");

        bool result = svc.Update(0, "", "new content");

        Assert.False(result);
    }

    /// <summary>
    /// 提供有效索引與新值時，Update() 應回傳 true，且片語應反映新名稱與新內容。
    /// </summary>
    [Fact]
    public void Update_ValidEntry_ReflectsNewValues()
    {
        var svc = new PhraseService();
        svc.Add("old name", "old content");

        bool result = svc.Update(0, "new name", "new content");

        Assert.True(result);
        Assert.Equal("new name", svc.Phrases[0].Name);
        Assert.Equal("new content", svc.Phrases[0].Content);
    }

    /// <summary>
    /// 更新時名稱超過 MaxPhraseNameLength，應自動截斷至上限長度。
    /// </summary>
    [Fact]
    public void Update_NameExceedsMaxLength_TruncatesName()
    {
        var svc = new PhraseService();
        svc.Add("initial", "content");
        string longName = new('Z', AppSettings.MaxPhraseNameLength + 5);

        svc.Update(0, longName, "content");

        Assert.Equal(AppSettings.MaxPhraseNameLength, svc.Phrases[0].Name.Length);
    }

    // ── Remove ───────────────────────────────────────────────────────────

    /// <summary>
    /// 清單為空時呼叫 Remove(0)，應回傳 false（索引越界）。
    /// </summary>
    [Fact]
    public void Remove_InvalidIndex_ReturnsFalse()
    {
        var svc = new PhraseService();

        bool result = svc.Remove(0);

        Assert.False(result);
    }

    /// <summary>
    /// 使用負數索引呼叫 Remove()，應回傳 false。
    /// </summary>
    [Fact]
    public void Remove_NegativeIndex_ReturnsFalse()
    {
        var svc = new PhraseService();
        svc.Add("test", "content");

        bool result = svc.Remove(-1);

        Assert.False(result);
    }

    /// <summary>
    /// 使用有效索引呼叫 Remove()，應回傳 true 且 Count 減少為 1。
    /// </summary>
    [Fact]
    public void Remove_ValidIndex_ReturnsTrueAndDecrementsCount()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");
        svc.Add("B", "contentB");

        bool result = svc.Remove(0);

        Assert.True(result);
        Assert.Equal(1, svc.Count);
    }

    /// <summary>
    /// 刪除索引 0 後，原索引 1 的片語應移至索引 0，驗證清單順序正確維護。
    /// </summary>
    [Fact]
    public void Remove_ValidIndex_RemovesCorrectEntry()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");
        svc.Add("B", "contentB");

        svc.Remove(0);

        Assert.Equal("B", svc.Phrases[0].Name);
    }

    // ── MoveUp ───────────────────────────────────────────────────────────

    /// <summary>
    /// 對索引 0 呼叫 MoveUp()，已在清單頂端，應回傳 false。
    /// </summary>
    [Fact]
    public void MoveUp_IndexZero_ReturnsFalse()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");

        bool result = svc.MoveUp(0);

        Assert.False(result);
    }

    /// <summary>
    /// 對索引 1 呼叫 MoveUp()，應與索引 0 交換位置，且回傳 true。
    /// </summary>
    [Fact]
    public void MoveUp_IndexOne_SwapsWithPrevious()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");
        svc.Add("B", "contentB");

        bool result = svc.MoveUp(1);

        Assert.True(result);
        Assert.Equal("B", svc.Phrases[0].Name);
        Assert.Equal("A", svc.Phrases[1].Name);
    }

    /// <summary>
    /// 使用超出清單範圍的索引呼叫 MoveUp()，應回傳 false。
    /// </summary>
    [Fact]
    public void MoveUp_OutOfBoundsIndex_ReturnsFalse()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");

        bool result = svc.MoveUp(5);

        Assert.False(result);
    }

    // ── MoveDown ─────────────────────────────────────────────────────────

    /// <summary>
    /// 對最後一個元素（唯一元素）呼叫 MoveDown()，已在清單底端，應回傳 false。
    /// </summary>
    [Fact]
    public void MoveDown_LastIndex_ReturnsFalse()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");

        bool result = svc.MoveDown(0);

        Assert.False(result);
    }

    /// <summary>
    /// 對索引 0 呼叫 MoveDown()（清單有兩項），應與索引 1 交換，且回傳 true。
    /// </summary>
    [Fact]
    public void MoveDown_ValidIndex_SwapsWithNext()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");
        svc.Add("B", "contentB");

        bool result = svc.MoveDown(0);

        Assert.True(result);
        Assert.Equal("B", svc.Phrases[0].Name);
        Assert.Equal("A", svc.Phrases[1].Name);
    }

    /// <summary>
    /// 使用負數索引呼叫 MoveDown()，應回傳 false。
    /// </summary>
    [Fact]
    public void MoveDown_NegativeIndex_ReturnsFalse()
    {
        var svc = new PhraseService();
        svc.Add("A", "contentA");
        svc.Add("B", "contentB");

        bool result = svc.MoveDown(-1);

        Assert.False(result);
    }

    // ── ExportToFile ─────────────────────────────────────────────────────

    /// <summary>
    /// 有片語時呼叫 ExportToFile()，應將片語序列化至指定檔案，且回傳 Success=true。
    /// </summary>
    [Fact]
    public void ExportToFile_WithPhrases_WritesFileAndReturnsSuccess()
    {
        var svc = new PhraseService();
        svc.Add("名稱A", "內容A");
        svc.Add("名稱B", "內容B");

        string exportPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");

        try
        {
            PhraseService.ExportOutcome result = svc.ExportToFile(exportPath);

            Assert.True(result.Success);
            Assert.Equal(PhraseService.ExportError.None, result.Error);
            Assert.Equal(2, result.Exported);
            Assert.True(File.Exists(exportPath));

            string content = File.ReadAllText(exportPath);
            Assert.Contains("名稱A", content);
            Assert.Contains("內容B", content);
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    /// <summary>
    /// 無片語時呼叫 ExportToFile()，應匯出空陣列 JSON 且回傳 Exported=0。
    /// </summary>
    [Fact]
    public void ExportToFile_NoPhrases_WritesEmptyArrayAndReturnsSuccess()
    {
        var svc = new PhraseService();

        string exportPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");

        try
        {
            PhraseService.ExportOutcome result = svc.ExportToFile(exportPath);

            Assert.True(result.Success);
            Assert.Equal(0, result.Exported);
            Assert.True(File.Exists(exportPath));

            string content = File.ReadAllText(exportPath).Trim();
            Assert.Equal("[]", content);
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    // ── ImportFromFile ────────────────────────────────────────────────────

    /// <summary>
    /// 匯入有效 JSON 後，片語清單應正確更新，且回傳 Success=true 及正確計數。
    /// </summary>
    [Fact]
    public void ImportFromFile_ValidJson_ReplacesPhrasesAndReturnsSuccess()
    {
        var svc = new PhraseService();
        svc.Add("舊片語", "舊內容");

        string importPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");
        string json = """[{"Name":"新片語","Content":"新內容"}]""";

        try
        {
            File.WriteAllText(importPath, json, System.Text.Encoding.UTF8);

            PhraseService.ImportOutcome result = svc.ImportFromFile(importPath);

            Assert.True(result.Success);
            Assert.Equal(PhraseService.ImportError.None, result.Error);
            Assert.Equal(1, result.Imported);
            Assert.Equal(1, result.Total);
            Assert.Single(svc.Phrases);
            Assert.Equal("新片語", svc.Phrases[0].Name);
            Assert.Equal("新內容", svc.Phrases[0].Content);
        }
        finally
        {
            if (File.Exists(importPath)) File.Delete(importPath);
        }
    }

    /// <summary>
    /// 匯入空陣列 JSON，應清空片語清單並回傳 Imported=0，Success=true。
    /// </summary>
    [Fact]
    public void ImportFromFile_EmptyArray_ClearsPhrases()
    {
        var svc = new PhraseService();
        svc.Add("保留片語", "保留內容");

        string importPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(importPath, "[]", System.Text.Encoding.UTF8);

            PhraseService.ImportOutcome result = svc.ImportFromFile(importPath);

            Assert.True(result.Success);
            Assert.Equal(0, result.Imported);
            Assert.Equal(0, result.Total);
            Assert.Empty(svc.Phrases);
        }
        finally
        {
            if (File.Exists(importPath)) File.Delete(importPath);
        }
    }

    /// <summary>
    /// 匯入超過 MaxPhraseCount（50）的片語，應截斷至上限，仍回傳 Success=true。
    /// </summary>
    [Fact]
    public void ImportFromFile_ExceedsMaxCount_TruncatesToMaxPhraseCount()
    {
        var svc = new PhraseService();

        var entries = Enumerable.Range(1, 60)
            .Select(i => $"{{\"Name\":\"片語{i}\",\"Content\":\"內容{i}\"}}")
            .ToList();

        string json = $"[{string.Join(",", entries)}]";
        string importPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(importPath, json, System.Text.Encoding.UTF8);

            PhraseService.ImportOutcome result = svc.ImportFromFile(importPath);

            Assert.True(result.Success);
            Assert.Equal(60, result.Total);
            Assert.Equal(AppSettings.MaxPhraseCount, result.Imported);
            Assert.Equal(AppSettings.MaxPhraseCount, svc.Count);
        }
        finally
        {
            if (File.Exists(importPath)) File.Delete(importPath);
        }
    }

    /// <summary>
    /// 匯入格式無效的 JSON，應回傳 InvalidJson 錯誤。
    /// </summary>
    [Fact]
    public void ImportFromFile_InvalidJson_ReturnsInvalidJsonError()
    {
        var svc = new PhraseService();

        string importPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(importPath, "{ not valid json ]]]", System.Text.Encoding.UTF8);

            PhraseService.ImportOutcome result = svc.ImportFromFile(importPath);

            Assert.False(result.Success);
            Assert.Equal(PhraseService.ImportError.InvalidJson, result.Error);
        }
        finally
        {
            if (File.Exists(importPath)) File.Delete(importPath);
        }
    }

    /// <summary>
    /// 匯入不存在的路徑，應回傳 FileNotFound 錯誤。
    /// </summary>
    [Fact]
    public void ImportFromFile_FileNotFound_ReturnsFileNotFoundError()
    {
        var svc = new PhraseService();

        PhraseService.ImportOutcome result = svc.ImportFromFile(
            Path.Combine(Path.GetTempPath(), "nonexistent_phrase_file.json"));

        Assert.False(result.Success);
        Assert.Equal(PhraseService.ImportError.FileNotFound, result.Error);
    }

    /// <summary>
    /// 匯入超過 MaxPhraseFileSizeBytes 的檔案，應回傳 FileTooLarge 錯誤，且不修改現有片語。
    /// </summary>
    [Fact]
    public void ImportFromFile_FileTooLarge_ReturnsFileTooLargeErrorAndPreservesPhrases()
    {
        var svc = new PhraseService();
        svc.Add("保留片語", "保留內容");

        string importPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");

        try
        {
            // 寫入超過 512 KB 的檔案。
            File.WriteAllText(importPath, new string('x', 513 * 1024), System.Text.Encoding.UTF8);

            PhraseService.ImportOutcome result = svc.ImportFromFile(importPath);

            Assert.False(result.Success);
            Assert.Equal(PhraseService.ImportError.FileTooLarge, result.Error);
            Assert.Equal(1, svc.Count);
            Assert.Equal("保留片語", svc.Phrases[0].Name);
        }
        finally
        {
            if (File.Exists(importPath)) File.Delete(importPath);
        }
    }

    /// <summary>
    /// 匯入後的 ExportToFile() 應輸出相同片語，驗證 Export/Import 端對端一致性。
    /// </summary>
    [Fact]
    public void ExportImport_RoundTrip_PreservesAllPhrases()
    {
        var svc = new PhraseService();
        svc.Add("片語一", "這是第一個片語");
        svc.Add("片語二", "This is phrase two");

        string exportPath = Path.Combine(Path.GetTempPath(), $"phrases_test_{Guid.NewGuid():N}.json");

        try
        {
            PhraseService.ExportOutcome exportResult = svc.ExportToFile(exportPath);
            Assert.True(exportResult.Success);

            var svc2 = new PhraseService();
            PhraseService.ImportOutcome importResult = svc2.ImportFromFile(exportPath);

            Assert.True(importResult.Success);
            Assert.Equal(2, importResult.Imported);
            Assert.Equal(2, svc2.Phrases.Count);
            Assert.Equal("片語一", svc2.Phrases[0].Name);
            Assert.Equal("片語二", svc2.Phrases[1].Name);
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }
}
