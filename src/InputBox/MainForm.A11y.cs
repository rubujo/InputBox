using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using System.Diagnostics;
using System.Windows.Forms.Automation;

namespace InputBox;

// 阻擋設計工具。
partial class DesignerBlocker { };

public partial class MainForm
{
    /// <summary>
    /// A11y 廣播服務
    /// </summary>
    private AnnouncementService? _announcementService;

    /// <summary>
    /// 快取的輸入框字型（用於資源管理）
    /// </summary>
    private Font? _inputFont;

    /// <summary>
    /// 當子對話框佔用廣播頻道時為 true，此期間主視窗廣播將被靜默。
    /// </summary>
    private volatile bool _announcementSuppressed;

    /// <summary>
    /// 設定 A11y 廣播器的 LiveSetting 狀態
    /// </summary>
    /// <param name="setting">LiveSetting 設定</param>
    public void SetA11yLiveSetting(AutomationLiveSetting setting)
    {
        _announcementSuppressed = (setting == AutomationLiveSetting.Off);

        if (_lblA11yAnnouncer != null &&
            !_lblA11yAnnouncer.IsDisposed)
        {
            _lblA11yAnnouncer.LiveSetting = setting;
        }
    }

    /// <summary>
    /// 執行階段套用在地化資源與 A11y 屬性
    /// </summary>
    private void ApplyLocalization()
    {
        // 視窗基礎屬性（標題由末尾的 UpdateTitle 統一處理）。
        AccessibleName = Strings.A11y_MainFormName;
        AccessibleDescription = Strings.A11y_MainFormDesc;

        // 佈局容器。
        TLPHost.AccessibleName = Strings.A11y_Layout_Main;
        TLPHost.AccessibleDescription = Strings.A11y_Layout_Main_Desc;
        PInputHost.AccessibleName = Strings.A11y_Layout_Input;
        PInputHost.AccessibleDescription = Strings.A11y_Layout_Input_Desc;

        // 輸入控制項。
        TBInput.PlaceholderText = Strings.Pht_TBInput;
        TBInput.AccessibleName = Strings.A11y_TBInputName;
        TBInput.AccessibleDescription = Strings.A11y_TBInputDesc;
        _lblInput?.Text = Strings.A11y_TBInputName;

        // 按鈕控制項。
        BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');
        BtnCopy.AccessibleName = Strings.Btn_CopyDefault;
        BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;

        // 從全域共享快取池取得輸入框專用的 28pt 大字體（14pt * 2.0）。
        // 既然使用共享快取，則不應手動放入回收桶，由快取池統一管理生命週期。
        _inputFont = GetSharedA11yFont(
            DeviceDpi,
            TBInput.Font.Style,
            TBInput.Font.FontFamily,
            2.0f);

        // 立即同步主視窗控制項字體。
        // 安全防護：若目前的字體不是共享字體（例如由 Designer 建立的初始字體），
        // 則必須將其放入回收桶延遲處置，防止 GDI Handle 洩漏。
        if (!IsSharedFont(TBInput.Font))
        {
            AddFontToTrashCan(TBInput.Font);
        }

        TBInput.Font = _inputFont;

        // 更新按鈕字體。
        if (!IsSharedFont(BtnCopy.Font))
        {
            AddFontToTrashCan(BtnCopy.Font);
        }

        BtnCopy.Font = BtnCopy.Focused ?
            BoldBtnFont :
            A11yFont;

        // 透過擴充方法統一管理按鈕的無障礙字型與眼動儀視覺回饋。
        // onFocusStateChanged 回呼：當按鈕焦點或懸停狀態改變時，
        // 連動清除輸入框的視覺焦點狀態（原 BtnCopy_Leave 中的 TBInput 清理邏輯）。
        BtnCopy.AttachEyeTrackerFeedback(
            Strings.A11y_BtnCopyName,
            A11yFont,
            BoldBtnFont,
            _formCts?.Token ?? CancellationToken.None,
            onFocusStateChanged: (isActive) =>
            {
                if (!isActive &&
                    TBInput != null &&
                    !TBInput.IsDisposed &&
                    !TBInput.Focused)
                {
                    UpdateBorderColor(false);

                    TBInput.BackColor = Color.Empty;
                    TBInput.ForeColor = Color.Empty;
                }
            }
        );

        // 確保標題快取與在地化字串同步。
        UpdateTitlePrefix();
        UpdateTitle();

        // 根據新翻譯的文字長度，更新視窗佈局約束。
        UpdateLayoutConstraints();
    }

    /// <summary>
    /// 更新視窗佈局約束，包含 MinimumSize 與 ClientSize 的精確測量
    /// </summary>
    private void UpdateLayoutConstraints()
    {
        InputBoxLayoutManager.UpdateLayoutConstraints(
            this,
            TLPHost,
            BtnCopy,
            TBInput,
            BoldBtnFont,
            SizeFromClientSize,
            ApplySmartPosition);
    }

    /// <summary>
    /// 初始化無障礙廣播元件
    /// </summary>
    private void InitializeA11yAnnouncer()
    {
        // 建立輸入框標籤（A11y 關聯用，但不顯示文字以免干擾視覺）。
        _lblInput = new Label
        {
            Size = new Size(1, 1),
            Location = new Point(-1, -1),
            TabStop = false,
            // 防止 & 符號被解析為助記鍵，避免 A11y 文字顯示異常。
            UseMnemonic = false,
            AccessibleRole = AccessibleRole.StaticText,
            Parent = this
        };

        // 套用在地化與 A11y 屬性（覆蓋 Designer）。
        ApplyLocalization();

        _lblA11yAnnouncer = new AnnouncerLabel
        {
            Name = "LblA11yAnnouncer",
            Visible = true,
            // 使用 Dock 置於底部，模擬標準狀態列，這是 NVDA 最信任的區域。
            Dock = DockStyle.Bottom,
            Height = 1,
            // 讓背景色與視窗一致，達成視覺上的隱形。
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            TabStop = false,
            Parent = this
        };

        _lblA11yAnnouncer.BringToFront();

        _announcementService = new AnnouncementService(AnnounceOnUiAsync);
    }

    /// <summary>
    /// 在 UI 執行緒實際執行 A11y 廣播
    /// </summary>
    /// <param name="message">廣播訊息內容。</param>
    /// <param name="interrupt">是否允許中斷較舊播報。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>非同步作業。</returns>
    private async Task AnnounceOnUiAsync(string message, bool interrupt, CancellationToken cancellationToken)
    {
        await this.SafeInvokeAsync(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested ||
                    IsDisposed ||
                    _announcementSuppressed ||
                    _lblA11yAnnouncer == null)
                {
                    return;
                }

                // 將 interrupt 屬性正確傳遞給 AnnouncerLabel。
                // 若使用者停用廣播中斷（WCAG 2.2.4），則強制以排隊模式播報。
                _lblA11yAnnouncer.Announce(message, interrupt && AppSettings.Current.A11yInterruptEnabled);
            }
            catch (ObjectDisposedException)
            {

            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "[A11y] UI 執行緒廣播失敗");

                Debug.WriteLine($"[A11y] UI 執行緒廣播失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 發送無障礙廣播訊息
    /// </summary>
    /// <param name="message">要朗讀的訊息。</param>
    /// <param name="interrupt">是否中斷之前的排隊內容（預設為 false）。對於游標移動等高頻率操作，建議設為 true。</param>
    internal void AnnounceA11y(
        string message,
        bool interrupt = false)
    {
        if (string.IsNullOrEmpty(message) ||
            IsDisposed ||
            _announcementSuppressed)
        {
            return;
        }

        _announcementService?.Enqueue(message, interrupt);
    }
}