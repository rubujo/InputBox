namespace InputBox.Core.Controls;

/// <summary>保留原生 Latest 外觀，並提供自訂回饋結束時的原生色彩快取重設。</summary>
public class FeedbackButton : Button
{
    /// <summary>透過 WinForms 既有主題刷新路徑終止舊色動畫，不改變樣式、HWND 或版面。</summary>
    internal void ResetNativeColorTransition() => base.OnSystemColorsChanged(EventArgs.Empty);
}