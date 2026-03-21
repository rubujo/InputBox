using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Services;
using InputBox.Core.Interop;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;

namespace InputBox;

// 阻擋設計工具。
partial class DesignerBlocker { };

public partial class MainForm
{
    private async void MainForm_Activated(object sender, EventArgs e)
    {
        try
        {
            // 確保視窗還原時，文字方塊直接取得焦點，不用再點一次。
            // 使用 Interlocked 防止 Activated 瞬間多次觸發引發的焦點競爭。
            if (Interlocked.CompareExchange(ref _isProcessingActivated, 1, 0) == 0)
            {
                try
                {
                    try
                    {
                        // 加上 50 毫秒延遲：讓 Windows 視窗放大動畫跑完，HWND 完全準備好。
                        // 否則 TextBox 的 BackColor 會被系統底層強制洗白。
                        await Task.Delay(50, _formCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // 視窗關閉時安全退出。
                        return;
                    }

                    if (IsDisposed ||
                        TBInput == null ||
                        TBInput.IsDisposed)
                    {
                        return;
                    }

                    if (TBInput.CanFocus)
                    {
                        TBInput.Focus();

                        // 手動補發 Enter 事件，強制更新黑底與邊框視覺
                        TBInput_Enter(TBInput, EventArgs.Empty);
                    }
                }
                finally
                {
                    _isProcessingActivated = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] MainForm_Activated 處理失敗：{ex.Message}");
        }
    }

    private async void MainForm_Shown(object sender, EventArgs e)
    {
        try
        {
            // Windows 平板模式會在 Shown 後強制最大化。
            // 延遲讓系統完成動畫與視窗狀態更新，再強制還原。
            try
            {
                await Task.Delay(AppSettings.Current.WindowRestoreDelay, _formCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            // 強制還原視窗，避免在 Windows 桌面（平板模式）下自動最大化。
            // 對「Windows 遊戲：全螢幕體驗」（Xbox 全螢幕體驗）不受影響，一樣會自動最大化。
            User32.ShowWindow(Handle, User32.ShowWindowCommand.Restore);

            // 初始化 GamepadController。
            // 使用 SafeFireAndForget 並傳入額外處理動作，以確保啟動失敗時能獲得 A11y 通知。
            InitializeGamepadControllerAsync()
                .SafeFireAndForget(ex =>
                {
                    AnnounceA11y(Strings.Err_GamepadInitFail);

                    Debug.WriteLine($"[啟動] 控制器初始化失敗：{ex.Message}");
                });

            // 啟動時自動取得焦點。
            if (!IsDisposed &&
                TBInput != null &&
                TBInput.CanFocus)
            {
                TBInput.Focus();
            }

            // A11y 廣播：歡迎訊息與操作提示。
            // 延遲 100ms 避開視窗開啟音效。
            try
            {
                await Task.Delay(100, _formCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            AnnounceA11y($"{Strings.App_Title}. {Strings.A11y_MainFormDesc}");

            // 延後播報狀態摘要（隱私模式與目前快速鍵）。
            try
            {
                await Task.Delay(1500, _formCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            string privacyStatus = AppSettings.Current.IsPrivacyMode ?
                Strings.A11y_PrivacyMode_On :
                Strings.A11y_PrivacyMode_Off;

            string hotkeyStr = GlobalHotKeyService.GetHotKeyDisplayString();

            AnnounceA11y(string.Format(Strings.A11y_Startup_Status, privacyStatus, hotkeyStr));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                Strings.Err_Title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            // 監聽顏色、無障礙設定以及一般設定。
            if (e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.Accessibility ||
                e.Category == UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    // 根據規範，系統無障礙設定（如高對比、大字型、動畫開關）變更時，需重新套用在地化資源與佈局。
                    ApplyLocalization();

                    // 即時同步不透明度防護（高對比模式下強制 100%）。
                    UpdateOpacity();

                    // 同步目前焦點狀態的邊框。
                    UpdateBorderColor(TBInput.Focused);

                    if (TBInput.Focused)
                    {
                        // 重新觸發 Enter 邏輯以同步最新的高對比主題配色。
                        TBInput_Enter(TBInput, EventArgs.Empty);
                    }

                    // A11y 廣播：告知使用者環境設定已同步。
                    if (SystemInformation.HighContrast)
                    {
                        AnnounceA11y(Strings.A11y_Opacity_HighContrast);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] SystemEvents_UserPreferenceChanged 處理失敗：{ex.Message}");
        }
    }

    private void TBInput_Enter(object sender, EventArgs e)
    {
        try
        {
            // Tab 鍵進入時，中止正在進行的警示動畫。
            _alertCts?.Cancel();

            // 如果正在擷取快速鍵，則不執行一般的進入變色邏輯，保留擷取模式的視覺狀態。
            if (_isCapturingHotkey != 0)
            {
                return;
            }

            // 更新邊框狀態。
            UpdateBorderColor(true);

            // 判斷是否為高對比模式，如果系統已經開啟高對比，
            // 就完全尊重系統設定，不要自己改顏色。
            if (SystemInformation.HighContrast)
            {
                // 高對比模式配色。
                TBInput.BackColor = SystemColors.Highlight;
                TBInput.ForeColor = SystemColors.HighlightText;

                return;
            }

            // 強烈靜態視覺回饋：主題感知的明暗反轉。
            if (TBInput.IsDarkModeActive())
            {
                // 深色模式下，反轉為亮色背景。
                TBInput.BackColor = Color.White;
                TBInput.ForeColor = Color.Black;
            }
            else
            {
                // 淺色模式下，反轉為深色背景。
                TBInput.BackColor = Color.Black;
                TBInput.ForeColor = Color.White;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] TBInput_Enter 處理失敗：{ex.Message}");
        }
    }

    private void TBInput_Leave(object sender, EventArgs e)
    {
        try
        {
            // 區分滑鼠與鍵盤的焦點轉移。
            if (BtnCopy != null &&
                BtnCopy.Focused)
            {
                // 如果 Control.MouseButtons 不是 None，代表使用者正「按住」滑鼠左鍵。
                // 這是滑鼠點擊瞬間搶走焦點，我們攔截並保留黑色背景，防止刺眼閃爍。
                if (MouseButtons != MouseButtons.None)
                {
                    return;
                }

                // 如果是 None，代表是按 Tab 鍵過來的。
                // 必須放行，讓 TBInput 乖乖清除黑色背景，避免雙重焦點！
            }

            _alertCts?.Cancel();

            // 如果正在擷取快速鍵時失去焦點，則取消擷取模式。
            if (_isCapturingHotkey != 0)
            {
                RestoreUIFromCaptureMode();

                // A11y 廣播：取消提示。
                AnnounceA11y(Strings.A11y_Capture_Cancelled);
            }

            // 還原邊框厚度。
            UpdateBorderColor(false);

            // 還原為預設顏色，觸發原生主題引擎自動套用正確背景。
            TBInput.BackColor = Color.Empty;
            TBInput.ForeColor = Color.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] TBInput_Leave 處理失敗：{ex.Message}");
        }
    }

    private void TBInput_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            HandleKeyDown(e);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] TBInput_KeyDown 處理失敗：{ex.Message}");
        }
    }

    private void BtnCopy_MouseEnter(object sender, EventArgs e)
    {
        try
        {
            // 防止背景誤觸（Prevent Background Midas Touch）。
            // 如果目前的主視窗不是活躍狀態（失去焦點退到背景了），
            // 則完全不觸發 Hover 視覺，更不啟動眼控 Dwell 進度條動畫。
            if (ActiveForm != this)
            {
                return;
            }

            _isBtnHovered = true;

            // 尊重目前的鍵盤焦點狀態。
            ApplyButtonHoverStyle(isKeyboardFocus: BtnCopy.Focused);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] BtnCopy_MouseEnter 處理失敗：{ex.Message}");
        }
    }

    private void BtnCopy_MouseLeave(object sender, EventArgs e)
    {
        try
        {
            _isBtnHovered = false;

            RestoreButtonDefaultStyle();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] BtnCopy_MouseLeave 處理失敗：{ex.Message}");
        }
    }

    private void BtnCopy_Enter(object sender, EventArgs e)
    {
        try
        {
            // 當按鈕取得焦點（例如透過 Tab 鍵）時，套用視覺強化。
            ApplyButtonHoverStyle(isKeyboardFocus: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] BtnCopy_Enter 處理失敗：{ex.Message}");
        }
    }

    private void BtnCopy_Leave(object sender, EventArgs e)
    {
        try
        {
            // 失去鍵盤焦點時，檢查滑鼠／視線是否還在上面。
            if (_isBtnHovered)
            {
                // 如果視線／滑鼠還在，主動套用「溫和的 Hover 樣式」，
                // 藉此洗掉強烈的 Cyan 邊框與粗體，避免 Focus 視覺殘留！
                ApplyButtonHoverStyle(isKeyboardFocus: false);
            }
            else
            {
                // 如果滑鼠也不在上面，就徹底還原成預設狀態。
                RestoreButtonDefaultStyle(force: true);
            }

            // 如果焦點離開按鈕，且「沒有回到輸入框」（例如使用者 Alt+Tab 切換視窗），
            // 則由按鈕幫忙清除輸入框的視覺焦點狀態。
            if (TBInput != null &&
                !TBInput.Focused)
            {
                UpdateBorderColor(false);

                TBInput.BackColor = Color.Empty;
                TBInput.ForeColor = Color.Empty;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] BtnCopy_Leave 處理失敗：{ex.Message}");
        }
    }

    private void BtnCopy_Paint(object? sender, PaintEventArgs e)
    {
        if (BtnCopy == null)
        {
            return;
        }

        float scale = DeviceDpi / BaseDpi;
        bool isDark = BtnCopy.IsDarkModeActive();

        // 1. 先繪製焦點邊框（Focus Border／Focus Ring）。
        if (BtnCopy.Focused ||
            _isBtnHovered)
        {
            int borderThickness = (int)Math.Max(3, 3 * scale),
                inset = (int)Math.Max(2, 2 * scale);

            // A11y 統一邏輯：淺色模式表單（黑底控制項）用 Cyan；深色模式表單（白底控制項）用 RoyalBlue。
            using Pen borderPen = new(
                SystemInformation.HighContrast ?
                    SystemColors.HighlightText :
                    (isDark ? Color.RoyalBlue : Color.Cyan),
                borderThickness);

            e.Graphics.DrawRectangle(
                borderPen,
                inset,
                inset,
                BtnCopy.Width - (inset * 2) - 1,
                BtnCopy.Height - (inset * 2) - 1);
        }

        // 2. 後繪製注視進度條（Dwell Feedback）。
        if (_dwellProgress > 0)
        {
            int barHeight = (int)(6 * scale),
                barWidth = (int)(BtnCopy.Width * _dwellProgress);

            Rectangle barRect = new(0, BtnCopy.Height - barHeight, barWidth, barHeight);

            if (SystemInformation.HighContrast)
            {
                using Brush barBrush = new SolidBrush(SystemColors.HighlightText);

                e.Graphics.FillRectangle(barBrush, barRect);
            }
            else
            {
                // A11y 絕對同步：淺色（黑底）= DarkOrange／Orange；深色（白底）= Firebrick／OrangeRed。
                Color baseColor = isDark ? Color.Firebrick : Color.DarkOrange,
                    hatchColor = isDark ? Color.OrangeRed : Color.Orange;

                using Brush bgBrush = new SolidBrush(baseColor);
                using Brush hatchBrush = new HatchBrush(
                    HatchStyle.BackwardDiagonal,
                    hatchColor,
                    Color.Transparent);

                e.Graphics.FillRectangle(bgBrush, barRect);
                e.Graphics.FillRectangle(hatchBrush, barRect);
            }
        }
    }

    private async void BtnCopy_Click(object sender, EventArgs e)
    {
        try
        {
            // 永遠保證點擊按鈕後，焦點會回到輸入框（即使在冷卻中），
            // 這確保了焦點不會卡在按鈕上。
            if (TBInput.CanFocus &&
                !TBInput.Focused)
            {
                TBInput.Focus();

                await Task.Yield();
            }

            // 邏輯冷卻攔截。
            // 取代 Enabled = false，眼控使用者的 MouseEnter 狀態不會被中斷。
            if (_isActionCooldown)
            {
                return;
            }

            // 保留 Hover 視覺，僅重置動畫與進度。
            // 取代 RestoreButtonDefaultStyle(force: true);，
            // 我們只中斷正在進行的 Dwell 動畫並將進度條歸零，保留黑底與粗體，提供點擊確認感。
            Interlocked.Increment(ref _animationId);

            _dwellProgress = 0f;

            BtnCopy.Invalidate();

            // 執行非同步動作（包含成功複製，或是空字串時的 1 秒冷卻等待）。
            await PerformCopyAsync();

            // Gaze Re-engagement（視線重新接合）。
            // 動作或冷卻結束後，檢查眼控／滑鼠是否還停留在按鈕上。
            // 如果還在，主動重新觸發 Hover 樣式與 Dwell 動畫！
            if (BtnCopy != null &&
                !BtnCopy.IsDisposed)
            {
                Point cursorPos = BtnCopy.PointToClient(Cursor.Position);

                // 檢查游標是否還在按鈕的範圍內。
                if (BtnCopy.ClientRectangle.Contains(cursorPos))
                {
                    _isBtnHovered = true;

                    // 如果目前已經有鍵盤焦點，就維持強烈視覺；否則才套用溫和的 Hover。
                    ApplyButtonHoverStyle(isKeyboardFocus: BtnCopy.Focused);
                }
                else
                {
                    // 如果動作結束時，滑鼠確實已經移開了，才徹底洗掉樣式
                    _isBtnHovered = false;

                    // 讓方法內部自己去判斷（!BtnCopy.Focused && !_isBtnHovered）。
                    // 這樣如果使用者是用 Tab 鍵停留在按鈕上（Focused = true），就不會被洗掉黑色背景！
                    RestoreButtonDefaultStyle();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] 複製按鈕點擊處理失敗：{ex.Message}");

            AnnounceA11y(string.Format(Strings.A11y_Background_Error, ex.Message));
        }
    }

    /// <summary>
    /// 套用按鈕高亮與加粗樣式
    /// </summary>
    /// <param name="isKeyboardFocus">是否為鍵盤焦點觸發（若是則給予最強烈視覺，且不啟動進度條動畫）</param>
    private void ApplyButtonHoverStyle(bool isKeyboardFocus)
    {
        // 如果按鈕已停用，不執行樣式變更，避免干擾 Reset 流程。
        if (BtnCopy == null ||
            !BtnCopy.Enabled)
        {
            return;
        }

        long id = Interlocked.Increment(ref _animationId);

        if (_originalBtnPadding == default)
        {
            _originalBtnPadding = BtnCopy.Padding;
        }

        if (isKeyboardFocus)
        {
            // 強烈視覺：鍵盤焦點（Tab 鍵切換過來）。
            // 目的：讓使用者明確知道按下 Enter 會觸發此按鈕。

            // 打斷並徹底重置 Dwell 進度。
            // 既然已經由鍵盤接管焦點，就必須清除跑到一半的進度，避免視覺殘留卡死！
            _dwellProgress = 0f;

            if (SystemInformation.HighContrast)
            {
                BtnCopy.BackColor = SystemColors.Highlight;
                BtnCopy.ForeColor = SystemColors.HighlightText;
            }
            else
            {
                if (BtnCopy.IsDarkModeActive())
                {
                    // 深色模式：白底黑字（最強烈對比）。
                    BtnCopy.BackColor = Color.White;
                    BtnCopy.ForeColor = Color.Black;
                }
                else
                {
                    // 淺色模式：黑底白字（最強烈對比）。
                    BtnCopy.BackColor = Color.Black;
                    BtnCopy.ForeColor = Color.White;
                }
            }

            // 形狀補償：字體加粗。
            if (_boldBtnFont != null)
            {
                BtnCopy.Font = _boldBtnFont;
            }

            // 強烈的焦點邊框。
            BtnCopy.FlatAppearance.BorderColor = Color.Cyan;
            // 粗邊框。
            BtnCopy.FlatAppearance.BorderSize = 2;

            BtnCopy.AccessibleDescription = $"{Strings.A11y_BtnCopyDesc} ({Strings.A11y_State_Focused})";
        }
        else
        {
            // 溫和視覺：滑鼠／眼控 Hover。
            // 目的：提示游標位置，但不搶走鍵盤焦點的風采。

            if (SystemInformation.HighContrast)
            {
                BtnCopy.BackColor = SystemColors.HotTrack;
                BtnCopy.ForeColor = SystemColors.HighlightText;
            }
            else
            {
                if (BtnCopy.IsDarkModeActive())
                {
                    // 深色模式：溫和的深灰色。
                    BtnCopy.BackColor = Color.FromArgb(60, 60, 60);
                    BtnCopy.ForeColor = Color.White;
                }
                else
                {
                    // 淺色模式：溫和的淺灰色。
                    BtnCopy.BackColor = Color.FromArgb(220, 220, 220);
                    BtnCopy.ForeColor = Color.Black;
                }
            }

            // 不加粗：維持無障礙標準字體。
            if (_a11yFont != null)
            {
                BtnCopy.Font = _a11yFont;
            }

            // 弱化或隱藏邊框，使其融入背景。
            BtnCopy.FlatAppearance.BorderColor = BtnCopy.BackColor;
            // 細邊框。
            BtnCopy.FlatAppearance.BorderSize = 1;
            BtnCopy.AccessibleDescription = $"{Strings.A11y_BtnCopyDesc} ({Strings.A11y_State_Hover})";

            // 動畫回饋：僅在視線/滑鼠進入時播放。
            BtnCopy.RunDwellAnimationAsync(
                    id,
                    () => Interlocked.Read(ref _animationId),
                    (p) => _dwellProgress = p,
                    ct: _formCts.Token)
                .SafeFireAndForget();
        }

        BtnCopy.Padding = new Padding(0);

        // 保險起見，強制要求按鈕立刻重繪，把歸零後的狀態畫出來。
        BtnCopy.Invalidate();
    }

    /// <summary>
    /// 還原按鈕原始樣式
    /// </summary>
    /// <param name="force">是否強制還原（忽略焦點檢查）</param>
    private void RestoreButtonDefaultStyle(bool force = false)
    {
        Interlocked.Increment(ref _animationId);

        _dwellProgress = 0f;

        if (BtnCopy == null)
        {
            return;
        }

        // 狀態守衛：只有在確實失焦且無懸停，或強制還原時執行。
        if (force ||
            (!BtnCopy.Focused && !_isBtnHovered))
        {
            // 還原為預設顏色，觸發原生主題引擎自動套用正確背景。
            BtnCopy.BackColor = Color.Empty;
            BtnCopy.ForeColor = Color.Empty;

            if (_a11yFont != null)
            {
                BtnCopy.Font = _a11yFont;
            }

            if (_originalBtnPadding != default)
            {
                BtnCopy.Padding = _originalBtnPadding;
            }

            BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;
            BtnCopy.FlatAppearance.BorderColor = Color.Empty;
            BtnCopy.FlatAppearance.BorderSize = 1;
        }

        BtnCopy.Invalidate();
    }

    /// <summary>
    /// 執行核心複製邏輯與歷程紀錄
    /// </summary>
    /// <returns>Task</returns>
    private async Task PerformCopyAsync()
    {
        // 在最開頭建立快照。
        string strTextToCopy = TBInput.Text;

        try
        {
            // 使用快照檢查。
            if (string.IsNullOrEmpty(strTextToCopy))
            {
                // 啟動邏輯冷卻。
                _isActionCooldown = true;

                // 發出警告音。
                FeedbackService.PlaySound(SystemSounds.Beep);

                // 觸覺回饋：錯誤操作。
                await VibrateAsync(VibrationPatterns.ActionFail);

                if (IsDisposed)
                {
                    return;
                }

                // 視覺回饋（雙重提示）。
                FlashAlertAsync().SafeFireAndForget();

                AnnounceA11y(Strings.A11y_No_Text_To_Copy);

                // 異步等待 1 秒冷卻，涵蓋閃爍動畫週期。
                // 期間所有的點擊都會被 BtnCopy_Click 攔截，但不會破壞 UI 狀態。
                await Task.Delay(1000);

                _isActionCooldown = false;

                return;
            }

            // 在停用按鈕之前，先主動把焦點移走（移回輸入框）。
            // 這樣螢幕閱讀器就會平順地唸出「輸入文字……」，而不會因為按鈕突然消失而發生焦點錯亂。
            if (BtnCopy.Focused)
            {
                TBInput.Focus();
            }

            BtnCopy.Enabled = false;

            bool isCopySuccess = await ClipboardService.TrySetTextAsync(strTextToCopy);

            if (IsDisposed ||
                BtnCopy == null)
            {
                return;
            }

            if (!isCopySuccess)
            {
                FeedbackService.PlaySound(SystemSounds.Hand);

                await VibrateAsync(VibrationPatterns.ActionFail);

                if (IsDisposed)
                {
                    return;
                }

                // 視覺回饋（寫入失敗警告）。
                FlashAlertAsync().SafeFireAndForget();

                BtnCopy.Text = Strings.Msg_CopyFail;
                BtnCopy.Enabled = true;

                // 確保按鈕重新啟用後，如果目前視窗還是活著的，將焦點還給輸入框或按鈕。
                if (ContainsFocus)
                {
                    // 失敗後將焦點還給輸入框讓使用者修改，是很好的體驗。
                    TBInput.Focus();
                }

                AnnounceA11y(Strings.Msg_CopyFail);

                await ResetButtonStateAsync();

                return;
            }

            // 複製成功後的處理。
            // 加入輸入歷程記錄（僅在成功後加入，確保歷程內容的正確性）。
            _historyService.Add(strTextToCopy);

            FeedbackService.PlaySound(SystemSounds.Asterisk);

            await VibrateAsync(VibrationPatterns.CopySuccess);

            if (IsDisposed)
            {
                return;
            }

            BtnCopy.Text = Strings.Msg_Copied;
            BtnCopy.AccessibleDescription = Strings.Msg_Copied;

            // 語音最佳化：將兩條相關訊息合併為一條發送，確保在焦點切換前使用者能聽到完整結果。
            AnnounceA11y($"{Strings.Msg_Copied}. {Strings.A11y_Returning}");

            // 複製後清除。
            TBInput.Clear();

            // 呼叫具備安全性檢查的返回方法，並傳入 false 以避免重複廣播 A11y_Returning。
            await ReturnToPreviousWindowAsync(announce: false);

            if (IsDisposed ||
                BtnCopy == null)
            {
                return;
            }

            // 恢復按鈕狀態。
            await ResetButtonStateAsync();
        }
        catch (Exception ex)
        {
            // 捕捉所有異常，包括 ExternalException 和其他可能的錯誤。

            FeedbackService.PlaySound(SystemSounds.Hand);

            await VibrateAsync(VibrationPatterns.ActionFail);

            // 視覺回饋（系統錯誤警告）。
            FlashAlertAsync().SafeFireAndForget();

            // 如果視窗還在，顯示錯誤。
            if (!IsDisposed &&
                BtnCopy != null)
            {
                BtnCopy.Text = Strings.Msg_CopyFail;
                BtnCopy.AccessibleDescription = Strings.Msg_CopyFail;
                // 確保按鈕被重新啟用，否則使用者無法重試。
                BtnCopy.Enabled = true;

                // 恢復按鈕狀態。
                await ResetButtonStateAsync();
            }

            Debug.WriteLine($"複製到剪貼簿失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理自定義快捷按鍵與歷史導覽
    /// </summary>
    /// <param name="e">KeyEventArgs</param>
    private void HandleKeyDown(KeyEventArgs e)
    {
        // Alt + B：不複製直接返回前一個視窗（與遊戲手把 LB + RB + B 對等）。
        if (e.Alt &&
            e.KeyCode == Keys.B)
        {
            HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();

            e.SuppressKeyPress = true;

            return;
        }

        // Esc：清除文字。
        if (e.KeyCode == Keys.Escape)
        {
            // UX 最佳化：如果文字框已經是空的，直接返回前一個視窗（隱藏視窗）。
            if (string.IsNullOrEmpty(TBInput.Text))
            {
                HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();
            }
            else
            {
                ClearInput();
            }

            e.SuppressKeyPress = true;

            return;
        }

        // ↑：上一筆。
        if (e.KeyCode == Keys.Up)
        {
            // 取得游標目前所在的行數（0 代表第一行）。
            int currentLine = TBInput.GetLineFromCharIndex(TBInput.SelectionStart);

            // 只有在第一行時，按「上」才觸發歷程記錄。
            if (currentLine == 0)
            {
                NavigateHistory(-1);

                e.SuppressKeyPress = true;
            }

            // 若不在第一行，就不攔截，讓 Windows 原生的多行游標往上移動。
            return;
        }

        // ↓：下一筆。
        if (e.KeyCode == Keys.Down)
        {
            // 取得游標目前所在的行數。 
            int currentLine = TBInput.GetLineFromCharIndex(TBInput.SelectionStart),
                // 取得文字方塊總共的行數（最後一行的 Index）。
                totalLines = TBInput.GetLineFromCharIndex(TBInput.TextLength);

            // 只有在最後一行時，按「下」才觸發歷程記錄。
            if (currentLine == totalLines)
            {
                NavigateHistory(+1);

                e.SuppressKeyPress = true;
            }

            return;
        }
    }

    /// <summary>
    /// 清除輸入框內容
    /// </summary>
    private void ClearInput()
    {
        // 如果已經是空的，不需要大動作清除。
        if (string.IsNullOrEmpty(TBInput.Text))
        {
            FeedbackService.PlaySound(SystemSounds.Beep);

            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

            return;
        }

        FeedbackService.PlaySound(SystemSounds.Beep);

        VibrateAsync(VibrationPatterns.ClearInput).SafeFireAndForget();

        TBInput.Clear();

        // 重置 InputHistoryManager 索引值。
        _historyService.ResetIndex();

        AnnounceA11y(Strings.Msg_InputCleared);
    }

    /// <summary>
    /// 內部註冊全域快速鍵
    /// </summary>
    /// <returns>是否成功註冊全域快速鍵</returns>
    private bool RegisterHotKeyInternal()
    {
        bool isOkay = GlobalHotKeyService.RegisterShowInputHotkey(Handle);

        if (!isOkay)
        {
            string currentHotkeyStr = GlobalHotKeyService.GetHotKeyDisplayString();

            // A11y 廣播：告知快速鍵註冊失敗。
            AnnounceA11y(Strings.Err_HotkeyRegFail_Brief);

            // 音效回饋。
            FeedbackService.PlaySound(SystemSounds.Hand);

            MessageBox.Show(
                string.Format(Strings.Err_HotkeyRegFail, $"[{currentHotkeyStr}]"),
                caption: Strings.Wrn_Title,
                buttons: MessageBoxButtons.OK,
                icon: MessageBoxIcon.Exclamation);
        }

        return isOkay;
    }

    /// <summary>
    /// 導覽輸入歷程記錄
    /// </summary>
    /// <param name="direction">導航方向，-1 表示向上，1 表示向下</param>
    private void NavigateHistory(int direction)
    {
        InputHistoryService.NavigationResult navigationResult = _historyService.Navigate(direction);

        // 處理震動與視覺（邊界或錯誤）。
        if (navigationResult.IsBoundaryHit)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);

            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

            // 視覺回饋（提示已經到底了）。
            FlashAlertAsync().SafeFireAndForget();

            AnnounceA11y(direction < 0 ? Strings.A11y_Nav_Oldest : Strings.A11y_Nav_Newest, interrupt: true);
        }

        // 處理文字更新。
        if (navigationResult.IsCleared)
        {
            TBInput.Clear();

            // 如果不是因為撞牆才清空，才獨立報讀（避免雙重語音）。
            if (!navigationResult.IsBoundaryHit)
            {
                AnnounceA11y(Strings.Msg_InputCleared, interrupt: true);
            }
        }
        else if (navigationResult.Success &&
            navigationResult.Text != null)
        {
            TBInput.Text = navigationResult.Text;
            // 游標移到最後。
            TBInput.SelectionStart = TBInput.Text.Length;
            TBInput.ScrollToCaret();

            // 強制朗讀切換後的歷程記錄內容，並包含索引資訊。
            // 索引 + 1 是為了讓使用者聽到 1-based 的數字。
            AnnounceA11y(string.Format(
                Strings.A11y_History_Navigation,
                navigationResult.CurrentIndex + 1,
                navigationResult.TotalCount,
                navigationResult.Text), interrupt: true);
        }
    }

    /// <summary>
    /// 嘗試顯示 Windows 觸控式鍵盤
    /// </summary>
    private void ShowTouchKeyboard()
    {
        if ((DateTime.UtcNow - _lastTouchKeyboardOpened).TotalMilliseconds < 500)
        {
            return;
        }

        _lastTouchKeyboardOpened = DateTime.UtcNow;

        // 確保文字方塊取得焦點。
        if (TBInput.CanFocus &&
            !TBInput.Focused)
        {
            TBInput.Focus();
        }

        // A11y 廣播。
        AnnounceA11y(Strings.A11y_Opening_Keyboard);

        try
        {
            if (Interlocked.CompareExchange(ref _isShowingTouchKeyboard, 1, 0) != 0)
            {
                return;
            }

            bool isTouchKeyboardOpened = TouchKeyboardService.TryOpen();

            if (isTouchKeyboardOpened)
            {
                FeedbackService.PlaySound(SystemSounds.Asterisk);
            }
            else
            {
                // 找不到檔案時的處理。
                FeedbackService.PlaySound(SystemSounds.Hand);

                VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                // 錯誤時觸發視覺閃爍。
                FlashAlertAsync().SafeFireAndForget();

                AnnounceA11y(Strings.Err_TouchKeyboardNotFound);
            }
        }
        catch
        {
            FeedbackService.PlaySound(SystemSounds.Hand);

            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

            // 錯誤時觸發視覺閃爍。
            FlashAlertAsync().SafeFireAndForget();

            AnnounceA11y(Strings.Err_TouchKeyboardNotFound);
        }
        finally
        {
            // 改為呼叫獨立的非同步方法，並加上 _ = 捨棄回傳值以防堵編譯警告。
            _ = ResetTouchKeyboardFlagAsync();
        }
    }

    /// <summary>
    /// 將視窗呼叫至前景準備輸入
    /// </summary>
    public void ShowForInput()
    {
        // 捕捉目前視窗。
        _windowFocusService.CaptureCurrentWindow();

        // 顯示視窗。
        Show();

        // 還原視窗。
        User32.ShowWindow(Handle, User32.ShowWindowCommand.Restore);

        // 帶至前方。
        BringToFront();

        // 啟用視窗。
        Activate();

        // A11y 宣告：告知使用者視窗已開啟並就緒。
        AnnounceA11y(Strings.A11y_MainFormName);

        FeedbackService.PlaySound(SystemSounds.Asterisk);

        VibrateAsync(VibrationPatterns.ShowInput).SafeFireAndForget();
    }

    /// <summary>
    /// 還原焦點至先前擷取的視窗
    /// </summary>
    /// <param name="announce">是否公告還原焦點的訊息</param>
    /// <returns>Task</returns>
    private async Task ReturnToPreviousWindowAsync(bool announce = true)
    {
        // 前置檢查：如果目標視窗已經消失，直接進入導航流程（它會發送正確的失敗廣播）。
        // 這能防止先播報「正在返回...」隨後立刻接「錯誤/視窗已關閉」的語音衝突。
        if (!_navigationService.CanNavigateBack)
        {
            await _navigationService.NavigateBackAsync(_gamepadController, msg => AnnounceA11y(msg), _formCts.Token);

            return;
        }

        if (announce)
        {
            // A11y 廣播：告知使用者正在返回前一個視窗。
            AnnounceA11y(Strings.A11y_Returning);
        }

        // 呼叫導航服務，並傳入目前的控制器實例以進行安全檢查與震動。
        await _navigationService.NavigateBackAsync(_gamepadController, msg => AnnounceA11y(msg), _formCts.Token);

        // 如果切換後，目前視窗仍是前景視窗，代表切換失敗。
        if (User32.ForegroundWindow == Handle)
        {
            // 讓視窗閃爍以提醒使用者手動切換。
            WindowFocusService.FlashWindow(Handle);
        }
    }

    /// <summary>
    /// 執行視覺警示閃爍效果
    /// </summary>
    /// <returns>Task</returns>
    private async Task FlashAlertAsync()
    {
        // 狀態與生命週期守衛。
        if (IsDisposed ||
            !IsHandleCreated ||
            Interlocked.CompareExchange(ref _isFlashing, 1, 0) != 0)
        {
            return;
        }

        // 建立本次動畫專用的中斷權杖。
        _alertCts?.Cancel();
        _alertCts = CancellationTokenSource.CreateLinkedTokenSource(_formCts.Token);

        CancellationToken token = _alertCts.Token;

        try
        {
            float scale = DeviceDpi / BaseDpi;

            int normalThickness = (int)Math.Max(2, 2 * scale),
                pulseThickness = (int)Math.Max(7, 7 * scale);

            // 決定警示色。
            // A11y 強化：修正選色邏輯以對齊反轉後的控制項背景。
            // 淺色模式（黑底）：用 DarkOrange（8.3:1）；深色模式（白底）：用 Firebrick（5.8:1）。
            Color alertColor = SystemInformation.HighContrast ?
                SystemColors.HighlightText :
                (TBInput.IsDarkModeActive() ? Color.Firebrick : Color.DarkOrange);

            // 快照目前全域字型，確保在動畫循環中不會誤處置它們。
            Font? snapshotA11yFont = _a11yFont,
                snapshotBoldFont = _boldBtnFont;

            void ApplyAlertVisuals(float intensity)
            {
                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                bool isDark = TBInput.IsDarkModeActive();

                if (SystemInformation.HighContrast)
                {
                    PInputHost.BackColor = intensity > 0.5f ?
                        alertColor :
                        SystemColors.Highlight;
                }
                else
                {
                    // 1. 核心修正：閃爍基色改為純淨底色（黑／白），避免與高飽和焦點色（Cyan/RoyalBlue）插值產生髒濁色。
                    Color pureBase = isDark ?
                        Color.White :
                        Color.Black;

                    int rB = (int)(pureBase.R + (alertColor.R - pureBase.R) * intensity),
                        gB = (int)(pureBase.G + (alertColor.G - pureBase.G) * intensity),
                        bB = (int)(pureBase.B + (alertColor.B - pureBase.B) * intensity);

                    Color flashColor = Color.FromArgb(255, rB, gB, bB);

                    PInputHost.BackColor = flashColor;

                    // 2. 遞歸背景更新（Recursive Visual Sync）：同步更新輸入框編輯區，確保視覺一致性。
                    TBInput.BackColor = flashColor;

                    // 3. 警示作用域隔離（Alert Scoping）：按鈕即使具備焦點，也禁止參與同步背景閃爍。
                    // 原有的按鈕閃爍邏輯已移除，按鈕將維持其靜態焦點樣式。
                }
            }

            // 嚴格遵守光敏性癲癇防護與使用者偏好：
            // 若使用者在系統層級關閉了動畫效果（UIEffectsEnabled 為 false），
            // 則不進行循環閃爍，改為一次性的「長脈衝（Static Pulse）」回饋。
            if (!SystemInformation.UIEffectsEnabled)
            {
                this.SafeInvoke(() => ApplyAlertVisuals(1.0f));

                // 維持一段較長時間（800ms）讓低視能使用者感知狀態，隨後恢復。
                await Task.Delay(800, token);

                return;
            }

            int totalDuration = AppSettings.PhotoSafeFrequencyMs,
                delayMs = 16;

            long startTime = Stopwatch.GetTimestamp();

            while (true)
            {
                token.ThrowIfCancellationRequested();

                long elapsedTicks = Stopwatch.GetTimestamp() - startTime;

                double elapsedMs = (double)elapsedTicks / Stopwatch.Frequency * 1000.0;

                if (elapsedMs >= totalDuration)
                {
                    break;
                }

                // 使用 AppSettings.PhotoSafeFrequencyMs 定義的正弦波週期（1Hz）。
                double angle = elapsedMs / AppSettings.PhotoSafeFrequencyMs * 2.0 * Math.PI - (Math.PI / 2.0);

                float intensity = (float)((Math.Sin(angle) + 1.0) / 2.0);

                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                this.SafeInvoke(() => ApplyAlertVisuals(intensity));

                await Task.Delay(delayMs, token);
            }
        }
        catch (OperationCanceledException)
        {

        }
        finally
        {
            Interlocked.Exchange(ref _isFlashing, 0);

            if (_alertCts != null)
            {
                try
                {
                    _alertCts.Dispose();
                }
                catch
                {

                }

                _alertCts = null;
            }

            if (!IsDisposed &&
                IsHandleCreated)
            {
                this.SafeInvoke(() =>
                {
                    // 嚴格遵守自身焦點狀態。
                    // 移除之前的 isActiveState 混合判斷，只看 TBInput.Focused。
                    bool isTextBoxFocused = TBInput.Focused;

                    UpdateBorderColor(isTextBoxFocused);

                    // 還原輸入框顏色。
                    if (isTextBoxFocused)
                    {
                        if (SystemInformation.HighContrast)
                        {
                            TBInput.BackColor = SystemColors.Highlight;
                            TBInput.ForeColor = SystemColors.HighlightText;
                        }
                        else
                        {
                            if (TBInput.IsDarkModeActive())
                            {
                                TBInput.BackColor = Color.White;
                                TBInput.ForeColor = Color.Black;
                            }
                            else
                            {
                                TBInput.BackColor = Color.Black;
                                TBInput.ForeColor = Color.White;
                            }
                        }
                    }
                    else
                    {
                        // 只有當焦點真的離開了這個輸入區域（例如點擊了其他視窗），才徹底還原。
                        TBInput.BackColor = Color.Empty;
                        TBInput.ForeColor = Color.Empty;
                    }
                });
            }
        }
    }

    /// <summary>
    /// 依據焦點狀態更新邊框視覺
    /// </summary>
    /// <param name="isFocused">是否具有焦點</param>
    private void UpdateBorderColor(bool isFocused)
    {
        if (IsDisposed ||
            !IsHandleCreated)
        {
            return;
        }

        // 根據 DPI 計算厚度差異。
        float scale = DeviceDpi / BaseDpi;

        int activeThickness = (int)Math.Max(5, 5 * scale),
            normalThickness = (int)Math.Max(2, 2 * scale);

        if (isFocused)
        {
            // 獲得焦點：加粗邊框（顯著形狀變化）+ 同步高對比配色。
            // 語意一致性：使用與按鈕焦點框相同的配色邏輯。
            // 最終修正：淺色模式（黑底控制項）用 Cyan（14:1）；深色模式（白底控制項）用 RoyalBlue（4.9:1）。
            PInputHost.BackColor = SystemInformation.HighContrast ?
                SystemColors.Highlight :
                (TBInput.IsDarkModeActive() ? Color.RoyalBlue : Color.Cyan);

            PInputHost.Padding = new Padding(activeThickness);
        }
        else
        {
            // 失去焦點：細邊框 + 預設還原色（觸發主題引擎）。
            PInputHost.BackColor = Color.Empty;
            PInputHost.Padding = new Padding(normalThickness);
        }
    }

    /// <summary>
    /// 執行執行緒安全的視窗返回
    /// </summary>
    /// <returns>Task</returns>
    private async Task HandleReturnToPreviousWindowSafeAsync()
    {
        if (Interlocked.CompareExchange(ref _isReturning, 1, 0) != 0)
        {
            return;
        }

        bool isNavigable = _navigationService.CanNavigateBack;

        try
        {
            await ReturnToPreviousWindowAsync();
        }
        finally
        {
            // 如果導航失敗（目標視窗消失），則多等待 1000ms 冷卻。
            // 這能防止因按鍵連點或長按導致的「連續報錯廣播」。
            if (!isNavigable)
            {
                try
                {
                    await Task.Delay(1000, _formCts.Token);
                }
                catch (OperationCanceledException)
                {

                }
            }

            Interlocked.Exchange(ref _isReturning, 0);
        }
    }

    /// <summary>
    /// 從快速鍵擷取模式還原 UI 狀態
    /// </summary>
    private void RestoreUIFromCaptureMode()
    {
        // 狀態守衛：若目前不處於擷取模式，則不執行還原邏輯。
        // 這能防止鍵盤 Esc 與手把 B 同時觸發取消時的重複 UI 重新整理。
        if (_isCapturingHotkey == 0)
        {
            return;
        }

        Interlocked.Exchange(ref _isCapturingHotkey, 0);

        // 重新快取標題前綴（因為快速鍵可能已變更）。
        UpdateTitlePrefix();

        // 還原輸入框狀態。
        TBInput.ReadOnly = false;
        TBInput.PlaceholderText = Strings.Pht_TBInput;
        TBInput.AccessibleName = Strings.A11y_TBInputName;
        TBInput.ImeMode = ImeMode.On;

        UpdateBorderColor(TBInput.Focused);

        // 還原按鈕狀態。
        BtnCopy.Enabled = true;
        BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');

        // 重置標題。
        UpdateTitle();
    }

    /// <summary>
    /// 更新視窗標題列文字
    /// </summary>
    /// <param name="suffix">要附加在標題後方的暫時性訊息（可選）</param>
    private void UpdateTitle(string? suffix = null)
    {
        // 始終以包含快速鍵的快取前綴為基礎。
        string baseTitle = _cachedTitlePrefix;

        // 擷取模式專用處理：
        // 如果正在擷取中且未提供 suffix，自動補上「請按下一組按鍵」提示。
        if (_isCapturingHotkey != 0 &&
            string.IsNullOrEmpty(suffix))
        {
            suffix = Strings.Msg_PressAnyKey;
        }

        // 如果有傳入額外訊息（如「錯誤」、「快速鍵已更新」或自動補上的「請按下一組按鍵」），優先顯示。
        if (!string.IsNullOrEmpty(suffix))
        {
            Text = $"{baseTitle} - [{suffix}]";

            return;
        }

        if (_gamepadController == null ||
            !_gamepadController.IsConnected)
        {
            Text = baseTitle;

            return;
        }

        // 當控制器連線時，顯示裝置名稱。
        Text = $"{baseTitle} · [{_gamepadController.DeviceName}]";
    }

    /// <summary>
    /// 重置觸控鍵盤標誌
    /// </summary>
    /// <returns>Task</returns>
    private async Task ResetTouchKeyboardFlagAsync()
    {
        try
        {
            await Task.Delay(AppSettings.Current.TouchKeyboardDismissDelay, _formCts.Token);

            Interlocked.Exchange(ref _isShowingTouchKeyboard, 0);
        }
        catch (OperationCanceledException)
        {
            // 視窗關閉時中止。
        }
        catch
        {
            // 忽略因視窗關閉或取消引發的錯誤。
        }
    }

    /// <summary>
    /// 顯示數值輸入對話框，並返回使用者輸入的值
    /// </summary>
    /// <param name="title">對話框標題</param>
    /// <param name="currentValue">當前值</param>
    /// <param name="defaultValue">預設值</param>
    /// <param name="minimum">最小值</param>
    /// <param name="maximum">最大值</param>
    /// <returns>使用者輸入的值，如果取消則為 null</returns>
    private int? AskForValue(
        string title,
        int currentValue,
        int defaultValue,
        int minimum = 0,
        int maximum = 65535)
    {
        using NumericInputDialog dialog = new(
            title,
            currentValue,
            defaultValue,
            0,
            1m,
            minimum,
            maximum);

        dialog.GamepadController = _gamepadController;

        // 傳入 this 作為 Owner，確保模態視窗行為正確。
        return dialog.ShowDialog(this) == DialogResult.OK ?
            (int)dialog.Value :
            null;
    }

    /// <summary>
    /// 顯示浮點數輸入對話框，並返回使用者輸入的值
    /// </summary>
    /// <param name="title">對話框標題</param>
    /// <param name="currentValue">當前值</param>
    /// <param name="defaultValue">預設值</param>
    /// <param name="minimum">最小值</param>
    /// <param name="maximum">最大值</param>
    /// <param name="step">增減步進值</param>
    /// <param name="decimalPlaces">顯示的小數位數</param>
    /// <returns>使用者輸入的值，如果取消則為 null</returns>
    private float? AskForFloat(
        string title,
        float currentValue,
        float defaultValue,
        float minimum = 0.0f,
        float maximum = 1.0f,
        decimal step = 0.1m,
        int decimalPlaces = 1)
    {
        using NumericInputDialog dialog = new(
            title,
            (decimal)currentValue,
            (decimal)defaultValue,
            decimalPlaces,
            step,
            (decimal)minimum,
            (decimal)maximum);

        dialog.GamepadController = _gamepadController;

        // 傳入 this 作為 Owner，確保模態視窗行為正確。
        return dialog.ShowDialog(this) == DialogResult.OK ?
            (float)dialog.Value :
            null;
    }
}