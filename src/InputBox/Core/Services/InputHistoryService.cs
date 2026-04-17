using InputBox.Core.Configuration;

namespace InputBox.Core.Services;

/// <summary>
/// 輸入歷程記錄服務
/// </summary>
/// <param name="maxHistory">最大輸入歷程記錄資料筆數，預設值為 100</param>
internal sealed class InputHistoryService(int maxHistory = 100)
{
    /// <summary>
    /// 輸入歷程記錄
    /// </summary>
    private readonly List<string> _listHistory = [];

    /// <summary>
    /// 最大輸入歷程記錄資料筆數
    /// </summary>
    private readonly int _maxHistory = maxHistory;

    /// <summary>
    /// 目前的輸入歷程索引值（-1 代表正在輸入新內容，非歷程狀態）
    /// </summary>
    private int _currentIndex = -1;

    /// <summary>
    /// 鎖物件，用於保護輸入歷程記錄的執行緒安全
    /// </summary>
    private readonly Lock _lockObj = new();

    /// <summary>
    /// 是否為隱私模式（不紀錄新的輸入）
    /// </summary>
    public bool IsPrivacyMode { get; set; }

    /// <summary>
    /// 加入新的輸入歷程記錄
    /// </summary>
    /// <param name="text">文字</param>
    public void Add(string text)
    {
        lock (_lockObj)
        {
            if (IsPrivacyMode ||
                string.IsNullOrEmpty(text))
            {
                return;
            }

            // 截斷過長的文字，避免記憶體爆炸。
            if (text.Length > AppSettings.MaxInputLength)
            {
                text = text[..AppSettings.MaxInputLength];
            }

            // 防止連續重複輸入。
            if (_listHistory.Count > 0 &&
                _listHistory[0] == text)
            {
                _currentIndex = -1;

                return;
            }

            _listHistory.Insert(0, text);

            if (_listHistory.Count > _maxHistory)
            {
                _listHistory.RemoveAt(_listHistory.Count - 1);
            }

            // 加入後重置索引，回到「新輸入」狀態。
            _currentIndex = -1;
        }
    }

    /// <summary>
    /// 重置索引（回到最新／空白狀態）
    /// </summary>
    public void ResetIndex()
    {
        lock (_lockObj)
        {
            _currentIndex = -1;
        }
    }

    /// <summary>
    /// 清除所有輸入歷程記錄
    /// </summary>
    public void Clear()
    {
        lock (_lockObj)
        {
            _listHistory.Clear();

            _currentIndex = -1;
        }
    }

    /// <summary>
    /// 導覽結果物件。
    /// <para>封裝一次歷程導覽操作後的狀態，供呼叫端同時判斷是否成功、是否撞牆，以及要顯示的文字內容。</para>
    /// </summary>
    /// <param name="Success">本次導覽是否成功移動到另一筆歷程或成功回到空白輸入狀態。</param>
    /// <param name="Text">導覽後應呈現在輸入框的文字；失敗時可能為 null，回到空白狀態時為空字串。</param>
    /// <param name="IsBoundaryHit">是否已撞到最舊或最新邊界，代表目前方向無法再繼續移動。</param>
    /// <param name="IsCleared">是否已離開歷程瀏覽並回到新的空白輸入狀態。</param>
    /// <param name="CurrentIndex">目前歷程索引（0-based）；-1 代表不在歷程瀏覽狀態。</param>
    /// <param name="TotalCount">目前可供導覽的總歷程筆數。</param>
    public record struct NavigationResult(
        bool Success,
        string? Text,
        bool IsBoundaryHit,
        bool IsCleared,
        int CurrentIndex,
        int TotalCount);

    /// <summary>
    /// 導覽輸入歷程
    /// </summary>
    /// <param name="direction">方向 （-1：上一筆／舊，+1：下一筆／新）</param>
    /// <returns>NavigationResult</returns>
    public NavigationResult Navigate(int direction)
    {
        lock (_lockObj)
        {
            if (_listHistory.Count == 0)
            {
                return new NavigationResult(
                    Success: false,
                    Text: null,
                    IsBoundaryHit: true,
                    IsCleared: false,
                    CurrentIndex: -1,
                    TotalCount: 0);
            }

            int previousIndex = _currentIndex;

            // 索引 0 是最新的一筆，Count-1 是最舊的一筆。
            // 方向 -1 (向上) 代表要找「較舊」的記錄，因此索引必須增加。
            // 方向 +1 (向下) 代表要找「較新」的記錄，因此索引必須減少。
            int newIndex = _currentIndex - direction;

            // 邊界檢查：超過最舊的一筆，停留在最舊的一筆（頂部）
            if (newIndex >= _listHistory.Count)
            {
                newIndex = _listHistory.Count - 1;
            }

            // 邊界檢查：小於 0 代表回到空輸入狀態（底部）
            if (newIndex < 0)
            {
                if (_currentIndex == -1)
                {
                    // 已經是空輸入狀態，又按下（撞牆）
                    return new NavigationResult(
                        Success: false,
                        Text: null,
                        IsBoundaryHit: true,
                        IsCleared: false,
                        CurrentIndex: -1,
                        TotalCount: _listHistory.Count);
                }

                _currentIndex = -1;

                // 成功回到空輸入狀態。
                return new NavigationResult(
                    Success: true,
                    Text: string.Empty,
                    IsBoundaryHit: false,
                    IsCleared: true,
                    CurrentIndex: -1,
                    TotalCount: _listHistory.Count);
            }

            // 如果索引沒有變動（撞牆）
            if (newIndex == previousIndex)
            {
                return new NavigationResult(
                    Success: false,
                    Text: null,
                    IsBoundaryHit: true,
                    IsCleared: false,
                    CurrentIndex: _currentIndex,
                    TotalCount: _listHistory.Count);
            }

            // 成功移動
            _currentIndex = newIndex;

            return new NavigationResult(
                Success: true,
                Text: _listHistory[_currentIndex],
                IsBoundaryHit: false,
                IsCleared: false,
                CurrentIndex: _currentIndex,
                TotalCount: _listHistory.Count);
        }
    }

    /// <summary>
    /// 以固定步數快速翻頁導覽輸入歷程。
    /// <para>方向規則與 <see cref="Navigate(int)"/> 相同：-1 代表向較舊項目跳轉，+1 代表向較新項目跳轉。</para>
    /// </summary>
    /// <param name="direction">方向（-1：上一頁／較舊，+1：下一頁／較新）。</param>
    /// <param name="pageSize">每次要嘗試移動的最大筆數。</param>
    /// <returns>最後一次成功移動後的結果；若第一步就撞牆則回傳撞牆結果。</returns>
    public NavigationResult NavigatePage(int direction, int pageSize = 5)
    {
        if (direction is not -1 and not 1)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        NavigationResult lastSuccessfulResult = default;

        for (int i = 0; i < pageSize; i++)
        {
            NavigationResult result = Navigate(direction);

            if (!result.Success)
            {
                return i == 0 ? result : lastSuccessfulResult;
            }

            lastSuccessfulResult = result;

            if (result.IsCleared)
            {
                return result;
            }
        }

        return lastSuccessfulResult;
    }
}