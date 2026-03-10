using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Services;
using InputBox.Core.Interop;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Media;

namespace InputBox;

// 阻擋設計工具。
partial class DesignerBlocker { };

public partial class MainForm
{
    private void MainForm_Activated(object sender, EventArgs e)
    {
        try
        {
            // 確保視窗還原時，文字方塊直接取得焦點，不用再點一次。
            // 使用 Interlocked 防止 Activated 瞬間多次觸發引發的焦點競爭。
            if (Interlocked.CompareExchange(ref _isProcessingActivated, 1, 0) == 0)
            {
                try
                {
                    if (WindowState == FormWindowState.Normal &&
                        TBInput != null &&
                        !TBInput.IsDisposed &&
                        TBInput.CanFocus)
                    {
                        TBInput.Focus();
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
            // 延遲 50ms 讓系統完成動畫與視窗狀態更新，再強制還原。
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
            InitializeGamepadControllerAsync().SafeFireAndForget(ex =>
            {
                Debug.WriteLine($"[啟動] 控制器初始化失敗：{ex.Message}");
            });

            // 啟動時自動取得焦點。
            TBInput.Focus();

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

            // 稍微延遲後播報狀態摘要（隱私模式與目前快速鍵）。
            // 這裡直接 await Task.Delay，結束後會自動回到 UI 執行緒，不需過度切換執行緒。
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
            if (e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.Accessibility)
            {
                this.SafeInvoke(() =>
                {
                    if (TBInput.Focused)
                    {
                        TBInput_Enter(TBInput, EventArgs.Empty);
                    }
                    else
                    {
                        UpdateBorderColor();
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
            // 如果正在擷取快速鍵，則不執行一般的進入變色邏輯，保留擷取模式的視覺狀態。
            if (_isCapturingHotkey != 0)
            {
                return;
            }

            // 判斷是否為高對比模式，如果系統已經開啟高對比，
            // 就完全尊重系統設定，不要自己改顏色。
            if (SystemInformation.HighContrast)
            {
                // 在高對比模式下，手動還原為系統配色，不進行自訂染色。
                TBInput.BackColor = SystemColors.Window;
                TBInput.ForeColor = SystemColors.WindowText;
                PInputHost.BackColor = SystemColors.Highlight;

                return;
            }

            // 背景改為「純黑色」。
            TBInput.BackColor = Color.Black;

            // 文字改為「純白色」。
            TBInput.ForeColor = Color.White;

            // 邊框維持系統高亮色（藍色）。
            PInputHost.BackColor = SystemColors.Highlight;
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
            // 如果正在擷取快速鍵時失去焦點，則取消擷取模式。
            if (_isCapturingHotkey != 0)
            {
                RestoreUIFromCaptureMode();

                // A11y 廣播：取消提示。
                AnnounceA11y(Strings.A11y_Capture_Cancelled);
            }

            // 還原為一般視窗背景（白色）。
            TBInput.BackColor = SystemColors.Window;
            // 還原為一般視窗文字（黑色）。
            TBInput.ForeColor = SystemColors.WindowText;

            // 設定邊框顏色為灰色。
            PInputHost.BackColor = SystemColors.ControlDark;
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
            ApplyButtonHoverStyle();
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
            ApplyButtonHoverStyle();
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
            // 當按鈕失去焦點時，還原視覺樣式。
            RestoreButtonDefaultStyle();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] BtnCopy_Leave 處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 套用按鈕高亮與加粗樣式
    /// </summary>
    private void ApplyButtonHoverStyle()
    {
        // 如果按鈕已停用，不執行樣式變更，避免干擾 Reset 流程。
        if (BtnCopy == null ||
            !BtnCopy.Enabled)
        {
            return;
        }

        // 安全保護：只有在尚未記錄原始字型時才進行紀錄。
        // 這能防止在按鈕已經是藍色（Highlight）的狀態下重複紀錄，導致「原始色」被覆蓋為「藍色」。
        if (_originalBtnFont == null)
        {
            _originalBtnFont = BtnCopy.Font;
            _originalBtnBackColor = BtnCopy.BackColor;
            _originalBtnForeColor = BtnCopy.ForeColor;
        }

        // A11y 視覺回饋 1：顏色變化。
        BtnCopy.BackColor = SystemColors.Highlight;
        BtnCopy.ForeColor = SystemColors.HighlightText;

        // A11y 視覺回饋 2：形狀與輪廓變化。
        _boldBtnFont ??= new Font(_originalBtnFont, FontStyle.Bold);

        BtnCopy.Font = _boldBtnFont;
    }

    /// <summary>
    /// 還原按鈕原始樣式。
    /// </summary>
    private void RestoreButtonDefaultStyle()
    {
        // 只有在我們確實擁有備份時才還原，防止在按鈕停用期間被誤觸發。
        if (_originalBtnFont == null)
        {
            return;
        }

        // 還原為原始顏色。
        BtnCopy.BackColor = _originalBtnBackColor;
        BtnCopy.ForeColor = _originalBtnForeColor;

        // 還原為原始字體粗細。
        BtnCopy.Font = _originalBtnFont;
    }

    private async void BtnCopy_Click(object sender, EventArgs e)
    {
        try
        {
            await PerformCopyAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[事件] 複製按鈕點擊處理失敗：{ex.Message}");

            AnnounceA11y(string.Format(Strings.A11y_Background_Error, ex.Message));
        }
    }

    /// <summary>
    /// 執行核心複製邏輯
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

            // 語音優化：將兩條相關訊息合併為一條發送，確保在焦點切換前使用者能聽到完整結果。
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
    /// 處理 KeyDown
    /// </summary>
    /// <param name="e">KeyEventArgs</param>
    private void HandleKeyDown(KeyEventArgs e)
    {
        // Alt + B：不複製直接返回前一個視窗（與遊戲手把 LB+RB+B 對等）。
        if (e.Alt && e.KeyCode == Keys.B)
        {
            HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();

            e.SuppressKeyPress = true;

            return;
        }

        // Esc：清除文字。
        if (e.KeyCode == Keys.Escape)
        {
            // UX 優化：如果文字框已經是空的，直接返回前一個視窗（隱藏視窗）。
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

            // 只有在第一行時，按「上」才觸發歷史紀錄。
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
            int currentLine = TBInput.GetLineFromCharIndex(TBInput.SelectionStart);

            // 取得文字方塊總共的行數（最後一行的 Index）。
            int totalLines = TBInput.GetLineFromCharIndex(TBInput.TextLength);

            // 只有在最後一行時，按「下」才觸發歷史紀錄。
            if (currentLine == totalLines)
            {
                NavigateHistory(+1);

                e.SuppressKeyPress = true;
            }

            return;
        }
    }

    /// <summary>
    /// 清除文字方塊
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
    /// 註冊全域快速鍵的內部方法，並在註冊失敗時顯示包含目前設定的快速鍵組合的錯誤訊息
    /// </summary>
    /// <returns>是否註冊成功</returns>
    private bool RegisterHotKeyInternal()
    {
        bool isOkay = GlobalHotKeyService.RegisterShowInputHotkey(Handle);

        if (!isOkay)
        {
            string currentHotkeyStr = GlobalHotKeyService.GetHotKeyDisplayString();

            // A11y 廣播：告知熱鍵註冊失敗。
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
    /// <param name="direction">數值</param>
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

            AnnounceA11y(direction < 0 ? Strings.A11y_Nav_Oldest : Strings.A11y_Nav_Newest);
        }

        // 處理文字更新。
        if (navigationResult.IsCleared)
        {
            TBInput.Clear();

            // 如果不是因為撞牆才清空，才獨立報讀（避免雙重語音）。
            if (!navigationResult.IsBoundaryHit)
            {
                AnnounceA11y(Strings.Msg_InputCleared);
            }
        }
        else if (navigationResult.Success &&
            navigationResult.Text != null)
        {
            TBInput.Text = navigationResult.Text;
            // 游標移到最後。
            TBInput.SelectionStart = TBInput.Text.Length;
            TBInput.ScrollToCaret();

            // 強制朗讀切換後的歷史紀錄內容，並包含索引資訊。
            // 索引 + 1 是為了讓使用者聽到 1-based 的數字。
            AnnounceA11y(string.Format(
                Strings.A11y_History_Navigation,
                navigationResult.CurrentIndex + 1,
                navigationResult.TotalCount,
                navigationResult.Text));
        }
    }

    /// <summary>
    /// 顯示觸控式鍵盤
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
    /// 顯示輸入
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
    /// 返回前一個視窗
    /// </summary>
    /// <param name="announce">是否執行 A11y 廣播（預設為 true）</param>
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
    /// 讓輸入框邊框閃爍與脈衝加粗
    /// </summary>
    /// <returns>Task</returns>
    private async Task FlashAlertAsync()
    {
        // 尊重系統動畫開關，避免對前庭神經系統敏感的使用者造成暈眩。
        // 同時檢查是否正在閃爍或視窗已被處置，防止重複觸發或報錯。
        if (!SystemInformation.UIEffectsEnabled ||
            IsDisposed ||
            !IsHandleCreated ||
            Interlocked.CompareExchange(ref _isFlashing, 1, 0) != 0)
        {
            return;
        }

        // 為避免實體座標位移（Margin）干擾眼動儀的停留點擊（Dwell-Click），
        // 改為記錄原始的 Padding（內部邊距），透過動態加粗邊框來產生「原位脈衝」效果。
        Padding originalPadding = PInputHost.Padding;

        try
        {
            // 決定閃爍顏色。
            Color flashColor;

            // 檢查系統是否開啟了「高對比模式」。
            if (SystemInformation.HighContrast)
            {
                flashColor = SystemColors.HighlightText;
            }
            else
            {
                // 一般模式：改用 OrangeRed，紅色盲看 OrangeRed 會像「亮黃色／亮褐色」，對比度高。
                flashColor = Color.OrangeRed;
            }

            // 效能優化：將與 DPI 相關的計算移出迴圈。
            float scale = DeviceDpi / BaseDpi;

            int pulseThickness = (int)Math.Max(4, 4 * scale),
                flashCount = 3,
                // 節奏維持 100ms，製造急促的警告感。
                interval = 100;

            for (int i = 0; i < flashCount; i++)
            {
                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                // 使用 SafeInvoke 確保即使從背景啟動閃爍也能安全執行。
                this.SafeInvoke(() =>
                {
                    if (IsDisposed ||
                        !IsHandleCreated)
                    {
                        return;
                    }

                    // A11y 第一重反饋：顏色警告。
                    PInputHost.BackColor = flashColor;

                    // A11y 第二重反饋：形狀／厚度變化（原位脈衝）。
                    PInputHost.Padding = new Padding(originalPadding.Left + pulseThickness);
                });

                // 脈衝維持時間。
                try
                {
                    await Task.Delay(interval, _formCts.Token);
                }
                catch (OperationCanceledException) { return; }

                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                // 還原顏色與厚度（製造一縮一放的跳動感）。
                this.SafeInvoke(() =>
                {
                    if (IsDisposed ||
                        !IsHandleCreated)
                    {
                        return;
                    }

                    UpdateBorderColor();

                    PInputHost.Padding = originalPadding;
                });

                try
                {
                    await Task.Delay(interval, _formCts.Token);
                }
                catch (OperationCanceledException) { return; }

                // 再次檢查狀態，確保下一次循環安全。
                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isFlashing, 0);

            // 無論是否成功完成（即使發生錯誤或中斷），都必須確保視覺狀態完全還原。
            if (!IsDisposed &&
                IsHandleCreated)
            {
                this.SafeInvoke(() =>
                {
                    if (IsDisposed ||
                        !IsHandleCreated)
                    {
                        return;
                    }

                    UpdateBorderColor();

                    PInputHost.Padding = originalPadding;
                });
            }
        }
    }

    /// <summary>
    /// 依據焦點狀態更新邊框顏色
    /// </summary>
    private void UpdateBorderColor()
    {
        if (TBInput.Focused)
        {
            // 取得焦點時：邊框變為系統高亮色（通常是藍色）。
            PInputHost.BackColor = SystemColors.Highlight;
        }
        else
        {
            // 失去焦點時：邊框變回一般的灰色。
            PInputHost.BackColor = SystemColors.ControlDark;
        }
    }

    /// <summary>
    /// 安全地處理返回前一個視窗的流程，避免重複觸發導致的問題
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

        // 還原邊框形狀與顏色（需根據 DPI 縮放以保持視覺一致性）。
        float scale = DeviceDpi / BaseDpi;

        int defaultPadding = (int)Math.Max(3, 3 * scale);

        PInputHost.Padding = new Padding(defaultPadding);

        UpdateBorderColor();

        // 還原按鈕狀態。
        BtnCopy.Enabled = true;
        BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');

        // 重置標題。
        UpdateTitle();
    }

    /// <summary>
    /// 依據目前的控制器狀態更新視窗標題
    /// </summary>
    private void UpdateTitle()
    {
        // 邏輯守衛：如果正在擷取快速鍵，則由擷取邏輯控制標題，此處不應覆蓋。
        if (_isCapturingHotkey != 0)
        {
            return;
        }

        if (_gamepadController == null ||
            !_gamepadController.IsConnected)
        {
            Text = _cachedTitlePrefix;

            return;
        }

        // 當控制器連線時，顯示裝置名稱。
        Text = $"{_cachedTitlePrefix} · [{_gamepadController.DeviceName}]";
    }

    /// <summary>
    /// 套用本地化
    /// </summary>
    private void ApplyLocalization()
    {
        // 設定視窗標題。
        Text = Strings.App_Title;

        // 設定按鈕文字。
        BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');

        // 設定 PlaceholderText。
        TBInput.PlaceholderText = Strings.Pht_TBInput;

        // 設定無障礙資訊。
        AccessibleName = Strings.A11y_MainFormName;
        AccessibleDescription = Strings.A11y_MainFormDesc;
        TBInput.AccessibleName = Strings.A11y_TBInputName;
        TBInput.AccessibleDescription = Strings.A11y_TBInputDesc;
        BtnCopy.AccessibleName = Strings.A11y_BtnCopyName;
        BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;
    }

    /// <summary>
    /// 重置觸控式鍵盤顯示旗標
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
    /// 彈出輸入視窗獲取整數數值
    /// </summary>
    /// <param name="title">視窗標題</param>
    /// <param name="currentValue">目前值</param>
    /// <param name="defaultValue">預設值</param>
    /// <param name="minimum">最小值</param>
    /// <param name="maximum">最大值</param>
    /// <returns>int?</returns>
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
    /// 彈出輸入視窗獲取浮點數數值
    /// </summary>
    /// <param name="title">視窗標題</param>
    /// <param name="currentValue">目前值</param>
    /// <param name="defaultValue">預設值</param>
    /// <param name="minimum">最小值</param>
    /// <param name="maximum">最大值</param>
    /// <returns>float?</returns>
    private float? AskForFloat(
        string title,
        float currentValue,
        float defaultValue,
        float minimum = 0.0f,
        float maximum = 1.0f)
    {
        using NumericInputDialog dialog = new(
            title,
            (decimal)currentValue,
            (decimal)defaultValue,
            2,
            0.1m,
            (decimal)minimum,
            (decimal)maximum);

        dialog.GamepadController = _gamepadController;

        // 傳入 this 作為 Owner，確保模態視窗行為正確。
        return dialog.ShowDialog(this) == DialogResult.OK ?
            (float)dialog.Value :
            null;
    }
}