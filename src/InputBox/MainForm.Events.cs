using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
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
            if (_inputState.TryBeginProcessingActivated())
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
                    _inputState.EndProcessingActivated();
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
            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            // 如果正在擷取快速鍵，則不執行一般的進入變色邏輯，保留擷取模式的視覺狀態。
            if (_inputState.IsHotkeyCaptureActive)
            {
                return;
            }

            // 判斷是否為高對比模式，如果系統已經開啟高對比，
            // 就完全尊重系統設定，不要自己改顏色。
            if (SystemInformation.HighContrast)
            {
                // 高對比模式配色。
                TBInput.BackColor = SystemColors.Highlight;
                TBInput.ForeColor = SystemColors.HighlightText;

                // 顏色套用後再更新邊框，確保情境感知選色與目前背景一致。
                UpdateBorderColor(true);

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

            // 顏色套用後再更新邊框，避免邊框以舊背景狀態判斷造成可見性下降。
            UpdateBorderColor(true);

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

            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            // 如果正在擷取快速鍵時失去焦點，則取消擷取模式。
            if (_inputState.IsHotkeyCaptureActive)
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

            // 執行非同步動作（包含成功複製，或是空字串時的 1 秒冷卻等待）。
            await PerformCopyAsync();

            // 視線重新接合：由擴充方法統一處理進度條重置與游標位置檢測。
            await BtnCopy.CompleteClickAndReGazeAsync();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "複製按鈕點擊處理失敗");

            Debug.WriteLine($"[事件] 複製按鈕點擊處理失敗：{ex.Message}");

            AnnounceA11y(string.Format(Strings.A11y_Background_Error, ex.Message));
        }
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
            if (await TryHandleEmptyCopyAsync(strTextToCopy))
            {
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
                await HandleCopyFailureAsync();

                return;
            }

            await HandleCopySuccessAsync(strTextToCopy);
        }
        catch (Exception ex)
        {
            // 捕捉所有異常，包括 ExternalException 和其他可能的錯誤。

            LoggerService.LogException(ex, "PerformCopyAsync 剪貼簿操作失敗");

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
    /// 嘗試處理空字串複製情境，包含冷卻與多模態錯誤回饋
    /// </summary>
    /// <param name="textToCopy">目前準備複製的文字。</param>
    /// <returns>若已處理空字串情境則回傳 true。</returns>
    private async Task<bool> TryHandleEmptyCopyAsync(string textToCopy)
    {
        if (!string.IsNullOrEmpty(textToCopy))
        {
            return false;
        }

        _isActionCooldown = true;

        FeedbackService.PlaySound(SystemSounds.Beep);

        await VibrateAsync(VibrationPatterns.ActionFail);

        if (IsDisposed)
        {
            return true;
        }

        FlashAlertAsync().SafeFireAndForget();

        AnnounceA11y(Strings.A11y_No_Text_To_Copy);

        await Task.Delay(1000, _formCts?.Token ?? CancellationToken.None);

        _isActionCooldown = false;

        return true;
    }

    /// <summary>
    /// 處理寫入剪貼簿失敗後的 UI 與 A11y 回饋
    /// </summary>
    /// <returns>非同步作業。</returns>
    private async Task HandleCopyFailureAsync()
    {
        FeedbackService.PlaySound(SystemSounds.Hand);

        await VibrateAsync(VibrationPatterns.ActionFail);

        if (IsDisposed)
        {
            return;
        }

        FlashAlertAsync().SafeFireAndForget();

        BtnCopy.Text = Strings.Msg_CopyFail;
        BtnCopy.Enabled = true;

        if (ContainsFocus)
        {
            TBInput.Focus();
        }

        AnnounceA11y(Strings.Msg_CopyFail);

        await ResetButtonStateAsync();
    }

    /// <summary>
    /// 處理複製成功後的歷程記錄、回饋與返回前景視窗流程
    /// </summary>
    /// <param name="textToCopy">已成功寫入剪貼簿的文字。</param>
    /// <returns>非同步作業。</returns>
    private async Task HandleCopySuccessAsync(string textToCopy)
    {
        _historyService.Add(textToCopy);

        FeedbackService.PlaySound(SystemSounds.Asterisk);

        await VibrateAsync(VibrationPatterns.CopySuccess);

        if (IsDisposed)
        {
            return;
        }

        BtnCopy.Text = Strings.Msg_Copied;
        BtnCopy.AccessibleDescription = Strings.Msg_Copied;

        AnnounceA11y($"{Strings.Msg_Copied}. {Strings.A11y_Returning}");

        TBInput.Clear();

        await ReturnToPreviousWindowAsync(announce: false);

        if (IsDisposed ||
            BtnCopy == null)
        {
            return;
        }

        await ResetButtonStateAsync();
    }

    /// <summary>
    /// 處理自定義快速鍵與歷史導覽
    /// </summary>
    /// <param name="e">KeyEventArgs</param>
    private void HandleKeyDown(KeyEventArgs e)
    {
        if (TryHandleEscapeKeyDown(e) ||
            TryHandleHelpKeyDown(e) ||
            TryHandleCursorAnnouncementKeys(e) ||
            TryHandleDeletionKeys(e) ||
            TryHandleHistoryNavigationKeys(e))
        {
            return;
        }
    }

    /// <summary>
    /// 嘗試攔截 Escape 按鍵（清空或返回前景視窗）
    /// </summary>
    /// <param name="e">鍵盤事件參數。</param>
    /// <returns>若按鍵已被處理則回傳 true。</returns>
    private bool TryHandleEscapeKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape)
        {
            return false;
        }

        if (string.IsNullOrEmpty(TBInput.Text))
        {
            HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();
        }
        else
        {
            ClearInput();
        }

        e.SuppressKeyPress = true;

        return true;
    }

    /// <summary>
    /// 嘗試攔截 F1 按鍵並開啟說明
    /// </summary>
    /// <param name="e">鍵盤事件參數。</param>
    /// <returns>若按鍵已被處理則回傳 true。</returns>
    private bool TryHandleHelpKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F1)
        {
            return false;
        }

        e.SuppressKeyPress = true;

        ShowHelpDialog();

        return true;
    }

    /// <summary>
    /// 嘗試處理游標移動相關按鍵並播報位置／選取資訊
    /// </summary>
    /// <param name="e">鍵盤事件參數。</param>
    /// <returns>若按鍵已被處理則回傳 true。</returns>
    private bool TryHandleCursorAnnouncementKeys(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left ||
            e.KeyCode == Keys.Right)
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

            return true;
        }

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

            return true;
        }

        return false;
    }

    /// <summary>
    /// 嘗試處理 Backspace/Delete 刪除行為。
    /// </summary>
    /// <param name="e">鍵盤事件參數。</param>
    /// <returns>若按鍵已被處理則回傳 true。</returns>
    private bool TryHandleDeletionKeys(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Back &&
            e.KeyCode != Keys.Delete)
        {
            return false;
        }

        string oldText = TBInput.Text;

        int oldStart = TBInput.SelectionStart,
            oldLen = TBInput.SelectionLength;

        if (e.KeyCode == Keys.Back)
        {
            if (oldLen == 0 &&
                oldStart == 0)
            {
                VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();
                AnnounceA11y(Strings.A11y_Cannot_Delete, true);

                return true;
            }

            this.SafeBeginInvoke(() => AnnounceDeletionResult(oldText, oldStart, oldLen, isBackspace: true));

            return true;
        }

        if (oldLen == 0 &&
            oldStart == oldText.Length)
        {
            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

            return true;
        }

        this.SafeBeginInvoke(() => AnnounceDeletionResult(oldText, oldStart, oldLen, isBackspace: false));

        return true;
    }

    /// <summary>
    /// 依據刪除前後狀態播報刪除結果
    /// </summary>
    /// <param name="oldText">刪除前文字內容。</param>
    /// <param name="oldStart">刪除前游標起始位置。</param>
    /// <param name="oldLen">刪除前選取長度。</param>
    /// <param name="isBackspace">是否為 Backspace 行為。</param>
    private void AnnounceDeletionResult(string oldText, int oldStart, int oldLen, bool isBackspace)
    {
        if (TBInput.TextLength >= oldText.Length)
        {
            return;
        }

        if (oldLen > 0)
        {
            AnnounceA11y(string.Format(Strings.A11y_Delete_Multiple, oldLen), true);

            return;
        }

        if (AppSettings.Current.IsPrivacyMode)
        {
            AnnounceA11y(Strings.A11y_Delete_Char_PrivacySafe, true);

            return;
        }

        char deletedChar = isBackspace ? oldText[oldStart - 1] : oldText[oldStart];

        AnnounceA11y(string.Format(Strings.A11y_Delete_Char, deletedChar), true);
    }

    /// <summary>
    /// 嘗試處理歷程導覽按鍵（上／下）
    /// </summary>
    /// <param name="e">鍵盤事件參數。</param>
    /// <returns>若按鍵已被處理則回傳 true。</returns>
    private bool TryHandleHistoryNavigationKeys(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up)
        {
            int currentLine = TBInput.GetLineFromCharIndex(TBInput.SelectionStart);

            if (currentLine == 0)
            {
                NavigateHistory(-1);
                e.SuppressKeyPress = true;
            }

            return true;
        }

        if (e.KeyCode == Keys.Down)
        {
            int currentLine = TBInput.GetLineFromCharIndex(TBInput.SelectionStart);
            int totalLines = TBInput.GetLineFromCharIndex(TBInput.TextLength);

            if (currentLine == totalLines)
            {
                NavigateHistory(+1);
                e.SuppressKeyPress = true;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 清除輸入框內容
    /// </summary>
    private void ClearInput()
    {
        // 統一震動回饋，確保鍵盤 Esc 或控制器觸發時皆有觸覺感應。
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

        HandleHistoryBoundaryFeedback(navigationResult, direction);

        if (navigationResult.IsCleared)
        {
            HandleClearedHistoryResult(navigationResult);
            return;
        }

        HandleSuccessHistoryResult(navigationResult);
    }

    /// <summary>
    /// 處理歷程導覽邊界時的提示音、震動與語音播報
    /// </summary>
    /// <param name="navigationResult">導覽結果。</param>
    /// <param name="direction">導覽方向。</param>
    private void HandleHistoryBoundaryFeedback(InputHistoryService.NavigationResult navigationResult, int direction)
    {
        // 處理震動與視覺（邊界或錯誤）。
        if (!navigationResult.IsBoundaryHit)
        {
            return;
        }

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

    /// <summary>
    /// 處理歷程清空後的輸入框與播報行為。
    /// </summary>
    /// <param name="navigationResult">導覽結果。</param>
    private void HandleClearedHistoryResult(InputHistoryService.NavigationResult navigationResult)
    {
        TBInput.Clear();

        // 如果不是因為撞牆才清空，才獨立報讀（避免雙重語音）。
        if (!navigationResult.IsBoundaryHit)
        {
            AnnounceA11y(Strings.Msg_InputCleared, interrupt: true);
        }
    }

    /// <summary>
    /// 處理歷程導覽成功後的文字套用與索引播報
    /// </summary>
    /// <param name="navigationResult">導覽結果。</param>
    private void HandleSuccessHistoryResult(InputHistoryService.NavigationResult navigationResult)
    {
        if (!navigationResult.Success ||
            navigationResult.Text == null)
        {
            return;
        }

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
        Task.Run(OpenTouchKeyboardWithDelayAsync).SafeFireAndForget();
    }

    /// <summary>
    /// 延遲嘗試開啟觸控鍵盤，避免與系統自動彈出邏輯競態
    /// </summary>
    /// <returns>非同步作業。</returns>
    private async Task OpenTouchKeyboardWithDelayAsync()
    {
        // 給予 Windows 系統約 150ms 的緩衝時間來啟動其原生的鍵盤彈出邏輯。
        await Task.Delay(150);

        // 如果延遲後鍵盤已經顯示（由系統自動開啟），則我們不需要再介入 Toggle。
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
                if (!_inputState.TryBeginTouchKeyboard())
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
                    HandleTouchKeyboardOpenFailure();
                }
            }
            catch
            {
                HandleTouchKeyboardOpenFailure();
            }
            finally
            {
                _ = ResetTouchKeyboardFlagAsync();
            }
        });
    }

    /// <summary>
    /// 處理觸控鍵盤開啟失敗時的回饋
    /// </summary>
    private void HandleTouchKeyboardOpenFailure()
    {
        // 失敗時的處理。
        FeedbackService.PlaySound(SystemSounds.Hand);

        VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

        FlashAlertAsync().SafeFireAndForget();

        AnnounceA11y(Strings.Err_TouchKeyboardNotFound);
    }

    /// <summary>
    /// 將視窗呼叫至前景準備輸入
    /// </summary>
    public void ShowForInput()
    {
        // 捕捉目前視窗。
        _windowFocusService.CaptureCurrentWindow();

        ShowAndActivateInputWindow();

        // 強效焦點補捉機制。
        // 在全螢幕遊戲環境下呼叫時，系統焦點可能會有短暫的競爭期。
        // 透過非同步重試確保 TBInput 絕對取得焦點。
        Task.Run(
            EnsureInputFocusAndAnnounceAsync,
            _formCts?.Token ?? CancellationToken.None).SafeFireAndForget();

        FeedbackService.PlaySound(SystemSounds.Asterisk);

        VibrateAsync(VibrationPatterns.ShowInput).SafeFireAndForget();
    }

    /// <summary>
    /// 顯示並啟用主輸入視窗
    /// </summary>
    private void ShowAndActivateInputWindow()
    {
        // 顯示視窗。
        Show();

        // 還原視窗。
        User32.ShowWindow(Handle, User32.ShowWindowCommand.Restore);

        // 帶至前方。
        BringToFront();

        // 啟用視窗。
        Activate();
    }

    /// <summary>
    /// 以短次數重試確保輸入框取得焦點，並於完成後播報主視窗名稱
    /// </summary>
    /// <returns>非同步作業。</returns>
    private async Task EnsureInputFocusAndAnnounceAsync()
    {
        for (int i = 0; i < 3; i++)
        {
            if (TryFocusInputControl())
            {
                break;
            }

            await Task.Delay(50, _formCts?.Token ?? CancellationToken.None);
        }

        AnnounceMainFormNameIfAlive();
    }

    /// <summary>
    /// 嘗試在 UI 執行緒聚焦輸入框
    /// </summary>
    /// <returns>若輸入框成功取得焦點則回傳 true。</returns>
    private bool TryFocusInputControl()
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

        return focused;
    }

    /// <summary>
    /// 在視窗仍有效時播報主視窗名稱
    /// </summary>
    private void AnnounceMainFormNameIfAlive()
    {
        this.SafeInvoke(() =>
        {
            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            AnnounceA11y(Strings.A11y_MainFormName);
        });
    }

    /// <summary>
    /// 還原焦點至先前擷取的視窗
    /// </summary>
    /// <param name="announce">是否公告還原焦點的訊息</param>
    /// <returns>Task</returns>
    private async Task ReturnToPreviousWindowAsync(bool announce = true)
    {
        if (await TryNavigateBackWhenTargetMissingAsync())
        {
            return;
        }

        if (announce)
        {
            // 告知使用者正在返回前一個視窗。
            AnnounceA11y(Strings.A11y_Returning);
        }

        await NavigateBackWithAnnouncementAsync();
        FlashWindowIfStillForeground();
    }

    /// <summary>
    /// 若目標前景視窗已消失，直接執行導航流程並回傳 true
    /// </summary>
    /// <returns>若已直接處理導航則回傳 true。</returns>
    private async Task<bool> TryNavigateBackWhenTargetMissingAsync()
    {
        // 前置檢查：如果目標視窗已經消失，直接進入導航流程（它會發送正確的失敗廣播）。
        // 這能防止先播報「正在返回...」隨後立刻接「錯誤／視窗已關閉」的語音衝突。
        if (_navigationService.CanNavigateBack)
        {
            return false;
        }

        await NavigateBackWithAnnouncementAsync();

        return true;
    }

    /// <summary>
    /// 透過導航服務返回前景視窗，並將狀態訊息導向 A11y 廣播
    /// </summary>
    /// <returns>非同步作業。</returns>
    private async Task NavigateBackWithAnnouncementAsync()
    {
        await _navigationService.NavigateBackAsync(
            _gamepadController,
            msg => AnnounceA11y(msg),
            _formCts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// 若返回後本視窗仍在前景，觸發閃爍提示使用者手動切換
    /// </summary>
    private void FlashWindowIfStillForeground()
    {
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
            !_inputState.TryBeginFlashing())
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

                    Color flashColor = Color.FromArgb(255, rB, gB, bB);

                    // WCAG 相對亮度精確切換閾值（crossover L≈0.1791），修復 YUV≈128 近似在切換帶（intensity≈0.75）
                    // 導致文字對比跌破 AA（3.5~4.2:1）的問題。修復後全程 ≥4.64:1 AA；
                    // 14f bold 大型文字全程 ≥4.5:1 AAA。
                    static float FLin(int c)
                    {
                        float f = c / 255f;

                        return f <= 0.04045f ? f / 12.92f : MathF.Pow((f + 0.055f) / 1.055f, 2.4f);
                    }

                    Color flashFore = (0.2126f * FLin(flashColor.R) + 0.7152f * FLin(flashColor.G) + 0.0722f * FLin(flashColor.B)) > 0.1791f ?
                        Color.Black :
                        Color.White;

                    // 遞歸背景與前景同步：僅作用於數據內容區域（PInputHost），按鈕保持其靜態視覺狀態。
                    PInputHost.UpdateRecursive(flashColor, flashFore);
                }
            }

            // 嚴格遵守光敏性癲癇防護與使用者偏好：
            // 若使用者在系統層級關閉了動畫效果（UIEffectsEnabled 為 false），
            // 則不進行循環閃爍，改為一次性的「長脈衝（Static Pulse）」回饋。
            if (!SystemInformation.UIEffectsEnabled ||
                !AppSettings.Current.EnableAnimatedVisualAlerts)
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
            _inputState.EndFlashing();
            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            if (!IsDisposed &&
                IsHandleCreated)
            {
                this.SafeInvoke(RestoreInputVisualsAfterFlash);
            }
        }
    }

    /// <summary>
    /// 閃爍結束後還原輸入區與焦點視覺狀態
    /// </summary>
    private void RestoreInputVisualsAfterFlash()
    {
        // 還原 PInputHost 及其所有子控制項的顏色（包含 ForeColor），消除視覺殘留。
        PInputHost.ResetThemeRecursive();

        // 根據焦點狀態重新套用強烈視覺回饋（若有焦點）。
        // 必須在 UpdateBorderColor 之前設定 BackColor，
        // 確保情境感知選色以正確的背景狀態為基準（對齊 TBInput_Enter 的修正邏輯）。
        if (TBInput.Focused)
        {
            if (SystemInformation.HighContrast)
            {
                TBInput.BackColor = SystemColors.Highlight;
                TBInput.ForeColor = SystemColors.HighlightText;
            }
            else if (TBInput.IsDarkModeActive())
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

        // BackColor 設定完畢後再恢復邊框，確保情境感知選色與當前背景一致。
        UpdateBorderColor(TBInput.Focused);
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
            // 情境感知焦點邊框色（Context-Aware Focus Border Color）：
            // 依據 TBInput.BackColor 實際值動態選取，確保在強視覺（反轉底色）
            // 與中性（懸停灰）兩種狀態下皆達 WCAG AA（≥ 4.5:1）以上。
            Color borderColor;

            if (SystemInformation.HighContrast)
            {
                borderColor = SystemColors.Highlight;
            }
            else if (TBInput.BackColor == Color.Black)
            {
                // 淺色強視覺（黑底）16.75:1 AAA。
                borderColor = Color.Cyan;
            }
            else if (TBInput.BackColor == Color.White)
            {
                // 深色強視覺（白底）11.16:1 AAA。
                borderColor = Color.MediumBlue;
            }
            else if (TBInput.IsDarkModeActive())
            {
                // TBInput 尚未反轉，視為深色中性背景。
                // 深色中性 / 懸停 ≥ 7.2:1 AAA。
                borderColor = Color.LightBlue;
            }
            else
            {
                // TBInput 尚未反轉，視為淺色中性背景。
                // 淺色中性 / 懸停 8.14:1 AAA。
                borderColor = Color.MediumBlue;
            }

            PInputHost.BackColor = borderColor;
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
        if (!_inputState.TryBeginReturning())
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
            await DelayIfNavigationUnavailableAsync(isNavigable);

            _inputState.EndReturning();
        }
    }

    /// <summary>
    /// 導航不可用時套用短暫冷卻，避免連續錯誤廣播
    /// </summary>
    /// <param name="isNavigable">導覽前是否可返回前景視窗。</param>
    /// <returns>非同步作業。</returns>
    private async Task DelayIfNavigationUnavailableAsync(bool isNavigable)
    {
        // 如果導航失敗（目標視窗消失），則多等待 1000ms 冷卻。
        // 這能防止因按鍵連點或長按導致的「連續報錯廣播」。
        if (isNavigable)
        {
            return;
        }

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

    /// <summary>
    /// 從快速鍵擷取模式還原 UI 狀態
    /// </summary>
    private void RestoreUIFromCaptureMode()
    {
        // 若目前不處於擷取模式，則不執行還原邏輯。
        // 這能防止鍵盤 Esc 與控制器 B 同時觸發取消時的重複 UI 重新整理。
        if (!_inputState.IsHotkeyCaptureActive)
        {
            return;
        }

        _inputState.EndHotkeyCapture();

        // 重新快取標題前綴（因為快速鍵可能已變更）。
        UpdateTitlePrefix();

        RestoreInputControlFromCaptureMode();
        RestoreCopyButtonFromCaptureMode();

        // 重置標題。
        UpdateTitle();
    }

    /// <summary>
    /// 從快速鍵擷取模式還原輸入框屬性與 A11y 描述
    /// </summary>
    private void RestoreInputControlFromCaptureMode()
    {
        TBInput.ReadOnly = false;
        TBInput.PlaceholderText = Strings.Pht_TBInput;
        TBInput.AccessibleName = Strings.A11y_TBInputName;
        TBInput.AccessibleDescription =
            string.IsNullOrWhiteSpace(_tbInputAccessibleDescriptionBeforeCapture) ?
                Strings.A11y_TBInputDesc :
                _tbInputAccessibleDescriptionBeforeCapture;
        _tbInputAccessibleDescriptionBeforeCapture = null;
        TBInput.ImeMode = ImeMode.On;

        UpdateBorderColor(TBInput.Focused);
    }

    /// <summary>
    /// 從快速鍵擷取模式還原複製按鈕狀態
    /// </summary>
    private void RestoreCopyButtonFromCaptureMode()
    {
        BtnCopy.Enabled = true;
        BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');
    }

    /// <summary>
    /// 更新視窗標題列文字
    /// </summary>
    /// <param name="suffix">要附加在標題後方的暫時性訊息（可選）</param>
    private void UpdateTitle(string? suffix = null)
    {
        string baseTitle = _cachedTitlePrefix;
        string? resolvedSuffix = ResolveTitleSuffix(suffix);

        if (!string.IsNullOrEmpty(resolvedSuffix))
        {
            Text = $"{baseTitle} - [{resolvedSuffix}]";

            return;
        }

        Text = ComposeConnectedDeviceTitle(baseTitle);
    }

    /// <summary>
    /// 解析標題尾綴文字，擷取模式下會自動補上提示
    /// </summary>
    /// <param name="suffix">呼叫端提供的尾綴文字。</param>
    /// <returns>解析後的尾綴文字。</returns>
    private string? ResolveTitleSuffix(string? suffix)
    {
        // 擷取模式專用處理：
        // 如果正在擷取中且未提供 suffix，自動補上「請按下一組按鍵」提示。
        if (_inputState.IsHotkeyCaptureActive &&
            string.IsNullOrEmpty(suffix))
        {
            return Strings.Msg_PressAnyKey;
        }

        return suffix;
    }

    /// <summary>
    /// 依控制器連線狀態組合標題列文字
    /// </summary>
    /// <param name="baseTitle">標題前綴文字。</param>
    /// <returns>最終標題字串。</returns>
    private string ComposeConnectedDeviceTitle(string baseTitle)
    {
        if (_gamepadController == null ||
            !_gamepadController.IsConnected)
        {
            return baseTitle;
        }

        // 當控制器連線時，顯示裝置名稱。
        return $"{baseTitle} · [{_gamepadController.DeviceName}]";
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

            _inputState.EndTouchKeyboard();
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

    /// <summary>
    /// 顯示說明對話框（WCAG 3.3.5 Help Mechanism）
    /// </summary>
    private void ShowHelpDialog()
    {
        try
        {
            using HelpDialog dialog = new()
            {
                GamepadController = _gamepadController
            };

            // 置於主視窗右側或上方（SmartPosition 會進行邊界修正）。
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(
                Left + Width + 8,
                Top);

            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "[說明] ShowHelpDialog 失敗");

            Debug.WriteLine($"[說明] ShowHelpDialog 失敗：{ex.Message}");
        }
    }
}