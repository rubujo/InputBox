using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Resources;
using System.Diagnostics;
using System.Threading.Channels;
using System.Windows.Forms.Automation;

namespace InputBox;

// 阻擋設計工具。
partial class DesignerBlocker { };

public partial class MainForm
{
    /// <summary>
    /// A11y 廣播訊息佇列
    /// </summary>
    private Channel<AnnouncementRequest>? _a11yChannel;

    /// <summary>
    /// 廣播背景工作者的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _a11yCts;

    /// <summary>
    /// 目前廣播的序號（用於處理高頻率廣播的丟棄邏輯）
    /// </summary>
    private long _currentAnnouncementId = 0;

    /// <summary>
    /// 在 UI 執行緒中最後一次完成廣播的 ID（用於防止非同步排程導致的舊訊息覆蓋新訊息）
    /// </summary>
    private long _lastProcessedAnnouncementId = 0;

    /// <summary>
    /// A11y 廣播請求模型
    /// </summary>
    private record AnnouncementRequest(string Message, bool Interrupt, long Id);

    /// <summary>
    /// 設定 A11y 廣播器的 LiveSetting 狀態
    /// </summary>
    /// <param name="setting">LiveSetting 設定</param>
    public void SetA11yLiveSetting(AutomationLiveSetting setting)
    {
        if (_lblA11yAnnouncer != null &&
            !_lblA11yAnnouncer.IsDisposed)
        {
            _lblA11yAnnouncer.LiveSetting = setting;
        }
    }

    /// <summary>
    /// 取得專案統一的 A11y 放大字型（11f）
    /// </summary>
    /// <param name="dpi">目前的 DPI 數值</param>
    /// <param name="fontStyle">字型樣式，預設為 Regular</param>
    /// <returns>Font</returns>
    public static Font GetSharedA11yFont(
        int dpi,
        FontStyle fontStyle = FontStyle.Regular)
    {
        // 根據規範，預設為 11f 放大字型。
        const float BaseFontSize = 11.0f,
            // 根據規範，基準 DPI 固定為 96.0f。
            BaseDpi = 96.0f;

        float scale = dpi / BaseDpi;

        // 優先使用系統訊息視窗字型作為基準。
        Font baseFont = SystemFonts.MessageBoxFont ?? DefaultFont;

        return new Font(baseFont.FontFamily, BaseFontSize * scale, fontStyle);
    }

    /// <summary>
    /// 快取的 A11y 字型（用於資源管理）
    /// </summary>
    private Font? _a11yFont;

    /// <summary>
    /// 執行階段套用在地化資源與 A11y 屬性。
    /// 此方法用於覆蓋 Designer 中的硬編碼值，確保多語系正確性。
    /// </summary>
    private void ApplyLocalization()
    {
        // 視窗基礎屬性。
        Text = Strings.App_Title;
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

        // 建立或更新 A11y 放大字型。
        Font newA11yFont = GetSharedA11yFont(DeviceDpi);
        Font newBoldFont = new(newA11yFont, FontStyle.Bold);

        // 先處置舊的字型資源，防止 GDI 洩漏。
        _a11yFont?.Dispose();
        _boldBtnFont?.Dispose();

        _a11yFont = newA11yFont;
        _boldBtnFont = newBoldFont;

        // 按鈕。
        BtnCopy.AccessibleName = Strings.A11y_BtnCopyName;
        BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;
        BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');

        // 根據規範，套用統一的 A11y 共享字型。
        BtnCopy.Font = _a11yFont;

        // 紀錄原始顏色，以便在滑鼠移出或失去焦點時還原。
        // 當從高對比模式切換回一般模式時，必須重新整理快取，以確保顏色符合目前主題。
        if (!SystemInformation.HighContrast)
        {
            _originalBtnBackColor = BtnCopy.BackColor;
            _originalBtnForeColor = BtnCopy.ForeColor;
        }
    }

    /// <summary>
    /// 初始化無障礙廣播元件。
    /// </summary>
    private void InitializeA11yAnnouncer()
    {
        // 建立輸入框標籤（A11y 關聯用，但不顯示文字以免干擾視覺）。
        _lblInput = new Label
        {
            Size = new Size(1, 1),
            Location = new Point(-1, -1),
            TabStop = false,
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
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText,
            TabStop = false,
            Parent = this
        };

        _lblA11yAnnouncer.BringToFront();

        // 建立一個單一寫入者、單一讀取者的 Channel。
        _a11yChannel = Channel.CreateUnbounded<AnnouncementRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            // 可能從不同執行緒呼叫 AnnounceA11y。
            SingleWriter = false
        });

        _a11yCts = new CancellationTokenSource();

        // 啟動背景工作者。
        _ = ProcessA11yAnnouncementsAsync(_a11yCts.Token);
    }

    /// <summary>
    /// 處理 A11y 廣播訊息的背景工作
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Task</returns>
    private async Task ProcessA11yAnnouncementsAsync(CancellationToken cancellationToken)
    {
        if (_a11yChannel == null)
        {
            return;
        }

        try
        {
            await foreach (AnnouncementRequest request in _a11yChannel.Reader.ReadAllAsync(cancellationToken))
            {
                // 每個循環開始前檢查。
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // 第一重檢查：如果此訊息標記為可中斷，且目前已有更新的訊息在 ID 佇列中，則跳過。
                if (request.Interrupt)
                {
                    long currentLatestId = Interlocked.Read(ref _currentAnnouncementId);

                    if (request.Id < currentLatestId)
                    {
                        // 既然有更新的 ID，我們優先查看 Channel 是否還有新訊息。
                        // 如果有，就跳過目前這筆；如果沒有（代表 currentLatestId 可能還在傳輸中），則處理這筆以防漏報。
                        if (_a11yChannel.Reader.TryPeek(out _))
                        {
                            continue;
                        }
                    }
                }

                try
                {
                    // 進入 UI 執行緒進行正式廣播。
                    await this.SafeInvokeAsync(async () =>
                    {
                        try
                        {
                            // 進入 UI 執行緒後的即時檢查。
                            if (cancellationToken.IsCancellationRequested ||
                                IsDisposed ||
                                _lblA11yAnnouncer == null)
                            {
                                return;
                            }

                            // 第二重檢查（最終防護）：
                            // 檢查此訊息的 ID 是否小於等於 UI 執行緒最後一次「處理完成」的 ID。
                            // 這能防止多個非同步排程在 UI 執行緒中競爭時，舊訊息因 Task.Delay 結束而覆蓋新訊息。
                            if (request.Id <= _lastProcessedAnnouncementId)
                            {
                                return;
                            }

                            // 增加延遲（200ms）以避開系統音效（如 Asterisk）的音訊高峰。
                            await Task.Delay(200, cancellationToken);

                            if (cancellationToken.IsCancellationRequested ||
                                IsDisposed)
                            {
                                return;
                            }

                            // 再次檢查 ID，確保在 Delay 期間沒有更新的訊息已先發布。
                            if (request.Id <= _lastProcessedAnnouncementId)
                            {
                                return;
                            }

                            // 填入正式訊息並更新最後處理 ID。
                            _lblA11yAnnouncer.Announce(request.Message);

                            _lastProcessedAnnouncementId = request.Id;
                        }
                        catch (ObjectDisposedException)
                        {

                        }
                        catch (OperationCanceledException)
                        {

                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[A11y] UI 執行緒廣播失敗：{ex.Message}");
                        }
                    });

                    // 檢查中斷狀態。
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // 給予足夠的報讀時間，防止下一條訊息立即覆蓋。
                    // 對於中斷型廣播，可以縮短等待時間。
                    await Task.Delay(request.Interrupt ? 100 : 300, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[A11y] 廣播處理發生異常：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[A11y] 廣播工作者發生致命錯誤：{ex.Message}");
        }
    }

    /// <summary>
    /// 發送無障礙廣播訊息（具備隊列機制與丟棄機制，確保在高頻率操作下不會造成語音堆疊）
    /// </summary>
    /// <param name="message">要朗讀的訊息。</param>
    /// <param name="interrupt">是否中斷之前的排隊內容（預設為 false）。對於游標移動等高頻率操作，建議設為 true。</param>
    internal void AnnounceA11y(
        string message,
        bool interrupt = false)
    {
        if (string.IsNullOrEmpty(message) ||
            IsDisposed ||
            _a11yChannel == null)
        {
            return;
        }

        long id = Interlocked.Increment(ref _currentAnnouncementId);

        // 嘗試寫入 Channel。如果已經關閉，則安靜地結束。
        _a11yChannel.Writer.TryWrite(new AnnouncementRequest(message, interrupt, id));
    }
}