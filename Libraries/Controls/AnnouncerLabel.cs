namespace InputBox.Libraries.Controls;

/// <summary>
/// 專門用於無障礙廣播的 Label
/// </summary>
/// <remarks>
/// 因 AccessibilityNotifyClients 為 protected 方法，
/// 需透過繼承 Label 來公開此功能。
/// </remarks>
internal sealed class AnnouncerLabel : Label
{
    /// <summary>
    /// 發送無障礙廣播
    /// </summary>
    /// <param name="message">訊息內容</param>
    public void Announce(string message)
    {
        // 如果訊息跟上次一樣，先清除，確保 NameChange 事件能被觸發。
        if (string.Equals(Text, message, StringComparison.Ordinal))
        {
            Text = string.Empty;
        }

        Text = message;

        // 通知輔助科技（AT）此控制項名稱已變更，觸發朗讀。
        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
    }
}
