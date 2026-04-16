namespace InputBox.Core.Services;

/// <summary>
/// 右鍵選單建構輔助器，集中處理根選單初始化與重啟提示項目注入
/// </summary>
internal static class ContextMenuBuilder
{
    /// <summary>
    /// 確保存在根層 ContextMenuStrip，若不存在則建立新實例
    /// </summary>
    /// <param name="existing">既有選單實例。</param>
    /// <param name="accessibleName">選單無障礙名稱。</param>
    /// <param name="accessibleDescription">選單無障礙描述。</param>
    /// <returns>可用的根層 ContextMenuStrip。</returns>
    public static ContextMenuStrip EnsureRoot(
        ContextMenuStrip? existing,
        string accessibleName,
        string accessibleDescription)
    {
        return existing ?? new ContextMenuStrip
        {
            AccessibleName = accessibleName,
            AccessibleDescription = accessibleDescription
        };
    }

    /// <summary>
    /// 依需求注入根層重啟項目，並避免重複加入。
    /// </summary>
    /// <param name="menu">目標選單。</param>
    /// <param name="isRestartPending">是否仍有待重新啟動套用的變更。</param>
    /// <param name="pendingPrefix">待套用前綴文字。</param>
    /// <param name="restartLabel">重啟項目標籤。</param>
    /// <param name="onRestart">點擊重啟項目時執行的動作。</param>
    public static void EnsureRestartItem(
        ContextMenuStrip menu,
        bool isRestartPending,
        string pendingPrefix,
        string restartLabel,
        Action onRestart,
        string? restartAccessibleDescription = null)
    {
        const string restartItemName = "PendingRestartItem";
        const string restartSeparatorName = "PendingRestartSeparator";

        foreach (ToolStripItem existing in menu.Items.Cast<ToolStripItem>().Where(static item =>
                     item.Name == restartItemName ||
                     item.Name == restartSeparatorName).ToList())
        {
            menu.Items.Remove(existing);
            existing.Dispose();
        }

        if (!isRestartPending)
        {
            return;
        }

        ToolStripMenuItem tsmiRestart = new()
        {
            Name = restartItemName,
            Text = $"{pendingPrefix} {restartLabel}",
            AccessibleName = restartLabel,
            AccessibleDescription = string.IsNullOrWhiteSpace(restartAccessibleDescription) ?
                restartLabel :
                restartAccessibleDescription
        };
        tsmiRestart.Click += (s, e) => onRestart();

        menu.Items.Insert(0, tsmiRestart);
        menu.Items.Insert(1, new ToolStripSeparator { Name = restartSeparatorName });
    }

    /// <summary>
    /// 選取第一個可操作選單項目並產生播報字串
    /// </summary>
    /// <param name="menu">目標選單。</param>
    /// <param name="checkedLabel">已勾選狀態文字。</param>
    /// <param name="uncheckedLabel">未勾選狀態文字。</param>
    /// <param name="announcement">輸出播報字串。</param>
    /// <returns>若成功選取且產生播報字串則回傳 true。</returns>
    public static bool TrySelectFirstVisibleItem(
        ContextMenuStrip menu,
        string checkedLabel,
        string uncheckedLabel,
        out string announcement)
    {
        foreach (ToolStripItem item in menu.Items)
        {
            if (!item.Enabled ||
                !item.Visible ||
                item is ToolStripSeparator)
            {
                continue;
            }

            item.Select();

            string? name = item.AccessibleName ?? item.Text,
                desc = item.AccessibleDescription;

            if (item is ToolStripMenuItem menuItem &&
                menuItem.CheckOnClick)
            {
                string status = menuItem.Checked ?
                    checkedLabel :
                    uncheckedLabel;

                name = $"{name}, {status}";
            }

            announcement = string.IsNullOrEmpty(desc) ?
                (name ?? string.Empty) :
                $"{name}. {desc}";

            return !string.IsNullOrEmpty(announcement);
        }

        announcement = string.Empty;

        return false;
    }
}