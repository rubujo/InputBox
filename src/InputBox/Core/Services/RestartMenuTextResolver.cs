using InputBox.Resources;

namespace InputBox.Core.Services;

/// <summary>
/// 依待處理的重啟原因解析右鍵選單中的重啟提示文案。
/// </summary>
internal static class RestartMenuTextResolver
{
    /// <summary>
    /// 依目前的待重啟原因取得最合適的右鍵選單文字。
    /// </summary>
    /// <param name="reason">待重啟原因旗標。</param>
    /// <returns>對應情境的在地化選單文字。</returns>
    public static string GetMenuLabel(RestartPendingReason reason)
    {
        return reason switch
        {
            RestartPendingReason.AppSettings | RestartPendingReason.SystemSettings =>
                GetResourceOrFallback("Menu_ApplyAllChangesRestart", Strings.Menu_ApplyThemeRestart),

            RestartPendingReason.SystemSettings =>
                GetResourceOrFallback("Menu_ApplySystemChangesRestart", Strings.Menu_ApplyThemeRestart),

            RestartPendingReason.AppSettings => Strings.Menu_ApplyThemeRestart,

            _ => Strings.Menu_ApplyThemeRestart,
        };
    }

    /// <summary>
    /// 取得右鍵選單項目的無障礙描述。
    /// </summary>
    /// <param name="reason">待重啟原因旗標。</param>
    /// <returns>可供輔助技術播報的說明文字。</returns>
    public static string GetAccessibleDescription(RestartPendingReason reason)
    {
        return reason switch
        {
            RestartPendingReason.AppSettings | RestartPendingReason.SystemSettings =>
                GetResourceOrFallback("A11y_Restart_AllChanges_Desc", Strings.A11y_Theme_Changed_Hint),

            RestartPendingReason.SystemSettings => Strings.A11y_Theme_Changed_Hint,

            RestartPendingReason.AppSettings => Strings.Msg_RestartRequired,

            _ => Strings.Msg_RestartRequired,
        };
    }

    /// <summary>
    /// 優先讀取指定資源鍵；若資源不存在則退回指定的預設備援字串。
    /// </summary>
    private static string GetResourceOrFallback(string resourceKey, string fallback)
    {
        string? value = Strings.ResourceManager.GetString(resourceKey);

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}