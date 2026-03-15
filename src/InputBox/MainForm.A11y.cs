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
    /// 取得專案統一的 A11y 放大字型（11f）。
    /// </summary>
    /// <param name="dpi">目前的 DPI 數值</param>
    /// <param name="fontStyle">字型樣式，預設為 Regular</param>
    /// <returns>Font</returns>
    public static Font GetSharedA11yFont(int dpi, FontStyle fontStyle = FontStyle.Regular)
    {
        // 根據規範，預設為 11f 放大字型。
        const float BaseFontSize = 11.0f;

        float scale = dpi / 96.0f;

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
        _a11yFont?.Dispose();
        _a11yFont = GetSharedA11yFont(DeviceDpi);

        // 按鈕。
        BtnCopy.AccessibleName = Strings.A11y_BtnCopyName;
        BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;
        BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');
        
        // 根據規範，套用統一的 A11y 共享字型。
        BtnCopy.Font = _a11yFont;
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

                // 檢查序號：如果目前已有最新的廣播在排隊且 interrupt 為 true，則放棄舊的廣播。
                // 邏輯優化：我們只檢查目前讀取到的訊息 ID 是否顯著落後於最新分配的 ID。
                // 如果落後且此訊息標記為可中斷，則直接跳過，處理下一筆更新的內容。
                if (request.Interrupt &&
                    request.Id < Interlocked.Read(ref _currentAnnouncementId))
                {
                    // 只有在 Channel 中仍有資料可讀時才跳過，否則仍需處理最後一筆。
                    if (_a11yChannel.Reader.TryPeek(out _))
                    {
                        continue;
                    }
                }

                try
                {
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

                            // 再次確認序號（防止在排程到 UI 執行緒期間又有更新的訊息進入）。
                            if (request.Interrupt &&
                                request.Id < Interlocked.Read(ref _currentAnnouncementId) &&
                                _a11yChannel.Reader.TryPeek(out _))
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

                            // 填入正式訊息。
                            _lblA11yAnnouncer.Announce(request.Message);
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
    /// 上一次發送的訊息內容，用於重複訊息處理。
    /// </summary>
    private string _lastAnnouncedMessage = string.Empty;

    /// <summary>
    /// 是否使用 ZWSP (Zero Width Space) 作為交替字元。
    /// </summary>
    private bool _useZwsp = false;

    /// <summary>
    /// 發送無障礙廣播訊息（具備隊列機制與丟棄機制，確保在高頻率操作下不會造成語音堆疊）。
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

        // 重複訊息處理：
        // 如果連發兩次完全一樣的文字，UIA 可能會因為內容未變而不報讀。
        // 此時在結尾交替附加 \u200B（ZWSP）或 \u200C（ZWNJ）來強迫系統識別為變動。
        string finalMessage = message;

        if (string.Equals(message, _lastAnnouncedMessage, StringComparison.Ordinal))
        {
            _useZwsp = !_useZwsp;

            finalMessage = message + (_useZwsp ? "\u200B" : "\u200C");
        }

        _lastAnnouncedMessage = message;

        long id = Interlocked.Increment(ref _currentAnnouncementId);

        // 嘗試寫入 Channel。如果已經關閉，則安靜地結束。
        _a11yChannel.Writer.TryWrite(new AnnouncementRequest(finalMessage, interrupt, id));
    }
}