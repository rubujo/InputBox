using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語編輯對話框（遊戲控制器與輸入分部）。
/// <para>本分部檔案包含遊戲控制器事件、欄位切換、游標移動、延伸選取、右鍵選單，以及觸控式鍵盤等輸入處理成員。</para>
/// </summary>
internal sealed partial class PhraseEditDialog
{
    /// <summary>
    /// 遊戲控制器
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IGamepadController? GamepadController
    {
        get => _gamepadController;
        set
        {
            if (ReferenceEquals(_gamepadController, value))
            {
                return;
            }

            UnsubscribeGamepadEvents();

            _gamepadController = value;

            if (_gamepadController != null)
            {
                GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

                _gamepadController.APressed += profile.ConfirmOnSouth ? HandleGamepadA : HandleBackOrClear;
                _gamepadController.StartPressed += HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed += profile.ConfirmOnSouth ? HandleBackOrClear : HandleGamepadA;
                _gamepadController.BackPressed += HandleCancel;
                _gamepadController.LeftPressed += HandleLeft;
                _gamepadController.LeftRepeat += HandleLeft;
                _gamepadController.RightPressed += HandleRight;
                _gamepadController.RightRepeat += HandleRight;
                _gamepadController.UpPressed += HandleFieldPrev;
                _gamepadController.DownPressed += HandleFieldNext;
                _gamepadController.XPressed += HandleBackspace;
                _gamepadController.YPressed += HandleOpenContextMenu;
                _gamepadController.RSLeftPressed += HandleRSLeft;
                _gamepadController.RSLeftRepeat += HandleRSLeft;
                _gamepadController.RSRightPressed += HandleRSRight;
                _gamepadController.RSRightRepeat += HandleRSRight;
                _gamepadController.ConnectionChanged += HandleConnectionChanged;
            }
        }
    }

    /// <summary>
    /// A 鍵：按鈕 → PerformClick；輸入框為空時開啟觸控鍵盤；其餘情境走確認驗證
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

            TextBox? tb = GetActiveTextBox() ?? _txtName;

            if (tb != null && string.IsNullOrWhiteSpace(tb.Text))
            {
                ShowTouchKeyboard(tb);

                return;
            }

            HandleConfirm();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 控制器 A 鍵失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// Start 鍵：在輸入框焦點時直接開啟觸控式鍵盤（可在已有內容時修改）
    /// </summary>
    private void HandleOpenTouchKeyboardFromGamepad() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox() ?? _txtName;

            ShowTouchKeyboard(tb);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 控制器開啟觸控鍵盤失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// B 鍵：若焦點在輸入框且有內容則優先清空；否則執行取消
    /// </summary>
    private void HandleBackOrClear() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb != null)
            {
                if (tb.SelectionLength > 0)
                {
                    tb.SelectedText = string.Empty;

                    _rsSelectionAnchor = null;

                    AnnounceA11y(Strings.Msg_InputCleared, interrupt: true);

                    FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ClearInput,
                        _cts?.Token ?? CancellationToken.None)
                        .SafeFireAndForget();

                    return;
                }

                if (!string.IsNullOrEmpty(tb.Text))
                {
                    tb.Clear();

                    _rsSelectionAnchor = null;

                    AnnounceA11y(Strings.Msg_InputCleared, interrupt: true);

                    FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ClearInput,
                        _cts?.Token ?? CancellationToken.None)
                        .SafeFireAndForget();

                    return;
                }
            }

            HandleCancel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleBackOrClear 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 聚焦指定輸入框並非同步開啟觸控式鍵盤。
    /// </summary>
    /// <param name="tb">要聚焦的輸入框。</param>
    private void ShowTouchKeyboard(TextBox tb)
    {
        if (tb.CanFocus && !tb.Focused)
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
                        Debug.WriteLine($"[片語編輯] 觸控鍵盤開啟失敗：{ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[片語編輯] 開啟觸控鍵盤失敗：{ex.Message}");
            }
        },
        _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
    }

    /// <summary>
    /// 取得目前焦點所在的 TextBox（名稱或內容）。
    /// </summary>
    /// <returns>具有焦點的輸入框；兩者皆無焦點時回傳 null。</returns>
    private TextBox? GetActiveTextBox()
    {
        if (_txtName.Focused)
        {
            return _txtName;
        }

        if (_txtContent.Focused)
        {
            return _txtContent;
        }

        return null;
    }

    /// <summary>
    /// 游標左移（比照 MainForm.Gamepad.cs 的 MoveCursorLeft）
    /// </summary>
    private void HandleLeft() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb == null)
            {
                // 焦點在按鈕區：D-Pad 左向在確認/取消按鈕間循環。
                if (_btnOk.Focused)
                {
                    _btnCancel.Focus();
                    AnnounceA11y(_btnCancel.AccessibleName ?? _btnCancel.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }
                else if (_btnCancel.Focused)
                {
                    _btnOk.Focus();
                    AnnounceA11y(_btnOk.AccessibleName ?? _btnOk.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }

                return;
            }

            bool hasSelection = tb.SelectionLength > 0;

            if (hasSelection ||
                tb.SelectionStart > 0)
            {
                if (hasSelection)
                {
                    tb.SelectionLength = 0;
                }
                else if (_gamepadController?.IsLeftShoulderHeld == true)
                {
                    tb.WordJump(false);
                }
                else
                {
                    tb.SelectionStart--;
                }

                tb.ScrollToCaret();

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
            else
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 左移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 游標右移（比照 MainForm.Gamepad.cs 的 MoveCursorRight）
    /// </summary>
    private void HandleRight() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb == null)
            {
                // 焦點在按鈕區：D-Pad 右向在取消/確認按鈕間循環。
                if (_btnCancel.Focused)
                {
                    _btnOk.Focus();
                    AnnounceA11y(_btnOk.AccessibleName ?? _btnOk.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }
                else if (_btnOk.Focused)
                {
                    _btnCancel.Focus();
                    AnnounceA11y(_btnCancel.AccessibleName ?? _btnCancel.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }

                return;
            }

            bool hasSelection = tb.SelectionLength > 0;

            if (hasSelection || tb.SelectionStart < tb.Text.Length)
            {
                if (hasSelection)
                {
                    tb.SelectionStart += tb.SelectionLength;
                    tb.SelectionLength = 0;
                }
                else if (_gamepadController?.IsLeftShoulderHeld == true)
                {
                    tb.WordJump(true);
                }
                else
                {
                    tb.SelectionStart++;
                }

                tb.ScrollToCaret();

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
            else
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 右移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 上方向鍵：在名稱欄位與內容欄位和按鈕之間切換焦點
    /// </summary>
    private void HandleFieldPrev() => this.SafeInvoke(() =>
    {
        try
        {
            if (_txtContent.Focused)
            {
                _txtName.Focus();
            }
            else if (_btnOk.Focused)
            {
                _txtContent.Focus();
            }
            else if (_btnCancel.Focused)
            {
                _btnOk.Focus();
            }
            else if (_txtName.Focused)
            {
                _btnCancel.Focus();
            }

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 欄位向前失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 下方向鍵：在名稱欄位、內容欄位與按鈕之間循環焦點
    /// </summary>
    private void HandleFieldNext() => this.SafeInvoke(() =>
    {
        try
        {
            if (_txtName.Focused)
            {
                _txtContent.Focus();
            }
            else if (_txtContent.Focused)
            {
                _btnOk.Focus();
            }
            else if (_btnOk.Focused)
            {
                _btnCancel.Focus();
            }
            else if (_btnCancel.Focused)
            {
                _txtName.Focus();
            }

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 欄位向後失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// X 鍵：刪除選取文字或游標前一字元（比照 MainForm.Gamepad.cs 的 X 鍵刪除邏輯）
    /// </summary>
    private void HandleBackspace() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb == null ||
                tb.ReadOnly)
            {
                return;
            }

            if (tb.SelectionLength > 0)
            {
                tb.SelectedText = string.Empty;
            }
            else if (tb.SelectionStart > 0)
            {
                int pos = tb.SelectionStart;

                tb.Select(pos - 1, 1);
                tb.SelectedText = string.Empty;
            }
            else
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 退格失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 右搖桿左推：擴張選取範圍向左（比照 MainForm.Gamepad.cs 的 ExpandSelection）
    /// </summary>
    private void HandleRSLeft() => this.SafeInvoke(() =>
    {
        try
        {
            ExpandSelection(-1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 右摘桿左移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 右搖桿右推：擴張選取範圍向右
    /// </summary>
    private void HandleRSRight() => this.SafeInvoke(() =>
    {
        try
        {
            ExpandSelection(1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 右摘桿右移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// Y 鍵：開啟焦點 TextBox 的原生右鍵選單
    /// </summary>
    private void HandleOpenContextMenu() => this.SafeInvoke(() =>
    {
        try
        {
            if (GamescopeSurfaceRecovery.TryRecoverFromGamepadChord(
                this,
                RecreateHandle,
                _gamepadController,
                beforeRecover: CloseActiveTextBoxContextMenu,
                context: "PhraseEditDialog Gamescope surface recovery 失敗"))
            {
                return;
            }

            TextBox? tb = GetActiveTextBox();

            if (tb == null)
            {
                return;
            }

            // 在游標位置附近顯示內建右鍵選單。
            Point caretPos = tb.GetPositionFromCharIndex(tb.SelectionStart);

            tb.ContextMenuStrip?.Show(tb, caretPos);

            // TextBox 沒有 ContextMenuStrip 時，透過模擬 Shift+F10 觸發原生選單。
            if (tb.ContextMenuStrip == null)
            {
                User32.SendMessage(tb.Handle, 0x007B, tb.Handle, unchecked((nint)0xFFFFFFFF));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 開啟右鍵選單失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 在重建 PhraseEditDialog surface 前關閉目前 TextBox 的右鍵選單。
    /// </summary>
    private void CloseActiveTextBoxContextMenu()
    {
        GetActiveTextBox()?.ContextMenuStrip?.Close();
    }

    /// <summary>
    /// 擴張或縮減文字選取範圍（比照 MainForm.Gamepad.cs 的 ExpandSelection）
    /// </summary>
    /// <param name="direction">方向，正數表示向右擴張，負數表示向左擴張。</param>
    private void ExpandSelection(int direction)
    {
        TextBox? tb = GetActiveTextBox();

        if (tb == null)
        {
            return;
        }

        (int anchor, int caret) = tb.ResolveSelectionAnchor(_rsSelectionAnchor);

        _rsSelectionAnchor = anchor;

        int safeDirection = Math.Sign(direction);
        bool wordGranularity = _gamepadController?.IsLeftShoulderHeld == true ||
                               _gamepadController?.IsRightShoulderHeld == true;

        int newCaret = wordGranularity ?
            tb.GetWordJumpTarget(caret, safeDirection > 0) :
            Math.Clamp(caret + safeDirection, 0, tb.TextLength);

        if (newCaret == caret)
        {
            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.ActionFail,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            return;
        }

        // 使用 Win32 EM_SETSEL 正確設定選取範圍（含反向選取）。
        tb.SetSelectionWithActiveEdge(anchor, newCaret);

        PlaySelectionFeedback(safeDirection, wordGranularity);
    }

    /// <summary>
    /// 推進某類觸覺回饋的 burst 等級，用於快速連發時做輕量阻尼。
    /// </summary>
    /// <param name="lastUtc">上次觸發的 UTC 時間戳（ref，會被更新）。</param>
    /// <param name="burstLevel">目前 burst 等級（ref，會被累加或重設）。</param>
    /// <param name="fastWindowMs">視為快速連發的時間視窗（毫秒）。</param>
    /// <returns>更新後的 burst 等級（0 ~ 3）。</returns>
    private static int AdvanceFeedbackBurst(ref DateTime lastUtc, ref int burstLevel, int fastWindowMs)
    {
        DateTime now = DateTime.UtcNow;
        burstLevel = (now - lastUtc).TotalMilliseconds <= fastWindowMs ?
            Math.Min(burstLevel + 1, 3) :
            0;
        lastUtc = now;

        return burstLevel;
    }

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

        int burstLevel = wordGranularity ?
            0 :
            AdvanceFeedbackBurst(ref _lastSelectionFeedbackUtc, ref _selectionFeedbackBurstLevel, SelectionBurstWindowMs);

        FeedbackService.VibrateSequenceAsync(
            controller,
            VibrationPatterns.GetSelectionSequence(direction, wordGranularity, burstLevel, controller.VibrationMotorSupport),
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();

        FeedbackService.PlaySelectionCue(wordGranularity, burstLevel);
    }

    /// <summary>
    /// 控制器重新連線後恢復輪詢狀態。
    /// </summary>
    /// <param name="connected">新的控制器連線狀態。</param>
    private void HandleConnectionChanged(bool connected)
    {
        try
        {
            if (connected)
            {
                _gamepadController?.Resume();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 控制器連線變更：{ex.Message}");
        }
    }

    /// <summary>
    /// 解除目前片語編輯對話框所綁定的控制器事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.APressed -= HandleGamepadA;
                _gamepadController.APressed -= HandleBackOrClear;
                _gamepadController.StartPressed -= HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed -= HandleGamepadA;
                _gamepadController.BPressed -= HandleBackOrClear;
                _gamepadController.BackPressed -= HandleCancel;
                _gamepadController.LeftPressed -= HandleLeft;
                _gamepadController.LeftRepeat -= HandleLeft;
                _gamepadController.RightPressed -= HandleRight;
                _gamepadController.RightRepeat -= HandleRight;
                _gamepadController.UpPressed -= HandleFieldPrev;
                _gamepadController.DownPressed -= HandleFieldNext;
                _gamepadController.XPressed -= HandleBackspace;
                _gamepadController.YPressed -= HandleOpenContextMenu;
                _gamepadController.RSLeftPressed -= HandleRSLeft;
                _gamepadController.RSLeftRepeat -= HandleRSLeft;
                _gamepadController.RSRightPressed -= HandleRSRight;
                _gamepadController.RSRightRepeat -= HandleRSRight;
                _gamepadController.ConnectionChanged -= HandleConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] UnsubscribeGamepadEvents 失敗：{ex.Message}");
        }
    }
}
