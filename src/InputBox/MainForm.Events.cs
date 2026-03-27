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
                        await Task.Delay(
                            50,
                            _formCts?.Token ?? CancellationToken.None);
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

                        // 手動補發 Enter 事件，強制更新黑底與邊框視覺。
                        TBInput_Enter(TBInput, EventArgs.Empty);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isProcessingActivated, 0);
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "MainForm_Activated 處理失敗");

            Debug.WriteLine($"[事件] MainForm_Activated 處理失敗：{ex.Message}");

            AnnounceA11y(string.Format(Strings.A11y_Background_Error, ex.Message));
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
                await Task.Delay(
                    AppSettings.Current.WindowRestoreDelay,
                    _formCts?.Token ?? CancellationToken.None);
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

            // 歡迎訊息與操作提示。
            // 延遲 100ms 避開視窗開啟音效。
            try
            {
                await Task.Delay(
                    100,
                    _formCts?.Token ?? CancellationToken.None);
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
                await Task.Delay(
                    1500,
                    _formCts?.Token ?? CancellationToken.None);
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
            LoggerService.LogException(ex, "MainForm_Shown 處理失敗");

            Debug.WriteLine($"[事件] MainForm_Shown 處理失敗：{ex.Message}");

            AnnounceA11y(string.Format(Strings.A11y_Background_Error, ex.Message));

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
                    SuspendLayout();

                    try
                    {
                        // 抗抖動物理鎖定：鎖定 PInputHost 寬度，防止字體度量變更引發容器抖動。
                        int currentWidth = PInputHost.Width;

                        // 鎖定寬度，但不限制高度（維持原本的高度限制狀態）。
                        PInputHost.MinimumSize = new Size(currentWidth, PInputHost.MinimumSize.Height);
                        PInputHost.MaximumSize = new Size(currentWidth, PInputHost.MaximumSize.Height);

                        // 系統無障礙設定變更時，需重新套用在地化資源與佈局。
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

                        // 更新標題列（顯示 🔄 Emoji）。
                        UpdateTitlePrefix();
                        UpdateTitle();

                        // 更新右鍵選單（顯示重啟選項）。
                        RefreshMenu();

                        // 提示使用者環境已變動。
                        string announceMsg = Strings.A11y_Theme_Changed_Hint;

                        if (SystemInformation.HighContrast)
                        {
                            announceMsg = $"{Strings.A11y_Opacity_HighContrast} {announceMsg}";
                        }

                        AnnounceA11y(announceMsg);
                    }
                    finally
                    {
                        ResumeLayout(true);

                        // 解除物理鎖定（恢復自動調適能力）。
                        PInputHost.MinimumSize = Size.Empty;
                        PInputHost.MaximumSize = Size.Empty;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "SystemEvents_UserPreferenceChanged 處理失敗");

            Debug.WriteLine($"[事件] SystemEvents_UserPreferenceChanged 處理失敗：{ex.Message}");
        }
    }

    private void TBInput_Enter(object sender, EventArgs e)
    {
        try
        {
            // Tab 鍵進入時，中止正在進行的警示動畫。
            _alertCts?.CancelAndDispose();

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

            // 強化游標辨識度。
            UpdateCaretWidth();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "TBInput_Enter 處理失敗");

            Debug.WriteLine($"[事件] TBInput_Enter 處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 根據 DPI 與無障礙設定更新輸入框游標寬度
    /// </summary>
    private void UpdateCaretWidth()
    {
        if (TBInput == null ||
            TBInput.IsDisposed ||
            !TBInput.IsHandleCreated)
        {
            return;
        }

        // 基礎寬度 3px，隨 DPI 縮放。
        float scale = DeviceDpi / AppSettings.BaseDpi;

        int caretWidth = (int)Math.Max(3, 3 * scale);
        int caretHeight = TBInput.Height;

        // 高對比模式下額外加粗。
        if (SystemInformation.HighContrast) caretWidth += (int)(2 * scale);

        // 比對邏輯：若目前的寬度與高度與快取值相同，則直接返回，不重複調用 CreateCaret。
        if (caretWidth == _lastCaretWidth &&
            caretHeight == _lastCaretHeight)
        {
            return;
        }

        // 使用 Win32 API 重新建立游標。
        // hBitmap 為 0 代表實心反轉色。
        if (User32.CreateCaret(TBInput.Handle, IntPtr.Zero, caretWidth, caretHeight))
        {
            User32.ShowCaret(TBInput.Handle);

            // 僅在真正建立成功後更新快取。
            _lastCaretWidth = caretWidth;
            _lastCaretHeight = caretHeight;
        }
    }

    private void TBInput_Leave(object sender, EventArgs e)
    {
        try
        {
            // 銷毀自定義游標，釋放系統共用資源
            User32.DestroyCaret();

            // 重置快取，確保下次進入時一定會重新建立游標。
            _lastCaretWidth = -1;
            _lastCaretHeight = -1;

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

            _alertCts?.CancelAndDispose();

            // 如果正在擷取快速鍵時失去焦點，則取消擷取模式。
            if (_isCapturingHotkey != 0)
            {
                RestoreUIFromCaptureMode();

                // 取消提示。
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
            LoggerService.LogException(ex, "TBInput_Leave 處理失敗");

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
            LoggerService.LogException(ex, "TBInput_KeyDown 處理失敗");

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
            LoggerService.LogException(ex, "BtnCopy_MouseEnter 處理失敗");

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
            LoggerService.LogException(ex, "BtnCopy_MouseLeave 處理失敗");

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
            LoggerService.LogException(ex, "BtnCopy_Enter 處理失敗");

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
            LoggerService.LogException(ex, "BtnCopy_Leave 處理失敗");

            Debug.WriteLine($"[事件] BtnCopy_Leave 處理失敗：{ex.Message}");
        }
    }

    private void BtnCopy_Paint(object? sender, PaintEventArgs e)
    {
        if (BtnCopy == null)
        {
            return;
        }

        float scale = DeviceDpi / AppSettings.BaseDpi;

        bool isDark = BtnCopy.IsDarkModeActive();

        // 雙焦點衝突防護（Dual-Focus Conflict Protection）：
        // 判斷該按鈕是否為目前表單的預設動作按鈕（AcceptButton）。
        // 根據規範，只有當目前焦點「不在任何按鈕上」且按鈕為「可用狀態」時，預設按鈕才顯示焦點邊框，
        // 以指引 Enter 鍵動作並避免畫面上出現多個「焦點色」區域造成誤導。
        bool isDefault = ReferenceEquals(AcceptButton, BtnCopy) &&
            ActiveControl is not Button &&
            BtnCopy.Enabled;

        // 基礎邊框（預設狀態）：
        // 由於 BorderSize = 0，手動繪製基礎邊框以保持物理辨識度。
        // 使用動態厚度（至少 1 像素）以適應高 DPI 環境。
        if (!BtnCopy.Focused &&
            !_isBtnHovered &&
            !isDefault)
        {
            int baseThickness = (int)Math.Max(1, scale);

            using Pen basePen = new(
                SystemInformation.HighContrast ?
                    SystemColors.WindowFrame :
                    (isDark ? Color.DimGray : Color.DarkGray),
                baseThickness);

            e.Graphics.DrawRectangle(
                basePen,
                0,
                0,
                BtnCopy.Width - 1,
                BtnCopy.Height - 1);
        }

        // 繪製焦點、懸停與預設動作邊框（Focus／Hover／Default Border）：
        if (BtnCopy.Focused ||
            _isBtnHovered ||
            isDefault)
        {
            // 實施「Zero-Jitter」原則：使用固定的 3 像素厚度，與輸入框焦點邊框對齊。
            int borderThickness = (int)Math.Max(3, 3 * scale),
                inset = (int)Math.Max(2, 2 * scale);

            // 淺色模式（黑底控制項）用 Cyan；深色模式（白底控制項）用 RoyalBlue。
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

        // 繪製注視進度條（Dwell Feedback）。
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
                // 淺色（黑底）= DarkOrange／Orange；深色（白底）= Firebrick／OrangeRed。
                Color baseColor = isDark ?
                        Color.Firebrick :
                        Color.DarkOrange,
                    hatchColor = isDark ?
                        Color.OrangeRed :
                        Color.Orange;

                // 雙重編碼（CVD 色盲補償）。
                // 實心背景 + 斜向條紋紋理，確保不同色覺類型皆能直觀辨識。
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

            // 視線重新接合。
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

                    RestoreButtonDefaultStyle();
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "複製按鈕點擊處理失敗");

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

        // 核心邏輯調整：
        // 1. 若為鍵盤焦點觸發且滑鼠「不在」按鈕上，則套用強烈靜態視覺（不啟動動畫）。
        // 2. 其餘情況（主要是滑鼠懸停），不論是否有焦點，皆啟動 Dwell 動畫以支援 Re-gaze。
        bool isPureKeyboardFocus = isKeyboardFocus && !_isBtnHovered;

        if (isPureKeyboardFocus)
        {
            // 強烈視覺：純鍵盤焦點（Tab 鍵切換過來，且滑鼠不在上方）。
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
            if (BoldBtnFont != null && !ReferenceEquals(BtnCopy.Font, BoldBtnFont))
            {
                BtnCopy.Font = BoldBtnFont;
            }

            // 邊框由 Paint 事件繪製，此處將原生 BorderSize 設為 0。
            BtnCopy.FlatAppearance.BorderSize = 0;
            BtnCopy.AccessibleDescription = $"{Strings.A11y_BtnCopyDesc} ({Strings.A11y_State_Focused})";
        }
        else
        {
            // 溫和視覺與動畫：滑鼠／眼控懸停（Hover）。
            // 備註：即便目前已有鍵盤焦點，只要滑鼠進入，就轉為 Dwell 動畫模式以支援 Re-gaze。
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
            if (A11yFont != null &&
                !ReferenceEquals(BtnCopy.Font, A11yFont))
            {
                BtnCopy.Font = A11yFont;
            }

            // 邊框由 Paint 事件繪製，此處將原生 BorderSize 設為 0。
            BtnCopy.FlatAppearance.BorderSize = 0;
            BtnCopy.AccessibleDescription = $"{Strings.A11y_BtnCopyDesc} ({Strings.A11y_State_Hover})";

            // 動畫回饋：只要有 Hover，不論焦點狀態，皆啟動 Dwell！
            BtnCopy.RunDwellAnimationAsync(
                    id,
                    () => Interlocked.Read(ref _animationId),
                    (p) => _dwellProgress = p,
                    ct: _formCts?.Token ?? CancellationToken.None)
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

        // 若滑鼠已移出但仍具備鍵盤焦點，則由 Hover 樣式跳回強烈靜态高亮。
        if (!force && !_isBtnHovered && BtnCopy.Focused)
        {
            ApplyButtonHoverStyle(isKeyboardFocus: true);

            return;
        }

        // 只有在確實失焦且無懸停，或強制還原時執行完整還原。
        if (force ||
            (!BtnCopy.Focused && !_isBtnHovered))
        {
            // 還原為預設顏色，觸發原生主題引擎自動套用正確背景。
            BtnCopy.BackColor = Color.Empty;
            BtnCopy.ForeColor = Color.Empty;

            if (A11yFont != null)
            {
                BtnCopy.Font = A11yFont;
            }

            if (_originalBtnPadding != default)
            {
                BtnCopy.Padding = _originalBtnPadding;
            }

            BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;
            BtnCopy.FlatAppearance.BorderColor = Color.Empty;
            // 邊框由 Paint 事件繪製，此處將原生 BorderSize = 0。
            BtnCopy.FlatAppearance.BorderSize = 0;
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

                // 非同步等待 1 秒冷卻，涵蓋閃爍動畫週期。
                // 期間所有的點擊都會被 BtnCopy_Click 攔截，但不會破壞 UI 狀態。
                await Task.Delay(1000, _formCts?.Token ?? CancellationToken.None);

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

            // 將兩條相關訊息合併為一條發送，確保在焦點切換前使用者能聽到完整結果。
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
    /// 處理自定義快速鍵與歷史導覽
    /// </summary>
    /// <param name="e">KeyEventArgs</param>
    private void HandleKeyDown(KeyEventArgs e)
    {
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

        // ←：游標左移／選取。
        if (e.KeyCode == Keys.Left)
        {
            this.SafeBeginInvoke(() =>
            {
                if (e.Shift)
                {
                    if (TBInput.SelectionLength > 0)
                    {
                        AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                            Strings.A11y_Selected_Text_PrivacySafe :
                            string.Format(Strings.A11y_Selected_Text, TBInput.SelectedText), true);
                    }
                }
                else
                {
                    AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                        Strings.A11y_Cursor_Move_PrivacySafe :
                        string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);
                }
            });

            return;
        }

        // →：游標右移／選取。
        if (e.KeyCode == Keys.Right)
        {
            this.SafeBeginInvoke(() =>
            {
                if (e.Shift)
                {
                    if (TBInput.SelectionLength > 0)
                    {
                        AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                            Strings.A11y_Selected_Text_PrivacySafe :
                            string.Format(Strings.A11y_Selected_Text, TBInput.SelectedText), true);
                    }
                }
                else
                {
                    AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                        Strings.A11y_Cursor_Move_PrivacySafe :
                        string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);
                }
            });

            return;
        }

        // Home、End、Ctrl + Left／Right：跳轉游標。
        if (e.KeyCode == Keys.Home ||
            e.KeyCode == Keys.End ||
            (e.Control && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)))
        {
            this.SafeBeginInvoke(() =>
            {
                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.A11y_Cursor_Move_PrivacySafe :
                    string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);
            });

            return;
        }

        // Backspace：刪除文字。
        if (e.KeyCode == Keys.Back)
        {
            // 擷取刪除前的狀態。
            string oldText = TBInput.Text;

            int oldStart = TBInput.SelectionStart,
                oldLen = TBInput.SelectionLength;

            // 檢查是否可以刪除。
            if (oldLen == 0 &&
                oldStart == 0)
            {
                VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                AnnounceA11y(Strings.A11y_Cannot_Delete, true);

                return;
            }

            this.SafeBeginInvoke(() =>
            {
                // 比對內容是否真的減少。
                if (TBInput.TextLength < oldText.Length)
                {
                    if (oldLen > 0)
                    {
                        AnnounceA11y(string.Format(Strings.A11y_Delete_Multiple, oldLen), true);
                    }
                    else
                    {
                        if (AppSettings.Current.IsPrivacyMode)
                        {
                            AnnounceA11y(Strings.A11y_Delete_Char_PrivacySafe, true);
                        }
                        else
                        {
                            char deletedChar = oldText[oldStart - 1];

                            AnnounceA11y(string.Format(Strings.A11y_Delete_Char, deletedChar), true);
                        }
                    }
                }
            });

            return;
        }

        // Delete：刪除文字。
        if (e.KeyCode == Keys.Delete)
        {
            // 擷取刪除前的狀態。
            string oldText = TBInput.Text;

            int oldStart = TBInput.SelectionStart,
                oldLen = TBInput.SelectionLength;

            // 檢查是否可以刪除。
            if (oldLen == 0 &&
                oldStart == oldText.Length)
            {
                VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                return;
            }

            this.SafeBeginInvoke(() =>
            {
                // 比對內容是否真的減少。
                if (TBInput.TextLength < oldText.Length)
                {
                    if (oldLen > 0)
                    {
                        AnnounceA11y(string.Format(Strings.A11y_Delete_Multiple, oldLen), true);
                    }
                    else
                    {
                        if (AppSettings.Current.IsPrivacyMode)
                        {
                            AnnounceA11y(Strings.A11y_Delete_Char_PrivacySafe, true);
                        }
                        else
                        {
                            char deletedChar = oldText[oldStart];

                            AnnounceA11y(string.Format(Strings.A11y_Delete_Char, deletedChar), true);
                        }
                    }
                }
            });

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

            // 只有在最後一行時，按「下」才觸發歷史記錄。
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
        // 統一震動回饋，確保鍵盤 Esc 或手把觸發時皆有觸覺感應。
        VibrateAsync(VibrationPatterns.ClearInput).SafeFireAndForget();

        FeedbackService.PlaySound(SystemSounds.Beep);

        // 如果文字框不為空，執行清除並重置歷程索引。
        if (!string.IsNullOrEmpty(TBInput.Text))
        {
            TBInput.Clear();

            // 重置 InputHistoryManager 索引值。
            _historyService.ResetIndex();
        }

        // 統一播報訊息。
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

            // 告知快速鍵註冊失敗。
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

            AnnounceA11y(
                direction < 0 ?
                    Strings.A11y_Nav_Oldest :
                    Strings.A11y_Nav_Newest,
                interrupt: true);
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
        if ((DateTime.UtcNow - _lastTouchKeyboardOpened).TotalMilliseconds < 800)
        {
            return;
        }

        _lastTouchKeyboardOpened = DateTime.UtcNow;

        // 若 TBInput 尚未取得焦點，則先聚焦。
        // 在 Windows 10／11 中，Focus 動作本身通常就會觸發系統自動開啟觸控鍵盤。
        if (TBInput.CanFocus &&
            !TBInput.Focused)
        {
            TBInput.Focus();
        }

        // A11y 廣播。
        AnnounceA11y(Strings.A11y_Opening_Keyboard);

        // 使用非同步延遲，避免與系統原生的 Focus 彈出行為發生競態。
        Task.Run(async () =>
        {
            // 給予 Windows 系統 150ms 的緩衝時間來啟動其原生的鍵盤彈出邏輯。
            await Task.Delay(150);

            // 如果 150ms 後鍵盤已經顯示（由系統自動開啟），則我們不需要再介入 Toggle。
            if (TouchKeyboardService.IsVisible())
            {
                Debug.WriteLine("觸控鍵盤已由系統自動開啟，略過手動 Toggle。");

                return;
            }

            // 若系統沒開，我們才手動嘗試透過 COM 介面啟動。
            this.SafeInvoke(() =>
            {
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
                        // 失敗時的處理。
                        FeedbackService.PlaySound(SystemSounds.Hand);

                        VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                        FlashAlertAsync().SafeFireAndForget();

                        AnnounceA11y(Strings.Err_TouchKeyboardNotFound);
                    }
                }
                catch
                {
                    FeedbackService.PlaySound(SystemSounds.Hand);

                    VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                    FlashAlertAsync().SafeFireAndForget();

                    AnnounceA11y(Strings.Err_TouchKeyboardNotFound);
                }
                finally
                {
                    _ = ResetTouchKeyboardFlagAsync();
                }
            });
        });
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

        // 強效焦點補捉機制。
        // 在全螢幕遊戲環境下呼叫時，系統焦點可能會有短暫的競爭期。
        // 透過非同步重試確保 TBInput 絕對取得焦點。
        Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                bool focused = false;

                this.SafeInvoke(() =>
                {
                    if (IsDisposed ||
                        !IsHandleCreated)
                    {
                        return;
                    }

                    if (TBInput != null &&
                        TBInput.CanFocus)
                    {
                        TBInput.Focus();

                        focused = TBInput.Focused;
                    }
                });

                if (focused)
                {
                    break;
                }

                await Task.Delay(50, _formCts?.Token ?? CancellationToken.None);
            }

            // 焦點確定取得後，廣播 A11y 宣告。
            this.SafeInvoke(() =>
            {
                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                AnnounceA11y(Strings.A11y_MainFormName);
            });
        },
        _formCts?.Token ?? CancellationToken.None).SafeFireAndForget();

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
        // 這能防止先播報「正在返回...」隨後立刻接「錯誤／視窗已關閉」的語音衝突。
        if (!_navigationService.CanNavigateBack)
        {
            await _navigationService.NavigateBackAsync(
                _gamepadController,
                msg => AnnounceA11y(msg),
                _formCts?.Token ?? CancellationToken.None);

            return;
        }

        if (announce)
        {
            // 告知使用者正在返回前一個視窗。
            AnnounceA11y(Strings.A11y_Returning);
        }

        // 呼叫導航服務，並傳入目前的控制器實例以進行安全檢查與震動。
        await _navigationService.NavigateBackAsync(
            _gamepadController,
            msg => AnnounceA11y(msg),
            _formCts?.Token ?? CancellationToken.None);

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
        // 先建立新實例並持有本地引用，再原子交換出舊 CTS，確保 token 的取得不依賴欄位讀取。
        CancellationTokenSource newAlertCts = CancellationTokenSource
            .CreateLinkedTokenSource(_formCts?.Token ?? CancellationToken.None);

        Interlocked.Exchange(ref _alertCts, newAlertCts)?.CancelAndDispose();

        CancellationToken token = newAlertCts.Token;

        try
        {
            // 決定警示色。
            // 修正選色邏輯以對齊反轉後的控制項背景。
            // 淺色模式（黑底）：用 DarkOrange（8.3:1）；深色模式（白底）：用 Firebrick（5.8:1）。
            Color alertColor = SystemInformation.HighContrast ?
                SystemColors.Highlight :
                (TBInput.IsDarkModeActive() ? Color.Firebrick : Color.DarkOrange);

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
                    bool isAlert = intensity > 0.5f;

                    Color hcBack = isAlert ?
                            alertColor :
                            SystemColors.Window,
                        hcFore = isAlert ?
                            SystemColors.HighlightText :
                            SystemColors.WindowText;

                    // 同步更新背景與前景，確保高對比下文字可讀性。
                    PInputHost.UpdateRecursive(hcBack, hcFore);
                }
                else
                {
                    // 閃爍基色改為純淨底色（黑／白），避免與高飽和焦點色（Cyan／RoyalBlue）插值產生髒濁色。
                    Color pureBase = isDark ?
                        Color.White :
                        Color.Black;

                    int rB = (int)(pureBase.R + (alertColor.R - pureBase.R) * intensity),
                        gB = (int)(pureBase.G + (alertColor.G - pureBase.G) * intensity),
                        bB = (int)(pureBase.B + (alertColor.B - pureBase.B) * intensity);

                    Color flashColor = Color.FromArgb(255, rB, gB, bB),
                        // 根據背景亮度的知覺亮度（Perceptual Luminance）決定前景文字色，確保對比度符合規範。
                        flashFore = (flashColor.R * 0.299 + flashColor.G * 0.587 + flashColor.B * 0.114) > 128 ?
                            Color.Black :
                            Color.White;

                    // 遞歸背景與前景同步：僅作用於數據內容區域（PInputHost），按鈕保持其靜態視覺狀態。
                    PInputHost.UpdateRecursive(flashColor, flashFore);
                }
            }

            // 嚴格遵守光敏性癲癇防護與使用者偏好：
            // 若使用者在系統層級關閉了動畫效果（UIEffectsEnabled 為 false），
            // 則不進行循環閃爍，改為一次性的「長脈衝（Static Pulse）」回饋。
            if (!SystemInformation.UIEffectsEnabled)
            {
                await this.SafeInvokeAsync(() => ApplyAlertVisuals(1.0f));

                // 維持一段較長時間（800ms）讓低視能使用者感知狀態，隨後恢復。
                await Task.Delay(800, token);

                return;
            }

            int totalDuration = AppSettings.PhotoSafeFrequencyMs;

            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(AppSettings.TargetFrameTimeMs));

            long startTime = Stopwatch.GetTimestamp();

            while (await timer.WaitForNextTickAsync(token))
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTime;

                double elapsedMs = (double)elapsedTicks / Stopwatch.Frequency * 1000.0;

                if (elapsedMs >= totalDuration)
                {
                    break;
                }

                // 使用 AppSettings.PhotoSafeFrequencyMs 定義的正弦波週期（1Hz）。
                double angle = elapsedMs / AppSettings.PhotoSafeFrequencyMs * 2.0 * Math.PI - (Math.PI / 2.0);

                float intensity = (float)((Math.Sin(angle) + 1.0) / 2.0);

                await this.SafeInvokeAsync(() => ApplyAlertVisuals(intensity));
            }
        }
        catch (OperationCanceledException)
        {

        }
        finally
        {
            Interlocked.Exchange(ref _isFlashing, 0);
            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            if (!IsDisposed &&
                IsHandleCreated)
            {
                this.SafeInvoke(() =>
                {
                    // 還原 PInputHost 及其所有子控制項的顏色（包含 ForeColor），消除視覺殘留。
                    PInputHost.ResetThemeRecursive();

                    // 恢復邊框樣式與厚度。
                    UpdateBorderColor(TBInput.Focused);

                    // 根據焦點狀態重新套用強烈視覺回饋（若有焦點）。
                    if (TBInput.Focused)
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

        // 根據規範實施「Zero-Jitter」原則：Padding 必須固定以防止佈局抖動。
        // 使用 3 像素作為平衡美感與 A11y 辨識度的黃金厚度。
        float scale = DeviceDpi / AppSettings.BaseDpi;

        int fixedThickness = (int)Math.Max(3, 3 * scale);

        // 始終維持固定的 Padding，消除焦點切換時的文字跳動。
        PInputHost.Padding = new Padding(fixedThickness);

        if (isFocused)
        {
            // 獲得焦點：僅變更背景色以提供視覺暗示。
            // 淺色模式（黑底控制項）用 Cyan；深色模式（白底控制項）用 RoyalBlue。
            PInputHost.BackColor = SystemInformation.HighContrast ?
                SystemColors.Highlight :
                (TBInput.IsDarkModeActive() ? Color.RoyalBlue : Color.Cyan);
        }
        else
        {
            // 失去焦點：還原為透明／預設色。
            PInputHost.BackColor = Color.Empty;
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
                    await Task.Delay(
                        1000,
                        _formCts?.Token ?? CancellationToken.None);
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
        // 若目前不處於擷取模式，則不執行還原邏輯。
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
            await Task.Delay(
                AppSettings.Current.TouchKeyboardDismissDelay,
                _formCts?.Token ?? CancellationToken.None);

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