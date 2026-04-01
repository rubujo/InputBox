using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
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
        if (IsDisposed ||
            !IsHandleCreated)
        {
            return;
        }

        float currentScale = DeviceDpi / AppSettings.BaseDpi;

        // 測量按鈕所需寬度（考慮加粗狀態）。
        // 重置 MinimumSize 以便重新測量（避免被舊值干擾）。
        // 避免切換短語系（如英文）時，按鈕寬度仍卡在長語系（如日文）的寬度。
        BtnCopy.MinimumSize = Size.Empty;

        // 眼動儀友善：抗抖動寬度鎖定（Anti-Jitter Lock），
        // 預先測量 Bold 狀態下的文字寬度，並鎖定為 MinimumSize，防止懸停加粗時佈局抖動。
        Size boldSize = TextRenderer.MeasureText(
            BtnCopy.Text,
            BoldBtnFont ?? BtnCopy.Font);

        int requiredBtnWidth = boldSize.Width + BtnCopy.Padding.Horizontal + (int)(10 * currentScale);

        if (BtnCopy.MinimumSize.Width < requiredBtnWidth)
        {
            BtnCopy.MinimumSize = new Size(requiredBtnWidth, BtnCopy.MinimumSize.Height);
        }

        // 測量輸入區（Placeholder）所需邏輯寬度。
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

        // 實作視窗高度動態適應性。
        // 取「設計工作區地板（60px）」與「文字實測需求高度」的最大值。
        int clientFloorHeight = (int)(60 * currentScale);

        Size textSize = TextRenderer.MeasureText("Ag", TBInput.Font);

        int measuredTextHeight = textSize.Height + (int)(12 * currentScale),
            finalClientHeight = Math.Max(clientFloorHeight, measuredTextHeight);

        // 設定視窗最終最小尺寸與工作區大小。
        // 使用 SizeFromClientSize 確保 MinimumSize 包含標題列與邊框。
        Size minWindowSize = SizeFromClientSize(new Size(totalMinWidth, finalClientHeight));

        int finalWindowHeight = Math.Max(minWindowSize.Height, (int)(80 * currentScale));

        Rectangle workArea = Screen.FromControl(this).WorkingArea;

        // 超高縮放保護：限制最小尺寸不超過目前工作區（保留 40px 邊界）。
        int maxFitWidth = Math.Max(1, workArea.Width - 40),
            maxFitHeight = Math.Max(1, workArea.Height - 40);

        int clampedMinWidth = Math.Min(minWindowSize.Width, maxFitWidth),
            clampedMinHeight = Math.Min(finalWindowHeight, maxFitHeight);

        // 更新視窗最小尺寸。
        MinimumSize = new Size(clampedMinWidth, clampedMinHeight);

        // 若目前尺寸超出可視區，或低於縮放地板，則同步修正。
        if (Width < clampedMinWidth ||
            Height < clampedMinHeight ||
            Width > maxFitWidth ||
            Height > maxFitHeight)
        {
            // 邊界檢查：確保最小值不超過最大值，防止 Math.Clamp 拋出異常。
            int finalMaxW = Math.Max(clampedMinWidth, maxFitWidth),
                finalMaxH = Math.Max(clampedMinHeight, maxFitHeight);

            Size = new Size(
                Math.Clamp(Width, clampedMinWidth, finalMaxW),
                Math.Clamp(Height, clampedMinHeight, finalMaxH));

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

        // 建立一個單一寫入者、單一讀取者的 Channel。
        _a11yChannel = Channel.CreateUnbounded<AnnouncementRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            // 可能從不同執行緒呼叫 AnnounceA11y。
            SingleWriter = false
        });

        _a11yCts = new CancellationTokenSource();

        // 啟動背景工作者。
        Task.Run(() => ProcessA11yAnnouncementsAsync(_a11yCts.Token)).SafeFireAndForget();
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
                        // 在進入 UI 執行緒前先執行 Audio Ducking 避讓延遲，防止阻塞 UI 執行緒。
                        // 根據規範，統一使用 AudioDuckingDelayMs，並加入生理抖動的高斯延遲（μ=200, σ=30），模擬人類反應時間。
                        int duckingDelay = AppSettings.AudioDuckingDelayMs;

                        await Task.Delay(HumanoidRandom.NextDelay(duckingDelay, 60), cancellationToken);

                        // 進入 UI 執行緒進行正式廣播。
                        await this.SafeInvokeAsync(() =>
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
                                // 這能防止多個非同步排程在 UI 執行緒中競爭時，舊訊息覆蓋新訊息。
                                if (request.Id <= _lastProcessedAnnouncementId)
                                {
                                    return;
                                }

                                // 填入正式訊息並更新最後處理 ID，
                                // 將 interrupt 屬性正確傳遞給 AnnouncerLabel。
                                // 若使用者停用廣播中斷（WCAG 2.2.4），則強制以排隊模式播報。
                                _lblA11yAnnouncer.Announce(request.Message, request.Interrupt && AppSettings.Current.A11yInterruptEnabled);

                                _lastProcessedAnnouncementId = request.Id;
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

                        // 檢查中斷狀態。
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        // 給予足夠的報讀時間，防止下一條訊息立即覆蓋。
                        // 對於中斷型廣播，可以縮短等待時間，並加入微小的高斯擾動。
                        int waitDelay = request.Interrupt ? 100 : 300;

                        await Task.Delay(HumanoidRandom.NextDelay(waitDelay, 40), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[A11y] 廣播處理發生異常");

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
                LoggerService.LogException(ex, "[A11y] 廣播工作者發生致命錯誤");

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