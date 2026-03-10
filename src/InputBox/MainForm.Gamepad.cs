using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Diagnostics;
using System.Media;

namespace InputBox;

// 阻擋設計工具。
partial class DesignerBlocker { };

public partial class MainForm
{
    /// <summary>
    /// 初始化 GamepadController
    /// </summary>
    private async Task InitializeGamepadControllerAsync()
    {
        // 使用號誌確保一次只有一個初始化流程在運行，防止連點熱鍵引發的競爭。
        if (!await _gamepadInitLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            // 安全清理舊的控制器與輸入上下文實例。
            _gamepadController?.Dispose();
            _gamepadController = null;

            _inputContext?.Dispose();
            _inputContext = new FormInputContext(this);

            // 快取目前的設定快照，確保初始化過程中的參數一致性。
            AppSettings config = AppSettings.Current;

            // 建立 GamepadRepeatSettings。
            GamepadRepeatSettings gamepadRepeatSettings = new()
            {
                InitialDelayFrames = config.RepeatInitialDelayFrames,
                IntervalFrames = config.RepeatIntervalFrames
            };

            // 根據設定嘗試建立遊戲手把實作。
            if (config.GamepadProviderType == AppSettings.GamepadProvider.GameInput)
            {
                try
                {
                    // 嘗試非同步初始化 GameInput。
                    GameInputGamepadController controller = await GameInputGamepadController.CreateAsync(
                        _inputContext,
                        0,
                        gamepadRepeatSettings);

                    if (IsDisposed)
                    {
                        controller.Dispose();

                        return;
                    }

                    _gamepadController = controller;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[控制器] GameInput 初始化失敗，嘗試退避至 XInput：{ex.Message}");

                    // A11y：告知使用者已切換至相容模式。
                    AnnounceA11y(Strings.A11y_Gamepad_Fallback);

                    // 退避至 XInput。
                    CreateXInputController(gamepadRepeatSettings);
                }
            }
            else
            {
                // 預設使用 XInput 實作。
                CreateXInputController(gamepadRepeatSettings);
            }

            if (_gamepadController != null)
            {
                // 捕獲目前的控制器實例，確保 Lambda 內部存取的安全性。
                IGamepadController controller = _gamepadController;

                // A11y：當手把連線狀態改變時進行廣播。
                controller.ConnectionChanged += (isConnected) =>
                {
                    this.SafeInvoke(() =>
                    {
                        UpdateTitle();

                        // 防止重複廣播相同的連線狀態。
                        if (_lastGamepadConnectedState == isConnected)
                        {
                            return;
                        }

                        _lastGamepadConnectedState = isConnected;

                        // 取得目前控制器的索引。
                        uint index = 0;

                        if (AppSettings.Current.GamepadProviderType == AppSettings.GamepadProvider.XInput)
                        {
                            index = XInputGamepadController.GetFirstConnectedUserIndex();
                        }

                        string msg = isConnected ?
                            string.Format(Strings.A11y_Gamepad_Connected, index) :
                            string.Format(Strings.A11y_Gamepad_Disconnected, index);

                        AnnounceA11y(msg);
                    });
                };

                // 控制器事件綁定。
                controller.UpPressed += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Up"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    NavigateHistory(-1);
                });
                controller.DownPressed += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Down"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    NavigateHistory(+1);
                });
                controller.UpRepeat += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Up"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    NavigateHistory(-1);
                });
                controller.DownRepeat += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Down"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    NavigateHistory(+1);
                });
                controller.LeftPressed += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Left"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    MoveCursorLeft();
                });
                controller.LeftRepeat += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Left"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    MoveCursorLeft();
                });
                controller.RightPressed += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Right"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    MoveCursorRight();
                });
                controller.RightRepeat += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Right"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    MoveCursorRight();
                });

                // 啟動同步：確保從設定檔讀取的死區與重複速度立即生效。
                SyncGamepadSettings();

                controller.StartPressed += () =>
                {
                    this.SafeInvoke(() =>
                    {
                        if (HandleContextMenuGamepadInput("Confirm"))
                        {
                            return;
                        }

                        if (ActiveForm != this ||
                            _isCapturingHotkey != 0)
                        {
                            return;
                        }

                        if (TBInput.CanFocus &&
                            !TBInput.Focused)
                        {
                            TBInput.Focus();
                        }
                        else
                        {
                            ExecuteConfirmAction();
                        }
                    });
                };

                controller.BackPressed += () =>
                {
                    this.SafeInvoke(() =>
                    {
                        if (HandleContextMenuGamepadInput("Cancel"))
                        {
                            return;
                        }

                        if (ActiveForm != this ||
                            _isCapturingHotkey != 0)
                        {
                            return;
                        }

                        HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();
                    });
                };

                controller.APressed += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Confirm"))
                    {
                        return;
                    }

                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    ExecuteConfirmAction();
                });

                controller.BPressed += () =>
                {
                    this.SafeInvoke(() =>
                    {
                        if (HandleContextMenuGamepadInput("Cancel"))
                        {
                            return;
                        }

                        // 按鍵擷取模式下的取消處理。
                        if (_isCapturingHotkey != 0)
                        {
                            RestoreUIFromCaptureMode();

                            // A11y 廣播：告知擷取已取消。
                            AnnounceA11y(Strings.A11y_Capture_Cancelled);

                            // 播放警告音。
                            FeedbackService.PlaySound(SystemSounds.Beep);

                            return;
                        }

                        if (ActiveForm != this)
                        {
                            return;
                        }

                        if (controller.IsLeftShoulderHeld &&
                            controller.IsRightShoulderHeld)
                        {
                            HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();
                        }
                        else
                        {
                            ClearInput();
                        }
                    });
                };

                controller.YPressed += () => this.SafeInvoke(() =>
                {
                    if (ActiveForm != this ||
                        _isCapturingHotkey != 0)
                    {
                        return;
                    }

                    if (_cmsInput != null &&
                        !_cmsInput.Visible)
                    {
                        // 在文字方塊下方開啟選單。
                        _cmsInput.Show(this, new Point(TBInput.Left, TBInput.Bottom));

                        // 選取第一個有效的項目。
                        foreach (ToolStripItem item in _cmsInput.Items)
                        {
                            if (item.Enabled &&
                                item.Visible &&
                                item is not ToolStripSeparator)
                            {
                                item.Select();

                                break;
                            }
                        }

                        VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
                    }
                });

                controller.XPressed += () =>
                {
                    this.SafeInvoke(() =>
                    {
                        // 選單開啟時 X 鍵不執行刪除，除非有特殊需求。
                        if (_cmsInput != null &&
                            _cmsInput.Visible)
                        {
                            return;
                        }

                        if (ActiveForm != this ||
                            _isCapturingHotkey != 0)
                        {
                            return;
                        }

                        // 增加唯讀檢查，防止在擷取模式或其他唯讀狀態下修改文字。
                        if (TBInput == null ||
                            TBInput.IsDisposed)
                        {
                            return;
                        }

                        if (TBInput.ReadOnly)
                        {
                            FeedbackService.PlaySound(SystemSounds.Beep);

                            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                            return;
                        }

                        if (TBInput.SelectionLength > 0)
                        {
                            // 如果有選取範圍，直接刪除選取的文字。
                            int len = TBInput.SelectionLength;

                            string deletedSelection = TBInput.SelectedText;

                            TBInput.SelectedText = string.Empty;

                            // 語音最佳化：如果刪除內容太長，報讀字數而非內容。
                            if (len > 10)
                            {
                                AnnounceA11y(string.Format(Strings.A11y_Delete_Multiple, len));
                            }
                            else
                            {
                                AnnounceA11y(string.Format(Strings.A11y_Delete_Char, deletedSelection));
                            }
                        }
                        else if (TBInput.SelectionStart > 0)
                        {
                            // 如果沒有選取且游標不在最前面，刪除前一個字元。
                            int position = TBInput.SelectionStart;

                            char deletedChar = TBInput.Text[position - 1];

                            TBInput.Select(position - 1, 1);
                            TBInput.SelectedText = string.Empty;

                            AnnounceA11y(string.Format(Strings.A11y_Delete_Char, deletedChar));
                        }
                        else
                        {
                            // 撞牆（游標在最前面且沒選取文字）。
                            FeedbackService.PlaySound(SystemSounds.Beep);

                            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

                            AnnounceA11y(Strings.A11y_Cannot_Delete);
                        }
                    });
                };
            }

            UpdateTitle();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[控制器] 控制器系統初始化失敗：{ex.Message}");
        }
        finally
        {
            _gamepadInitLock.Release();
        }
    }

    /// <summary>
    /// 處理右鍵選單的控制器輸入
    /// </summary>
    /// <param name="action">動作名稱</param>
    /// <returns>是否已處理事件</returns>
    private bool HandleContextMenuGamepadInput(string action)
    {
        // 取得目前活躍的選單（可能包含子選單）。
        ToolStrip? activeTs = GetActiveToolStrip();

        if (activeTs == null) return false;

        switch (action)
        {
            case "Up":
                NavigateToolStrip(activeTs, false);

                return true;
            case "Down":
                NavigateToolStrip(activeTs, true);

                return true;
            case "Left":
                // 如果在子選單中，關閉子選單。
                if (activeTs is ToolStripDropDown dropDown &&
                    dropDown.OwnerItem != null)
                {
                    dropDown.Close();

                    return true;
                }

                return true;
            case "Right":
                // 如果目前項有子選單，開啟它。
                foreach (ToolStripItem item in activeTs.Items)
                {
                    if (item.Selected &&
                        item is ToolStripMenuItem tsmi &&
                        tsmi.HasDropDownItems)
                    {
                        tsmi.ShowDropDown();

                        if (tsmi.DropDown.Items.Count > 0)
                        {
                            NavigateToolStrip(tsmi.DropDown, true);
                        }

                        return true;
                    }
                }

                return true;
            case "Confirm":
                foreach (ToolStripItem item in activeTs.Items)
                {
                    if (item.Selected)
                    {
                        item.PerformClick();

                        return true;
                    }
                }

                return true;
            case "Cancel":
                _cmsInput?.Close();

                return true;
        }

        return false;
    }

    /// <summary>
    /// 取得目前活躍的 ToolStrip（針對右鍵選單及其子選單）
    /// </summary>
    /// <returns>ToolStrip?</returns>
    private ToolStrip? GetActiveToolStrip()
    {
        if (_cmsInput == null ||
            !_cmsInput.Visible)
        {
            return null;
        }

        return FindDeepestVisibleDropDown(_cmsInput);
    }

    /// <summary>
    /// 找出目前可見的最深層 DropDown，確保導覽與操作針對正確的選單層級
    /// </summary>
    /// <param name="root">ToolStrip</param>
    /// <returns>ToolStrip</returns>
    private static ToolStrip FindDeepestVisibleDropDown(ToolStrip root)
    {
        foreach (ToolStripItem item in root.Items)
        {
            if (item is ToolStripMenuItem tsmi &&
                tsmi.DropDown.Visible)
            {
                return FindDeepestVisibleDropDown(tsmi.DropDown);
            }
        }

        return root;
    }

    /// <summary>
    /// 在 ToolStrip 項目間導覽
    /// </summary>
    /// <param name="ts">ToolStrip</param>
    /// <param name="forward">是否向前導覽</param>
    private void NavigateToolStrip(ToolStrip ts, bool forward)
    {
        if (ts.Items.Count == 0)
        {
            return;
        }

        ToolStripItem? current = null;

        foreach (ToolStripItem item in ts.Items)
        {
            if (item.Selected)
            {
                current = item;

                break;
            }
        }

        int index = current != null ?
            ts.Items.IndexOf(current) :
            -1;
        int nextIndex = index;

        for (int i = 0; i < ts.Items.Count; i++)
        {
            nextIndex = forward ?
                (nextIndex + 1) % ts.Items.Count :
                (nextIndex - 1 + ts.Items.Count) % ts.Items.Count;

            ToolStripItem nextItem = ts.Items[nextIndex];

            if (nextItem.Enabled &&
                nextItem.Visible &&
                nextItem is not ToolStripSeparator)
            {
                nextItem.Select();

                VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

                return;
            }
        }
    }

    /// <summary>
    /// 建立 XInput 控制器實例
    /// </summary>
    private void CreateXInputController(GamepadRepeatSettings settings)
    {
        uint activeUserIndex = XInputGamepadController.GetFirstConnectedUserIndex();

        _gamepadController = new XInputGamepadController(_inputContext!, activeUserIndex, settings);
    }

    /// <summary>
    /// 執行確認操作（共用於 A 鍵與 Start 鍵）
    /// </summary>
    private void ExecuteConfirmAction()
    {
        if (string.IsNullOrWhiteSpace(TBInput.Text))
        {
            ShowTouchKeyboard();

            return;
        }

        if (!BtnCopy.Enabled)
        {
            return;
        }

        BtnCopy.PerformClick();
    }

    /// <summary>
    /// 游標左移
    /// </summary>
    private void MoveCursorLeft()
    {
        if (TBInput != null &&
            !TBInput.IsDisposed &&
            !string.IsNullOrEmpty(TBInput.Text) &&
            TBInput.SelectionStart > 0)
        {
            TBInput.SelectionStart--;
            TBInput.ScrollToCaret();

            // 手動報讀游標右側的字元，讓使用者知道跨過了什麼字。
            char crossedChar = TBInput.Text[TBInput.SelectionStart];

            AnnounceA11y(crossedChar.ToString(), interrupt: true);
        }
        else if (TBInput != null &&
            !TBInput.IsDisposed &&
            TBInput.SelectionStart == 0)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);

            // 只有在撞牆時才震動，避免長按時一直震動。
            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

            // 撞到最左邊。
            AnnounceA11y(Strings.A11y_Nav_Top, interrupt: true);
        }
    }

    /// <summary>
    /// 游標右移
    /// </summary>
    private void MoveCursorRight()
    {
        if (TBInput != null &&
            !TBInput.IsDisposed &&
            !string.IsNullOrEmpty(TBInput.Text) &&
            TBInput.SelectionStart < TBInput.Text.Length)
        {
            // 往右移之前，先抓取要跨過的字元。
            char crossedChar = TBInput.Text[TBInput.SelectionStart];

            TBInput.SelectionStart++;
            TBInput.ScrollToCaret();

            // 報讀該字元。
            AnnounceA11y(crossedChar.ToString(), interrupt: true);
        }
        else if (TBInput != null &&
            !TBInput.IsDisposed &&
            TBInput.SelectionStart == TBInput.Text.Length)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);

            // 只有在撞牆時才震動，避免長按時一直震動。
            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

            // 撞到最右邊。
            AnnounceA11y(Strings.A11y_Nav_Bottom, interrupt: true);
        }
    }

    /// <summary>
    /// 同步目前的設定至控制器實例
    /// </summary>
    private void SyncGamepadSettings()
    {
        if (_gamepadController == null)
        {
            return;
        }

        _gamepadController.ThumbDeadzoneEnter = AppSettings.Current.ThumbDeadzoneEnter;
        _gamepadController.ThumbDeadzoneExit = AppSettings.Current.ThumbDeadzoneExit;
        _gamepadController.RepeatSettings.InitialDelayFrames = AppSettings.Current.RepeatInitialDelayFrames;
        _gamepadController.RepeatSettings.IntervalFrames = AppSettings.Current.RepeatIntervalFrames;
    }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="profile">VibrationProfile</param>
    /// <returns>Task</returns>
    private Task VibrateAsync(VibrationProfile profile)
    {
        // 委派給 Service 處理。
        return FeedbackService.VibrateAsync(_gamepadController, profile);
    }
}