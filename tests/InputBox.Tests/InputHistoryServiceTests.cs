using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// InputHistoryService 的輸入歷程記錄與導覽邏輯測試
/// </summary>
public class InputHistoryServiceTests
{
    // ── Add ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 傳入空字串時，不應加入歷程。
    /// </summary>
    [Fact]
    public void Add_EmptyString_DoesNotAdd()
    {
        var svc = new InputHistoryService();
        svc.Add(string.Empty);

        var result = svc.Navigate(-1);

        Assert.False(result.Success);
        Assert.Equal(0, result.TotalCount);
    }

    /// <summary>
    /// 隱私模式啟用時，不應加入任何歷程記錄。
    /// </summary>
    [Fact]
    public void Add_PrivacyMode_DoesNotAdd()
    {
        var svc = new InputHistoryService
        {
            IsPrivacyMode = true
        };
        svc.Add("secret");

        var result = svc.Navigate(-1);

        Assert.Equal(0, result.TotalCount);
    }

    /// <summary>
    /// 連續加入相同文字時，第二次不應加入（防止重複紀錄）。
    /// </summary>
    [Fact]
    public void Add_DuplicateConsecutive_DoesNotAddSecond()
    {
        var svc = new InputHistoryService();
        svc.Add("hello");
        svc.Add("hello");

        Assert.Equal(1, svc.Navigate(-1).TotalCount);
    }

    /// <summary>
    /// 加入不同文字後，歷程數量應正確增加。
    /// </summary>
    [Fact]
    public void Add_DifferentTexts_AddsAll()
    {
        var svc = new InputHistoryService();
        svc.Add("a");
        svc.Add("b");
        svc.Add("c");

        Assert.Equal(3, svc.Navigate(-1).TotalCount);
    }

    /// <summary>
    /// 歷程超過最大數量時，應移除最舊的一筆。
    /// </summary>
    [Fact]
    public void Add_ExceedsMaxHistory_TrimsOldest()
    {
        var svc = new InputHistoryService(maxHistory: 3);

        svc.Add("a");
        svc.Add("b");
        svc.Add("c");
        svc.Add("d"); // 超出上限，"a" 應被移除

        // 一路向上導覽，追蹤最後一個成功結果（撞牆時 Text 為 null）
        InputHistoryService.NavigationResult last = default;
        var result = svc.Navigate(-1);
        while (!result.IsBoundaryHit)
        {
            last = result;
            result = svc.Navigate(-1);
        }

        // 最舊應為 "b"（"a" 已被裁剪移除）
        Assert.Equal("b", last.Text);
        Assert.Equal(3, last.TotalCount);
    }

    /// <summary>
    /// 一次翻頁時，應最多跳過指定筆數並停在對應的較舊歷程上。
    /// </summary>
    [Fact]
    public void NavigatePage_Up_JumpsSpecifiedCount()
    {
        var svc = new InputHistoryService();

        for (int i = 1; i <= 8; i++)
        {
            svc.Add($"item-{i}");
        }

        var result = svc.NavigatePage(-1, 5);

        Assert.True(result.Success);
        Assert.Equal("item-4", result.Text);
        Assert.Equal(4, result.CurrentIndex);
        Assert.Equal(8, result.TotalCount);
    }

    /// <summary>
    /// 從較舊歷程向下翻頁時，若超過最新邊界，應回到空白輸入狀態而不是停在中途。
    /// </summary>
    [Fact]
    public void NavigatePage_Down_PastNewest_ClearsInput()
    {
        var svc = new InputHistoryService();

        for (int i = 1; i <= 6; i++)
        {
            svc.Add($"entry-{i}");
        }

        _ = svc.NavigatePage(-1, 5);

        var result = svc.NavigatePage(1, 5);

        Assert.True(result.Success);
        Assert.True(result.IsCleared);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(-1, result.CurrentIndex);
    }

    // ── Navigate：空歷程 ────────────────────────────────────────────────────

    /// <summary>
    /// 歷程為空時，向上導覽應回傳失敗且 IsBoundaryHit 為 true。
    /// </summary>
    [Fact]
    public void Navigate_EmptyHistory_ReturnsBoundaryHit()
    {
        var svc = new InputHistoryService();
        var result = svc.Navigate(-1);

        Assert.False(result.Success);
        Assert.True(result.IsBoundaryHit);
        Assert.Equal(0, result.TotalCount);
    }

    // ── Navigate：向上（較舊）─────────────────────────────────────────────

    /// <summary>
    /// 有歷程時，向上導覽應回傳最新一筆（索引 0）。
    /// </summary>
    [Fact]
    public void Navigate_Up_FirstMove_ReturnsNewest()
    {
        var svc = new InputHistoryService();
        svc.Add("older");
        svc.Add("newer");

        var result = svc.Navigate(-1);

        Assert.True(result.Success);
        Assert.Equal("newer", result.Text);
        Assert.Equal(0, result.CurrentIndex);
    }

    /// <summary>
    /// 連續向上導覽到底部時，應停留在最舊的一筆並回傳 IsBoundaryHit。
    /// </summary>
    [Fact]
    public void Navigate_Up_PastOldest_StaysAtOldest()
    {
        var svc = new InputHistoryService();
        svc.Add("a");
        svc.Add("b");

        svc.Navigate(-1); // b (index 0)
        svc.Navigate(-1); // a (index 1)
        var result = svc.Navigate(-1); // 已在最舊，撞牆

        Assert.False(result.Success);
        Assert.True(result.IsBoundaryHit);
    }

    // ── Navigate：向下（較新）─────────────────────────────────────────────

    /// <summary>
    /// 在最舊處向下導覽應回到較新的一筆。
    /// </summary>
    [Fact]
    public void Navigate_Down_FromOldest_ReturnsNewer()
    {
        var svc = new InputHistoryService();
        svc.Add("a");
        svc.Add("b");

        svc.Navigate(-1); // b
        svc.Navigate(-1); // a（最舊）
        var result = svc.Navigate(1); // 回到 b

        Assert.True(result.Success);
        Assert.Equal("b", result.Text);
    }

    /// <summary>
    /// 從最新處向下導覽應清除輸入並回傳 IsCleared = true。
    /// </summary>
    [Fact]
    public void Navigate_Down_FromNewest_ReturnsIsCleared()
    {
        var svc = new InputHistoryService();
        svc.Add("hello");

        svc.Navigate(-1); // hello
        var result = svc.Navigate(1); // 回到空白

        Assert.True(result.Success);
        Assert.True(result.IsCleared);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(-1, result.CurrentIndex);
    }

    /// <summary>
    /// 已在空白（底部）時向下導覽，應回傳 IsBoundaryHit。
    /// </summary>
    [Fact]
    public void Navigate_Down_AtBottom_ReturnsBoundaryHit()
    {
        var svc = new InputHistoryService();
        svc.Add("hello");

        // 不先向上，直接向下（已在底部）
        var result = svc.Navigate(1);

        Assert.False(result.Success);
        Assert.True(result.IsBoundaryHit);
    }

    // ── Clear / ResetIndex ─────────────────────────────────────────────────

    /// <summary>
    /// Clear 後歷程應為空，導覽應回傳 TotalCount = 0。
    /// </summary>
    [Fact]
    public void Clear_ResetsHistoryAndIndex()
    {
        var svc = new InputHistoryService();
        svc.Add("a");
        svc.Add("b");
        svc.Navigate(-1);

        svc.Clear();

        var result = svc.Navigate(-1);
        Assert.Equal(0, result.TotalCount);
    }

    /// <summary>
    /// ResetIndex 後，應從最新一筆重新開始導覽。
    /// </summary>
    [Fact]
    public void ResetIndex_AllowsNavigationFromStart()
    {
        var svc = new InputHistoryService();
        svc.Add("a");
        svc.Add("b");

        svc.Navigate(-1); // b
        svc.Navigate(-1); // a
        svc.ResetIndex();  // 重置

        var result = svc.Navigate(-1); // 應再次從 b 開始

        Assert.True(result.Success);
        Assert.Equal("b", result.Text);
        Assert.Equal(0, result.CurrentIndex);
    }
}