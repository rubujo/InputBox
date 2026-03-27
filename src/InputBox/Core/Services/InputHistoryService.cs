using InputBox.Core.Configuration;

namespace InputBox.Core.Services;

/// <summary>
/// 輸入歷程記錄服務
/// </summary>
/// <param name="maxHistory">最大輸入歷程記錄資料筆數，預設值為 100</param>
public class InputHistoryService(int maxHistory = 100)
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
            if (text.Length > AppSettings.MaxHistoryEntryLength)
            {
                text = text[..AppSettings.MaxHistoryEntryLength];
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
    /// 導覽結果物件
    /// </summary>
    /// <param name="Success">是否成功</param>
    /// <param name="Text">文字</param>
    /// <param name="IsBoundaryHit">是否已撞到邊界</param>
    /// <param name="IsCleared">是否已清除</param>
    /// <param name="CurrentIndex">目前索引（0-based）</param>
    /// <param name="TotalCount">總筆數</param>
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
}