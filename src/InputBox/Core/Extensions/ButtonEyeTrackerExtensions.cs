using InputBox.Core.Controls;

namespace InputBox.Core.Extensions;

/// <summary>將現有表單的回饋設定與點擊接合呼叫導向共用按鈕，不維護第二份狀態。</summary>
public static class ButtonEyeTrackerExtensions
{
    /// <summary>更新共用按鈕的描述、共享字型與表單生命週期；不重建正在進行的互動。</summary>
    public static void AttachEyeTrackerFeedback(this Button btn, string baseDescription,
        Font? regularFont, Font? boldFont, CancellationToken formCt, Action<bool>? onFocusStateChanged = null)
    {
        GetFeedbackButton(btn).ConfigureFeedback(baseDescription, regularFont, boldFont, onFocusStateChanged, formCt);
    }

    /// <summary>點擊完成後重新接合實際視線；不執行自動點擊或剪貼簿輸出。</summary>
    public static Task CompleteClickAndReGazeAsync(this Button btn) => GetFeedbackButton(btn).ReGazeAfterClickAsync();

    /// <summary>停止目前 Dwell，不影響焦點、按壓或原生色彩。</summary>
    public static void CancelDwellAnimation(this Button btn) => GetFeedbackButton(btn).CancelDwell();

    /// <summary>同步 DPI／主題更新取得的共享字型，不重設回饋生命週期。</summary>
    public static void UpdateEyeTrackerFonts(this Button btn, Font regularFont, Font? boldFont) =>
        GetFeedbackButton(btn).UpdateFeedbackFonts(regularFont, boldFont);

    private static FeedbackButton GetFeedbackButton(Button button) => button as FeedbackButton ??
        throw new ArgumentException("Eye tracker feedback requires FeedbackButton.", nameof(button));
}