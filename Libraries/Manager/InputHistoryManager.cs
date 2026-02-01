namespace InputBox.Libraries.Manager;

/// <summary>
/// 輸入歷程記錄管理器
/// </summary>
/// <param name="maxHistory">最大輸入歷程記錄資料筆數，預設值為 100</param>
public class InputHistoryManager(int maxHistory = 100)
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
    /// 設定一個合理的上限，例如 10,000 字
    /// </summary>
    private const int MaxHistoryEntryLength = 10000;

    /// <summary>
    /// 加入新的輸入歷程記錄
    /// </summary>
    /// <param name="text">文字</param>
    public void Add(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // 截斷過長的文字，避免記憶體爆炸。
        if (text.Length > MaxHistoryEntryLength)
        {
            text = text[..MaxHistoryEntryLength];
        }

        // 防止連續重複輸入。
        // 如果使用者連續按兩次複製，雖然現在 UI 會擋，但邏輯層多一層保護會更好。
        if (_listHistory.Count > 0 &&
            _listHistory.Last() == text)
        {
            ResetIndex();

            return;
        }

        _listHistory.Add(text);

        if (_listHistory.Count > _maxHistory)
        {
            _listHistory.RemoveAt(0);
        }

        // 加入後重置索引，回到「新輸入」狀態。
        ResetIndex();
    }

    /// <summary>
    /// 重置索引（回到最新／空白狀態）
    /// </summary>
    public void ResetIndex()
    {
        _currentIndex = -1;
    }

    /// <summary>
    /// 清除所有輸入歷程記錄
    /// </summary>
    public void Clear()
    {
        _listHistory.Clear();

        ResetIndex();
    }

    /// <summary>
    /// 導覽結果物件
    /// </summary>
    /// <param name="Success">是否成功</param>
    /// <param name="Text">文字</param>
    /// <param name="IsBoundaryHit">是否已撞到邊界</param>
    /// <param name="IsCleared">是否已清除</param>
    public record struct NavigationResult(bool Success, string? Text, bool IsBoundaryHit, bool IsCleared);

    /// <summary>
    /// 導覽輸入歷程
    /// </summary>
    /// <param name="direction">方向 （-1：上一筆／舊，+1：下一筆／新）</param>
    /// <returns>NavigationResult</returns>
    public NavigationResult Navigate(int direction)
    {
        if (_listHistory.Count == 0)
        {
            return new NavigationResult(Success: false, Text: null, IsBoundaryHit: true, IsCleared: false);
        }

        int previousIndex = _currentIndex,
            newIndex = _currentIndex;

        if (newIndex < 0)
        {
            // 按「上」：載入最新一筆。
            if (direction < 0)
            {
                newIndex = _listHistory.Count - 1;
            }
            else
            {
                // 按「下」：保持原狀（撞牆）。
                return new NavigationResult(Success: false, Text: null, IsBoundaryHit: true, IsCleared: false);
            }
        }
        else
        {
            newIndex += direction;
        }

        // 邊界檢查：最舊的一筆（頂部）。
        if (newIndex < 0)
        {
            newIndex = 0;
        }

        // 邊界檢查：最新的一筆之後（底部／回到新輸入）。
        if (newIndex >= _listHistory.Count)
        {
            _currentIndex = -1;

            // 超過最新一筆時，清除輸入，但視為 ActionFail（震動）。
            return new NavigationResult(Success: true, Text: string.Empty, IsBoundaryHit: true, IsCleared: true);
        }

        // 如果索引沒有變動（撞牆）。
        if (newIndex == previousIndex)
        {
            return new NavigationResult(Success: false, Text: null, IsBoundaryHit: true, IsCleared: false);
        }

        // 成功移動。
        _currentIndex = newIndex;

        return new NavigationResult(Success: true, Text: _listHistory[_currentIndex], IsBoundaryHit: false, IsCleared: false);
    }
}