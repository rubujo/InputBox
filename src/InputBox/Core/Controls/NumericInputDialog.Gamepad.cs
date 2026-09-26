using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Resources;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 專門用於數值輸入的對話框（遊戲控制器與輸入分部）。
/// <para>本分部檔案包含遊戲控制器事件、數值加減、游標移動、延伸選取、刪除，以及觸控式鍵盤等輸入處理成員。</para>
/// </summary>
internal sealed partial class NumericInputDialog
{
    /// <summary>
    /// 設定控制器實作，並訂閱事件
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IGamepadController? GamepadController
    {
        get => _gamepadController;
        set
        {
            // 如果控制器相同，不執行任何操作。
            if (ReferenceEquals(_gamepadController, value))
            {
                return;
            }

            // 安全清理舊控制器的訂閱。
            UnsubscribeGamepadEvents();

            _gamepadController = value;

            if (_gamepadController != null)
            {
                GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

                // 訂閱新控制器事件。
                _gamepadController.UpPressed += HandlePlus;
                _gamepadController.UpRepeat += HandlePlus;
                _gamepadController.DownPressed += HandleMinus;
                _gamepadController.DownRepeat += HandleMinus;
                _gamepadController.LeftPressed += HandleLeft;
                _gamepadController.LeftRepeat += HandleLeft;
                _gamepadController.RightPressed += HandleRight;
                _gamepadController.RightRepeat += HandleRight;
                _gamepadController.RSLeftPressed += HandleRSLeft;
                _gamepadController.RSLeftRepeat += HandleRSLeft;
                _gamepadController.RSRightPressed += HandleRSRight;
                _gamepadController.RSRightRepeat += HandleRSRight;
                _gamepadController.APressed += profile.ConfirmOnSouth ? HandleGamepadA : HandleCancel;
                _gamepadController.StartPressed += HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed += profile.ConfirmOnSouth ? HandleCancel : HandleGamepadA;
                _gamepadController.BackPressed += HandleCancel;
                _gamepadController.XPressed += HandleBackspace;
                _gamepadController.YPressed += HandleReset;
                // 已由 D‑Pad (`LeftPressed` / `RightPressed`) 處理游標移動，移除 LT/RT 綁定以避免語意重複。
                _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
            }
        }
    }

    /// <summary>
    /// 取消訂閱控制器事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.UpPressed -= HandlePlus;
                _gamepadController.UpRepeat -= HandlePlus;
                _gamepadController.DownPressed -= HandleMinus;
                _gamepadController.DownRepeat -= HandleMinus;
                _gamepadController.LeftPressed -= HandleLeft;
                _gamepadController.LeftRepeat -= HandleLeft;
                _gamepadController.RightPressed -= HandleRight;
                _gamepadController.RightRepeat -= HandleRight;
                _gamepadController.RSLeftPressed -= HandleRSLeft;
                _gamepadController.RSLeftRepeat -= HandleRSLeft;
                _gamepadController.RSRightPressed -= HandleRSRight;
                _gamepadController.RSRightRepeat -= HandleRSRight;
                _gamepadController.APressed -= HandleGamepadA;
                _gamepadController.APressed -= HandleCancel;
                _gamepadController.StartPressed -= HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed -= HandleGamepadA;
                _gamepadController.BPressed -= HandleCancel;
                _gamepadController.BackPressed -= HandleCancel;
                _gamepadController.XPressed -= HandleBackspace;
                _gamepadController.YPressed -= HandleReset;
                _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] 取消訂閱控制器事件失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理控制器連線狀態變更
    /// </summary>
    /// <param name="connected">是否已連線</param>
    private void HandleGamepadConnectionChanged(bool connected)
    {
        try
        {
            if (connected)
            {
                _gamepadController?.Resume();
            }

            // 告知使用者控制器連線狀態變更。
            AnnounceA11y(connected ?
                string.Format(Strings.A11y_Gamepad_Connected, _gamepadController?.DeviceName) :
                string.Format(Strings.A11y_Gamepad_Disconnected, _gamepadController?.DeviceName));
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "NumericInputDialog.HandleGamepadConnectionChanged 失敗");

            Debug.WriteLine($"[NumericInputDialog] 控制器連線變更處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理邊界撞擊效果
    /// </summary>
    /// <param name="isUpperLimit">是否為上限</param>
    internal void HandleBoundaryHit(bool isUpperLimit)
    {
        this.SafeInvoke(() =>
        {
            try
            {
                // 利用 _isFlashing 作為防呆機制，避免控制器長按連發時造成音效與語音播報卡頓。
                if (_isFlashing == 0)
                {
                    FeedbackService.PlaySound(SystemSounds.Beep);

                    FeedbackService.VibrateAsync(
                            _gamepadController,
                            VibrationPatterns.ActionFail,
                            _cts?.Token ?? CancellationToken.None)
                        .SafeFireAndForget();

                    AnnounceA11y(isUpperLimit ?
                        Strings.A11y_Value_Max :
                        Strings.A11y_Value_Min, true);

                    FlashAlertAsync().SafeFireAndForget();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] HandleBoundaryHit 失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 處理數值增加，並保留焦點以支援眼動儀連發與鍵盤連點
    /// </summary>
    private void HandlePlus() => this.SafeInvoke(() =>
    {
        try
        {
            _nud?.UpButton();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandlePlus 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理數值減少，並保留焦點以支援眼動儀連發與鍵盤連點
    /// </summary>
    private void HandleMinus() => this.SafeInvoke(() =>
    {
        try
        {
            _nud?.DownButton();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleMinus 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理游標移動
    /// </summary>
    /// <param name="forward">是否向右（前進）移動</param>
    private void MoveCaret(bool forward) => this.SafeInvoke(() =>
    {
        try
        {
            if (_nud == null)
            {
                return;
            }

            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (textBox == null ||
                textBox.IsDisposed)
            {
                return;
            }

            bool hasSelection = textBox.SelectionLength > 0;

            bool canMove = forward ?
                (hasSelection || textBox.SelectionStart < textBox.TextLength) :
                (hasSelection || textBox.SelectionStart > 0);

            if (canMove)
            {
                if (hasSelection)
                {
                    if (forward)
                    {
                        textBox.SelectionStart += textBox.SelectionLength;
                    }

                    textBox.SelectionLength = 0;
                }
                // 組合鍵：任一肩鍵 + 方向鍵 執行單字跳轉。
                else if (_gamepadController?.IsLeftShoulderHeld == true ||
                         _gamepadController?.IsRightShoulderHeld == true)
                {
                    textBox.WordJump(forward);
                }
                else
                {
                    if (forward)
                    {
                        textBox.SelectionStart++;
                    }
                    else
                    {
                        textBox.SelectionStart--;
                    }
                }

                textBox.ScrollToCaret();

                // 取得目前游標位置（1-based 報讀）。
                int pos = textBox.SelectionStart;

                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.A11y_Cursor_Move_PrivacySafe :
                    string.Format(Strings.A11y_Cursor_Move, pos + 1), true);

                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.CursorMove,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
            else
            {
                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ActionFail,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] MoveCaret 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理游標左移
    /// </summary>
    private void HandleLeft() => MoveCaret(false);

    /// <summary>
    /// 處理游標右移
    /// </summary>
    private void HandleRight() => MoveCaret(true);

    /// <summary>
    /// 處理選取範圍擴張
    /// </summary>
    /// <param name="forward">是否向右擴張</param>
    private void ExpandSelection(bool forward) => this.SafeInvoke(() =>
    {
        try
        {
            if (_nud == null)
            {
                return;
            }

            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (textBox == null ||
                textBox.IsDisposed)
            {
                return;
            }

            // 當目前沒有選取範圍，或是目前的選取範圍與我們的錨點不匹配時，重新設定錨點，並推算活動邊緣。
            (int anchor, int caret) = textBox.ResolveSelectionAnchor(_rsSelectionAnchor);

            _rsSelectionAnchor = anchor;

            int direction = forward ? 1 : -1;
            bool wordGranularity = _gamepadController?.IsLeftShoulderHeld == true ||
                                   _gamepadController?.IsRightShoulderHeld == true;

            int newCaret = wordGranularity ?
                textBox.GetWordJumpTarget(caret, direction > 0) :
                Math.Clamp(caret + direction, 0, textBox.TextLength);

            if (newCaret == caret)
            {
                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ActionFail,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                return;
            }

            // 使用 Win32 EM_SETSEL 設定選取範圍。
            textBox.SetSelectionWithActiveEdge(anchor, newCaret);

            if (textBox.SelectionLength > 0)
            {
                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.A11y_Selected_Text_PrivacySafe :
                    string.Format(Strings.A11y_Selected_Text, textBox.SelectedText), true);
            }

            PlaySelectionFeedback(direction, wordGranularity);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] ExpandSelection 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 根據選取粒度與速度播放不同的右搖桿文字選取回饋。
    /// </summary>
    /// <param name="direction">選取方向；負值為向左，正值為向右。</param>
    /// <param name="wordGranularity">是否為單字粒度選取。</param>
    private void PlaySelectionFeedback(int direction, bool wordGranularity)
    {
        IGamepadController? controller = _gamepadController;

        if (controller == null ||
            !controller.IsConnected)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        _selectionFeedbackBurstLevel = wordGranularity ?
            0 :
            (now - _lastSelectionFeedbackUtc).TotalMilliseconds <= SelectionBurstWindowMs ?
                Math.Min(_selectionFeedbackBurstLevel + 1, 3) :
                0;
        _lastSelectionFeedbackUtc = now;

        FeedbackService.VibrateSequenceAsync(
            controller,
            VibrationPatterns.GetSelectionSequence(direction, wordGranularity, _selectionFeedbackBurstLevel, controller.VibrationMotorSupport),
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();

        FeedbackService.PlaySelectionCue(wordGranularity, _selectionFeedbackBurstLevel);
    }

    /// <summary>
    /// 處理選取左向擴張
    /// </summary>
    private void HandleRSLeft() => ExpandSelection(false);

    /// <summary>
    /// 處理選取右向擴張
    /// </summary>
    private void HandleRSRight() => ExpandSelection(true);

    /// <summary>
    /// 處理刪除按鍵（Backspace 與 Delete）的共用邏輯
    /// </summary>
    /// <param name="textBox">目標 TextBox</param>
    /// <param name="isBackspace">是否為 Backspace 鍵</param>
    private void HandleDeleteKey(TextBox textBox, bool isBackspace)
    {
        try
        {
            // 擷取刪除前的狀態。
            string oldText = textBox.Text;

            int oldStart = textBox.SelectionStart,
                oldLen = textBox.SelectionLength;

            // 檢查是否可以刪除。
            bool cannotDelete = isBackspace ?
                (oldLen == 0 && oldStart == 0) :
                (oldLen == 0 && oldStart == oldText.Length);

            if (cannotDelete)
            {
                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ActionFail,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                if (isBackspace)
                {
                    AnnounceA11y(Strings.A11y_Cannot_Delete, true);
                }

                return;
            }

            this.SafeBeginInvoke(() =>
            {
                try
                {
                    if (textBox.IsDisposed)
                    {
                        return;
                    }

                    // 比對內容是否真的減少。
                    if (textBox.TextLength < oldText.Length)
                    {
                        if (oldLen > 0)
                        {
                            AnnounceA11y(string.Format(Strings.A11y_Delete_Multiple, oldLen), true);
                        }
                        else
                        {
                            int deleteIndex = isBackspace ?
                                oldStart - 1 :
                                oldStart;

                            if (deleteIndex >= 0 &&
                                deleteIndex < oldText.Length)
                            {
                                if (AppSettings.Current.IsPrivacyMode)
                                {
                                    AnnounceA11y(Strings.A11y_Delete_Char_PrivacySafe, true);
                                }
                                else
                                {
                                    char deletedChar = oldText[deleteIndex];

                                    AnnounceA11y(string.Format(Strings.A11y_Delete_Char, deletedChar), true);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] HandleDeleteKey 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleDeleteKey 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// A 鍵：按鈕 → PerformClick；輸入框為空時開啟觸控鍵盤；其餘情境走確認驗證。
    /// </summary>
    private void HandleGamepadA() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveControl is Button btn)
            {
                btn.PerformClick();
                return;
            }

            if (_nud != null && (_nud.Focused || _nud.ContainsFocus))
            {
                TextBox? tb = _nud.Controls.OfType<TextBox>().FirstOrDefault();
                if (tb != null && string.IsNullOrWhiteSpace(tb.Text))
                {
                    ShowTouchKeyboard(tb);
                    return;
                }
            }

            HandleConfirm();
        }
        catch (Exception ex) { Debug.WriteLine($"[NumericInputDialog] HandleGamepadA 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// Start 鍵：在輸入框焦點時直接開啟觸控式鍵盤
    /// </summary>
    private void HandleOpenTouchKeyboardFromGamepad() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = _nud?.Controls.OfType<TextBox>().FirstOrDefault();
            if (tb != null)
            {
                ShowTouchKeyboard(tb);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[NumericInputDialog] HandleOpenTouchKeyboardFromGamepad 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// 聚焦指定輸入框並非同步開啟觸控式鍵盤。
    /// </summary>
    /// <param name="tb">要聚焦的輸入框。</param>
    private void ShowTouchKeyboard(TextBox tb)
    {
        if (tb.CanFocus &&
            !tb.Focused)
        {
            tb.Focus();
        }

        AnnounceA11y(Strings.A11y_Opening_Keyboard, interrupt: true);

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, _cts?.Token ?? CancellationToken.None);

                if (TouchKeyboardService.IsVisible()) return;

                await this.SafeInvokeAsync(() =>
                {
                    try
                    {
                        bool opened = TouchKeyboardService.TryOpen();

                        if (opened)
                        {
                            FeedbackService.PlaySound(SystemSounds.Asterisk);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NumericInputDialog] ShowTouchKeyboard 內層失敗: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] ShowTouchKeyboard 背景工作失敗: {ex.Message}");
            }
        }, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
    }

    /// <summary>
    /// X 鍵：刪除選取文字或游標前一字元
    /// </summary>
    private void HandleBackspace() => this.SafeInvoke(() =>
    {
        try
        {
            if (_nud == null)
            {
                return;
            }

            TextBox? tb = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (tb == null ||
                tb.IsDisposed ||
                tb.ReadOnly)
            {
                return;
            }

            // 實施手動字串處理以符合「不模擬按鍵」的安全性紅線。
            // 透過直接操作 TextBox.Text，能觸發 NumericUpDown 的內部驗證機制且不依賴 Win32 訊息注入。
            if (tb.SelectionLength > 0)
            {
                tb.SelectedText = string.Empty;
            }
            else if (tb.SelectionStart > 0)
            {
                int start = tb.SelectionStart;

                tb.Text = tb.Text.Remove(start - 1, 1);
                tb.SelectionStart = start - 1;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleBackspace 失敗: {ex.Message}");
        }
    });

    /// <summary>
    /// LT 鍵：向左導覽游標
    /// </summary>
    private void HandleLTNav() => MoveCaret(false);

    /// <summary>
    /// RT 鍵：向右導覽游標
    /// </summary>
    private void HandleRTNav() => MoveCaret(true);
}
