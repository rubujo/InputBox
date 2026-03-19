using System.Diagnostics;
using System.Windows.Forms.Automation;

namespace InputBox.Core.Controls;

/// <summary>
/// 專門用於無障礙廣播的 Label
/// </summary>
/// <remarks>
/// 利用 WinForms 內建的 RaiseLiveRegionChanged 支援，確保與 NVDA 等現代螢幕閱讀器的最佳相容性。
/// </remarks>
internal sealed class AnnouncerLabel : Label
{
    /// <summary>
    /// 最後一次廣播的訊息，用於處理重複訊息的特殊情況
    /// </summary>
    private string _lastMessage = string.Empty;

    /// <summary>
    /// 切換旗標，用於在重複訊息時添加零寬字元以觸發 UIA 事件
    /// </summary>
    private bool _toggleFlag = false;

    /// <summary>
    /// AnnouncerLabel
    /// </summary>
    public AnnouncerLabel()
    {
        // 使用 StatusBar 角色，NVDA 預設會自動監控此區域。
        AccessibleRole = AccessibleRole.StatusBar;
        // 設定 LiveSetting 為 Polite，確保訊息會在語音空閒時報讀。
        LiveSetting = AutomationLiveSetting.Polite;
        Visible = true;
        Size = new Size(20, 20);
        Location = new Point(0, 0);
        Text = string.Empty;
        BackColor = Color.Empty;
        ForeColor = Color.Empty;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams createParams = base.CreateParams;

            // 確保有適當的樣式以利 UIA 識別。
            return createParams;
        }
    }

    /// <summary>
    /// 清除內容
    /// </summary>
    public void Clear()
    {
        Text = "\u00A0";
        AccessibleName = "\u00A0";

        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
    }

    /// <summary>
    /// 發送無障礙廣播
    /// </summary>
    /// <param name="message">訊息內容</param>
    /// <param name="interrupt">是否中斷目前的廣播</param>
    public void Announce(string message, bool interrupt = false)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        Debug.WriteLine($"[A11y 廣播] {message}");

        string finalMsg = message;

        if (message == _lastMessage)
        {
            _toggleFlag = !_toggleFlag;

            finalMsg = _toggleFlag ?
                message + "\u200B" :
                message + "\u200C";
        }

        _lastMessage = message;

        // 根據是否打斷，動態切換 LiveRegion 的緊急程度。
        LiveSetting = interrupt ?
            AutomationLiveSetting.Assertive :
            AutomationLiveSetting.Polite;

        Text = finalMsg;
        AccessibleName = finalMsg;

        // 發送標準通知。
        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);

        try
        {
            // 觸發內建的 LiveRegion 變更事件。
            // 這會自動引發 UIA LiveRegionChanged 事件（0x4e08）。
            if (!IsDisposed &&
                IsHandleCreated)
            {
                // 嘗試觸發 UIA 通知。
                AccessibilityObject.RaiseLiveRegionChanged();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[A11y] 廣播時發生非預期錯誤：{ex.GetType().Name} - {message}");
        }
    }
}