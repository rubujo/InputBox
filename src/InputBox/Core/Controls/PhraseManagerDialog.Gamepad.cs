using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
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
/// 片語管理對話框（遊戲控制器事件處理分部）。
/// <para>本分部檔案包含遊戲控制器事件訂閱、清單導覽、片語快捷跳轉與按鈕焦點循環等成員。</para>
/// </summary>
internal sealed partial class PhraseManagerDialog
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
            if (ReferenceEquals(_gamepadController, value))
            {
                return;
            }

            UnsubscribeGamepadEvents();

            _gamepadController = value;

            SubscribeGamepadEvents();
        }
    }

    /// <summary>
    /// 判斷片語管理對話框目前是否應接手控制器輸入。
    /// </summary>
    /// <returns>若對話框可安全處理控制器操作則回傳 true。</returns>
    private bool CanHandleGamepadInput()
    {
        return Visible &&
            !IsDisposed &&
            (ActiveForm == this ||
             ContainsFocus ||
             _lstPhrases.Focused ||
             _lstPhrases.ContainsFocus ||
             IsButtonAreaFocused());
    }

    /// <summary>
    /// 控制器向上輸入時在清單項目或按鈕焦點之間移動
    /// </summary>
    private void HandleUp() => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput())
            {
                return;
            }

            // 焦點在按鈕上時，D-Pad 上下切換焦點。
            if (IsButtonAreaFocused())
            {
                CycleFocus(forward: false);
                return;
            }

            if (_lstPhrases.Items.Count > 0)
            {
                int newIdx = _lstPhrases.SelectedIndex <= 0 ?
                    _lstPhrases.Items.Count - 1 :
                    _lstPhrases.SelectedIndex - 1;

                _lstPhrases.SelectedIndex = newIdx;

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleUp 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器向下輸入時在清單項目或按鈕焦點之間移動
    /// </summary>
    private void HandleDown() => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput())
            {
                return;
            }

            // 焦點在按鈕上時，D-Pad 上下切換焦點。
            if (IsButtonAreaFocused())
            {
                CycleFocus(forward: true);

                return;
            }

            if (_lstPhrases.Items.Count > 0)
            {
                int newIdx = _lstPhrases.SelectedIndex >= _lstPhrases.Items.Count - 1 ?
                    0 :
                    _lstPhrases.SelectedIndex + 1;

                _lstPhrases.SelectedIndex = newIdx;

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleDown 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器 A 鍵依目前焦點執行按鈕、插入片語或新增片語
    /// </summary>
    private void HandleGamepadA() => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput())
            {
                return;
            }

            // 如果焦點在按鈕上，執行該按鈕的動作。
            if (TryGetFocusedButton(out Button? focusedBtn) &&
                focusedBtn is { Enabled: true })
            {
                focusedBtn.PerformClick();
            }
            else if (_lstPhrases.Focused &&
            _lstPhrases.SelectedIndex >= 0)
            {
                InsertSelectedPhrase();
            }
            else
            {
                AddPhrase();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleGamepadA 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器取消動作時關閉片語管理對話框
    /// </summary>
    private void HandleClose() => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput())
            {
                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleClose 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器刪除動作時移除目前選取的片語
    /// </summary>
    private void HandleDelete() => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput())
            {
                return;
            }

            DeleteSelectedPhrase();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleDelete 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器新增動作時開啟片語建立流程
    /// </summary>
    private void HandleAdd() => this.SafeInvoke(() =>
    {
        try
        {
            if (GamescopeSurfaceRecovery.TryRecoverFromGamepadChord(
                this,
                RecreateHandle,
                _gamepadController,
                context: "PhraseManagerDialog Gamescope surface recovery 失敗"))
            {
                return;
            }

            if (!CanHandleGamepadInput())
            {
                return;
            }

            AddPhrase();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleAdd 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// LB／RB 捷徑切換左側片語清單中的上一筆或下一筆，讓使用者可快速瀏覽而不影響右側按鈕區焦點邏輯。
    /// </summary>
    /// <param name="delta">-1 代表上一筆，+1 代表下一筆。</param>
    private void HandlePhraseShortcutStep(int delta) => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput() ||
                _lstPhrases.Items.Count == 0)
            {
                return;
            }

            int currentIndex = _lstPhrases.SelectedIndex < 0 ?
                0 :
                _lstPhrases.SelectedIndex;
            int targetIndex = Math.Clamp(currentIndex + delta, 0, _lstPhrases.Items.Count - 1);

            _lstPhrases.Focus();

            if (targetIndex == _lstPhrases.SelectedIndex)
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                return;
            }

            _lstPhrases.SelectedIndex = targetIndex;

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandlePhraseShortcutStep 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// LT／RT 捷徑直接跳到左側片語清單的第一筆或最後一筆，加快大量片語時的巡覽效率。
    /// </summary>
    /// <param name="last">true 表示跳到最後一筆；false 表示跳到第一筆。</param>
    private void HandlePhraseShortcutBoundary(bool last) => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput() ||
                _lstPhrases.Items.Count == 0)
            {
                return;
            }

            int targetIndex = last ?
                _lstPhrases.Items.Count - 1 :
                0;

            _lstPhrases.Focus();

            if (targetIndex == _lstPhrases.SelectedIndex)
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                return;
            }

            _lstPhrases.SelectedIndex = targetIndex;

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandlePhraseShortcutBoundary 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器 LB 捷徑切換到上一個片語。
    /// </summary>
    private void HandlePreviousPhraseShortcut() => HandlePhraseShortcutStep(-1);

    /// <summary>
    /// 控制器 RB 捷徑切換到下一個片語。
    /// </summary>
    private void HandleNextPhraseShortcut() => HandlePhraseShortcutStep(1);

    /// <summary>
    /// 控制器 LT 捷徑直接跳到第一個片語。
    /// </summary>
    private void HandleFirstPhraseShortcut() => HandlePhraseShortcutBoundary(last: false);

    /// <summary>
    /// 控制器 RT 捷徑直接跳到最後一個片語。
    /// </summary>
    private void HandleLastPhraseShortcut() => HandlePhraseShortcutBoundary(last: true);

    /// <summary>
    /// 控制器左向輸入時回到清單或將片語上移
    /// </summary>
    private void HandleMoveUp() => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput())
            {
                return;
            }

            // 左方向鍵：若目前在右側按鈕區，回到清單。
            if (IsButtonAreaFocused())
            {
                _lstPhrases.Focus();

                if (_lstPhrases.SelectedIndex >= 0)
                {
                    IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

                    if (_lstPhrases.SelectedIndex < phrases.Count)
                    {
                        AnnounceA11y(
                            AppSettings.Current.IsPrivacyMode ?
                                Strings.Phrase_A11y_Selected_PrivacySafe :
                                string.Format(Strings.Phrase_A11y_Selected, phrases[_lstPhrases.SelectedIndex].Name),
                            interrupt: true);
                    }
                }

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                return;
            }

            MoveSelectedPhrase(-1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleMoveUp 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 控制器右向輸入時進入按鈕區或將片語下移
    /// </summary>
    private void HandleMoveDown() => this.SafeInvoke(() =>
    {
        try
        {
            if (!CanHandleGamepadInput())
            {
                return;
            }

            // 右方向鍵：由清單進入右側按鈕區；若已在按鈕區則向後循環。
            if (_lstPhrases.Focused ||
                _lstPhrases.ContainsFocus)
            {
                FocusFirstActionButtonOrClose();

                return;
            }

            if (IsButtonAreaFocused())
            {
                CycleFocus(forward: true);

                return;
            }

            MoveSelectedPhrase(1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] HandleMoveDown 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 在清單與按鈕之間循環焦點
    /// </summary>
    /// <param name="forward">是否向前循環焦點</param>
    private void CycleFocus(bool forward)
    {
        // 建立焦點順序：清單 → 各按鈕（依面板順序）→ 關閉按鈕。
        List<Control> focusOrder = [_lstPhrases];

        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn &&
                btn.Enabled &&
                btn.Visible)
            {
                focusOrder.Add(btn);
            }
        }

        if (_btnClose.Enabled && _btnClose.Visible)
        {
            focusOrder.Add(_btnClose);
        }

        if (focusOrder.Count == 0)
        {
            return;
        }

        // 找到目前焦點在列表中的位置。
        int currentIdx = -1;

        for (int i = 0; i < focusOrder.Count; i++)
        {
            if (focusOrder[i].Focused ||
                focusOrder[i].ContainsFocus)
            {
                currentIdx = i;

                break;
            }
        }

        int nextIdx;

        if (currentIdx < 0)
        {
            nextIdx = forward ?
                0 :
                focusOrder.Count - 1;
        }
        else
        {
            nextIdx = forward ?
                (currentIdx + 1) % focusOrder.Count :
                (currentIdx - 1 + focusOrder.Count) % focusOrder.Count;
        }

        focusOrder[nextIdx].Focus();

        // 播報焦點目標。
        string? name = focusOrder[nextIdx].AccessibleName ?? focusOrder[nextIdx].Text;

        if (!string.IsNullOrEmpty(name))
        {
            AnnounceA11y(name, interrupt: true);
        }

        FeedbackService.VibrateAsync(
            _gamepadController,
            VibrationPatterns.CursorMove,
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
    }

    /// <summary>
    /// 嘗試找出目前在按鈕區取得焦點的按鈕
    /// </summary>
    /// <param name="focusedBtn">輸出的焦點按鈕。</param>
    /// <returns>若找到焦點按鈕則回傳 true。</returns>
    private bool TryGetFocusedButton(out Button? focusedBtn)
    {
        if (_btnClose.Focused ||
            _btnClose.ContainsFocus)
        {
            focusedBtn = _btnClose;

            return true;
        }

        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn &&
                (btn.Focused || btn.ContainsFocus))
            {
                focusedBtn = btn;

                return true;
            }
        }

        focusedBtn = null;

        return false;
    }

    /// <summary>
    /// 判斷目前焦點是否位於按鈕區
    /// </summary>
    /// <returns>若按鈕區有焦點則回傳 true。</returns>
    private bool IsButtonAreaFocused() => TryGetFocusedButton(out _);

    /// <summary>
    /// 將焦點移至第一個可用動作按鈕，否則移至關閉按鈕。
    /// </summary>
    private void FocusFirstActionButtonOrClose()
    {
        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn &&
                btn.Enabled &&
                btn.Visible)
            {
                btn.Focus();

                AnnounceA11y(btn.AccessibleName ?? btn.Text, interrupt: true);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                return;
            }
        }

        if (_btnClose.Enabled &&
            _btnClose.Visible)
        {
            _btnClose.Focus();

            AnnounceA11y(_btnClose.AccessibleName ?? _btnClose.Text, interrupt: true);

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
    }

    /// <summary>
    /// 控制器連線狀態變更時更新恢復狀態並廣播無障礙訊息。
    /// </summary>
    /// <param name="connected">連線成功為 <see langword="true"/>，斷線為 <see langword="false"/>。</param>
    private void HandleGamepadConnectionChanged(bool connected)
    {
        try
        {
            if (connected)
            {
                _gamepadController?.Resume();
            }

            AnnounceA11y(connected ?
                string.Format(Strings.A11y_Gamepad_Connected, _gamepadController?.DeviceName) :
                string.Format(Strings.A11y_Gamepad_Disconnected, _gamepadController?.DeviceName));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] 控制器連線變更處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 訂閱片語管理對話框使用的控制器事件
    /// </summary>
    private void SubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

                _gamepadController.UpPressed += HandleUp;
                _gamepadController.UpRepeat += HandleUp;
                _gamepadController.DownPressed += HandleDown;
                _gamepadController.DownRepeat += HandleDown;
                _gamepadController.APressed += profile.ConfirmOnSouth ? HandleGamepadA : HandleClose;
                _gamepadController.StartPressed += HandleGamepadA;
                _gamepadController.BPressed += profile.ConfirmOnSouth ? HandleClose : HandleGamepadA;
                _gamepadController.BackPressed += HandleClose;
                _gamepadController.XPressed += HandleDelete;
                _gamepadController.YPressed += HandleAdd;
                _gamepadController.LeftPressed += HandleMoveUp;
                _gamepadController.LeftRepeat += HandleMoveUp;
                _gamepadController.RightPressed += HandleMoveDown;
                _gamepadController.RightRepeat += HandleMoveDown;
                _gamepadController.LeftShoulderPressed += HandlePreviousPhraseShortcut;
                _gamepadController.LeftShoulderRepeat += HandlePreviousPhraseShortcut;
                _gamepadController.RightShoulderPressed += HandleNextPhraseShortcut;
                _gamepadController.RightShoulderRepeat += HandleNextPhraseShortcut;
                _gamepadController.LeftTriggerPressed += HandleFirstPhraseShortcut;
                _gamepadController.RightTriggerPressed += HandleLastPhraseShortcut;
                _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] SubscribeGamepadEvents 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 解除片語管理對話框使用的控制器事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.UpPressed -= HandleUp;
                _gamepadController.UpRepeat -= HandleUp;
                _gamepadController.DownPressed -= HandleDown;
                _gamepadController.DownRepeat -= HandleDown;
                _gamepadController.APressed -= HandleGamepadA;
                _gamepadController.APressed -= HandleClose;
                _gamepadController.StartPressed -= HandleGamepadA;
                _gamepadController.BPressed -= HandleGamepadA;
                _gamepadController.BPressed -= HandleClose;
                _gamepadController.BackPressed -= HandleClose;
                _gamepadController.XPressed -= HandleDelete;
                _gamepadController.YPressed -= HandleAdd;
                _gamepadController.LeftPressed -= HandleMoveUp;
                _gamepadController.LeftRepeat -= HandleMoveUp;
                _gamepadController.RightPressed -= HandleMoveDown;
                _gamepadController.RightRepeat -= HandleMoveDown;
                _gamepadController.LeftShoulderPressed -= HandlePreviousPhraseShortcut;
                _gamepadController.LeftShoulderRepeat -= HandlePreviousPhraseShortcut;
                _gamepadController.RightShoulderPressed -= HandleNextPhraseShortcut;
                _gamepadController.RightShoulderRepeat -= HandleNextPhraseShortcut;
                _gamepadController.LeftTriggerPressed -= HandleFirstPhraseShortcut;
                _gamepadController.RightTriggerPressed -= HandleLastPhraseShortcut;
                _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] UnsubscribeGamepadEvents 失敗：{ex.Message}");
        }
    }
}
