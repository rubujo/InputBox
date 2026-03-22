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
    /// 快取的輸入框字型（用於資源管理）
    /// </summary>
    private Font? _inputFont;

    /// <summary>
    /// 快取的 A11y 字型（用於資源管理）
    /// </summary>
    private Font? _a11yFont;

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
    /// 取得專案統一的 A11y 放大字型
    /// </summary>
    /// <param name="dpi">目前的 DPI 數值</param>
    /// <param name="fontStyle">字型樣式，預設為 Regular</param>
    /// <returns>Font</returns>
    public static Font GetSharedA11yFont(
        int dpi,
        FontStyle fontStyle = FontStyle.Regular)
    {
        // 根據規範，預設為 11f 放大字型。
        const float baseFontSize = 11.0f,
            // 根據規範，基準 DPI 固定為 96.0f。
            baseDpi = 96.0f;

        float scale = dpi / baseDpi;

        if (scale == 0.0f)
        {
            scale = 1.0f;
        }

        // 優先使用系統訊息視窗字型作為基準。
        Font baseFont = SystemFonts.MessageBoxFont ??
            DefaultFont;

        return new Font(baseFont.FontFamily, baseFontSize * scale, fontStyle);
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

        // 1. 建立或更新輸入框專用的 28pt 大字體。
        const float inputFontSize = 28.0f;

        Font newInputFont = new(
            TBInput.Font.FontFamily,
            inputFontSize * (DeviceDpi / 96.0f),
            TBInput.Font.Style);

        // 原子化交換輸入框字型。
        Font? oldInputFont = Interlocked.Exchange(ref _inputFont, newInputFont);

        TBInput.Font = _inputFont;

        oldInputFont?.Dispose();

        // 2. 建立或更新 A11y 放大字型與粗體字型。
        Font newA11yFont = GetSharedA11yFont(DeviceDpi),
            newBoldFont = new(newA11yFont, FontStyle.Bold);

        // 抗抖動寬度鎖定（Anti-Jitter Lock）：
        // 為了防止字體加粗引發佈局抖動導致眼動儀「追逐目標」，必須預先計算 Bold 狀態的最大寬度並鎖定。
        string btnText = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');

        Size boldSize = TextRenderer.MeasureText(btnText, newBoldFont);

        // 鎖定最小寬度：測量值加上適當的內邊距補償。
        BtnCopy.MinimumSize = new Size(
            boldSize.Width + (int)(24 * (DeviceDpi / 96.0f)),
            boldSize.Height + (int)(24 * (DeviceDpi / 96.0f)));

        // 原子化交換 A11y 字型與按鈕粗體。
        Font? oldA11yFont = Interlocked.Exchange(ref _a11yFont, newA11yFont),
            oldBoldFont = Interlocked.Exchange(ref _boldBtnFont, newBoldFont);

        // 套用至按鈕。
        BtnCopy.AccessibleName = Strings.A11y_BtnCopyName;
        BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;
        BtnCopy.Text = btnText;
        BtnCopy.Font = _a11yFont;

        // 安全釋放舊資源。
        oldA11yFont?.Dispose();
        oldBoldFont?.Dispose();

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
        if (IsDisposed ||
            !IsHandleCreated)
        {
            return;
        }

        float currentScale = DeviceDpi / 96.0f;

        // 1. 測量按鈕所需寬度（考慮加粗狀態）。
        // 重置 MinimumSize 以便重新測量（避免被舊值干擾）。
        // 避免切換短語系（如英文）時，按鈕寬度仍卡在長語系（如日文）的寬度。
        BtnCopy.MinimumSize = Size.Empty;

        // 眼動儀友善：抗抖動寬度鎖定（Anti-Jitter Lock），
        // 預先測量 Bold 狀態下的文字寬度，並鎖定為 MinimumSize，防止懸停加粗時佈局抖動。
        Size boldSize = TextRenderer.MeasureText(BtnCopy.Text, _boldBtnFont ?? BtnCopy.Font);

        int requiredBtnWidth = boldSize.Width + BtnCopy.Padding.Horizontal + (int)(10 * currentScale);

        if (BtnCopy.MinimumSize.Width < requiredBtnWidth)
        {
            BtnCopy.MinimumSize = new Size(requiredBtnWidth, BtnCopy.MinimumSize.Height);
        }

        // 2. 測量輸入區（Placeholder）所需邏輯寬度。
        // 依據語系與 Placeholder 內容動態調整 MainForm 的最小寬度。
        // 為了解決 ja 等語系文字過長壓縮輸入區的問題，我們根據 Placeholder 寬度進行計算。
        // 考慮到掌機（如 ROG Ally）在 150%-200% 高縮放下，邏輯寬度不可過大以免遮擋遊戲。

        // 測量 Placeholder 在目前大字體（28pt）下的寬度。
        Size phtSize = TextRenderer.MeasureText(TBInput.PlaceholderText, TBInput.Font);

        // 轉換回邏輯像素（96 DPI）進行邊界限制。
        int measuredLogicWidth = (int)(phtSize.Width / currentScale);

        // 設定輸入區邏輯寬度上限為 280px，下限為 180px。
        // 280px 能確保即便在 200% 縮放下，總寬度亦不致於佔據 1080p 螢幕超過一半。
        int finalInputLogicWidth = Math.Clamp(measuredLogicWidth + 20, 180, 280),
            minInputAreaWidth = (int)(finalInputLogicWidth * currentScale),
            totalMinWidth = requiredBtnWidth +
                minInputAreaWidth +
                TLPHost.Padding.Horizontal +
                (int)(20 * currentScale);

        // 3. 實作視窗高度動態適應性。
        // 取「設計工作區地板（60px）」與「文字實測需求高度」的最大值。
        int clientFloorHeight = (int)(60 * currentScale);

        Size textSize = TextRenderer.MeasureText("Ag", TBInput.Font);

        int measuredTextHeight = textSize.Height + (int)(12 * currentScale),
            finalClientHeight = Math.Max(clientFloorHeight, measuredTextHeight);

        // 4. 設定視窗最終最小尺寸與工作區大小。
        // 使用 SizeFromClientSize 確保 MinimumSize 包含標題列與邊框。
        Size minWindowSize = SizeFromClientSize(new Size(totalMinWidth, finalClientHeight));

        int finalWindowHeight = Math.Max(minWindowSize.Height, (int)(80 * currentScale));

        // 更新視窗最小尺寸。
        MinimumSize = new Size(minWindowSize.Width, finalWindowHeight);

        // 如果目前寬度或高度低於測量出的地板，則強制擴張。
        // 這能確保切換至長語系（如 ja）時，UI 立即適應而非維持舊尺寸。
        if (Width < minWindowSize.Width ||
            ClientSize.Height < finalClientHeight)
        {
            Size = new Size(
                Math.Max(Width, minWindowSize.Width),
                Math.Max(Height, finalWindowHeight));

            // 佈局擴張後，立即執行智慧定位檢查，防止視窗邊緣跑出螢幕。
            ApplySmartPosition();
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
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
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

        while (!cancellationToken.IsCancellationRequested)
        {
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

                                // 填入正式訊息並更新最後處理 ID，
                                // 將 interrupt 屬性正確傳遞給 AnnouncerLabel。
                                _lblA11yAnnouncer.Announce(request.Message, request.Interrupt);

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

                // ReadAllAsync 正常結束
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[A11y] 廣播工作者發生致命錯誤，嘗試重啟：{ex.Message}");

                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch
                {
                    break;
                }
            }
        }
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
            _a11yChannel == null)
        {
            return;
        }

        long id = Interlocked.Increment(ref _currentAnnouncementId);

        // 嘗試寫入 Channel。如果已經關閉，則安靜地結束。
        _a11yChannel.Writer.TryWrite(new AnnouncementRequest(message, interrupt, id));
    }
}