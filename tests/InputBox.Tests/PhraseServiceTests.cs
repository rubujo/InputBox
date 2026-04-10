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
}
