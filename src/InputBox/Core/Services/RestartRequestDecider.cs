namespace InputBox.Core.Services;

/// <summary>
/// 定義重啟要求的觸發來源，用於決定是否需要再次向使用者確認。
/// </summary>
internal enum RestartRequestSource
{
    /// <summary>
    /// 因設定變更而要求重新啟動；應讓使用者自行選擇是否立即重啟。
    /// </summary>
    SettingChange,

    /// <summary>
    /// 使用者已從選單主動要求重新啟動；不應再重複詢問。
    /// </summary>
    ManualMenu,
}

/// <summary>
/// 集中處理不同重啟入口的確認策略，避免手動重啟仍被二次詢問。
/// </summary>
internal static class RestartRequestDecider
{
    /// <summary>
    /// 判斷目前的重啟要求是否應繼續執行。
    /// </summary>
    /// <param name="source">重啟要求來源。</param>
    /// <param name="confirmationDialog">需要確認時顯示的對話框委派。</param>
    /// <returns>若應繼續重啟則回傳 true。</returns>
    public static bool ShouldRestart(
        RestartRequestSource source,
        Func<DialogResult> confirmationDialog)
    {
        ArgumentNullException.ThrowIfNull(confirmationDialog);

        return source == RestartRequestSource.ManualMenu ||
               confirmationDialog() == DialogResult.Yes;
    }
}