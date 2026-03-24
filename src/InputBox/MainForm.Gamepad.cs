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
    /// 追蹤 Back 鍵是否已作為組合鍵使用（用於防止放開時觸發返回動作）
    /// </summary>
    private bool _isBackUsedAsModifier = false;

    /// <summary>
    /// 初始化 GamepadController
    /// </summary>
    private async Task InitializeGamepadControllerAsync()
    {
        SemaphoreSlim? initLock = _gamepadInitLock;

        // 使用號誌確保一次只有一個初始化流程在運行，防止連點熱鍵引發的競爭。
        if (initLock == null ||
            !await initLock.WaitAsync(0))
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

            // 根據設定嘗試建立遊戲控制器實作。
            if (config.GamepadProviderType == AppSettings.GamepadProvider.GameInput)
            {
                try
                {
                    // 嘗試初始化 GameInput。
                    GameInputGamepadController controller = await GameInputGamepadController.CreateAsync(
                        _inputContext,
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

                    // 告知使用者已切換至相容模式。
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

                // 當手把連線狀態改變時進行廣播。
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
                controller.BackPressed += () => this.SafeInvoke(() =>
                {
                    // 按下時重置旗標。
                    _isBackUsedAsModifier = false;
                });

                controller.BackReleased += () => this.SafeInvoke(() =>
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

                    // 如果在按住 Back 期間使用了組合鍵（如 Back + Up），放開時就不觸發返回。
                    if (_isBackUsedAsModifier)
                    {
                        _isBackUsedAsModifier = false;

                        return;
                    }

                    HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();
                });

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

                    // 組合鍵：Back + Up 增加不透明度（5%）。
                    if (controller.IsBackHeld)
                    {
                        _isBackUsedAsModifier = true;

                        AdjustOpacity(0.05f);

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

                    // 組合鍵：Back + Down 減少不透明度（5%）。
                    if (controller.IsBackHeld)
                    {
                        _isBackUsedAsModifier = true;

                        AdjustOpacity(-0.05f);

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

                    // 組合鍵連發。
                    if (controller.IsBackHeld)
                    {
                        _isBackUsedAsModifier = true;

                        AdjustOpacity(0.05f);

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

                    // 組合鍵連發。
                    if (controller.IsBackHeld)
                    {
                        _isBackUsedAsModifier = true;

                        AdjustOpacity(-0.05f);

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

                controller.APressed += () => this.SafeInvoke(() =>
                {
                    if (HandleContextMenuGamepadInput("Confirm"))
                    {
                        return;
                    }

                    // 檢查是否在擷取快速鍵模式或非作用中視窗。
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

                            // 告知擷取已取消。
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
                            // B 鍵僅負責「清空文字」。
                            // 若已是空的，則僅發送錯誤震動提示，不執行返回，以防連點誤觸導致視窗意外關閉。
                            if (string.IsNullOrEmpty(TBInput.Text))
                            {
                                FeedbackService.PlaySound(SystemSounds.Beep);

                                VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                                return;
                            }

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

                                // 開啟選單時立即報讀首個項目的名稱與描述。
                                string? name = item.AccessibleName ?? item.Text,
                                    desc = item.AccessibleDescription;

                                if (item is ToolStripMenuItem mi &&
                                    mi.CheckOnClick)
                                {
                                    string status = mi.Checked ?
                                        Strings.A11y_Checked :
                                        Strings.A11y_Unchecked;

                                    name = $"{name}, {status}";
                                }

                                string announcement = string.IsNullOrEmpty(desc) ?
                                    (name ?? string.Empty) :
                                    $"{name}. {desc}";

                                if (!string.IsNullOrEmpty(announcement))
                                {
                                    AnnounceA11y(announcement, interrupt: true);
                                }

                                break;
                            }
                        }

                        VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
                    }
                });

                // 右搖桿文字選取實作。
                controller.RSLeftPressed += () => this.SafeInvoke(() => ExpandSelection(-1));
                controller.RSLeftRepeat += () => this.SafeInvoke(() => ExpandSelection(-1));
                controller.RSRightPressed += () => this.SafeInvoke(() => ExpandSelection(1));
                controller.RSRightRepeat += () => this.SafeInvoke(() => ExpandSelection(1));

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

                        // 組合鍵：LB + RB + X 直接結束應用程式。
                        if (controller.IsLeftShoulderHeld &&
                            controller.IsRightShoulderHeld)
                        {
                            // 告知使用者正在關閉程式。
                            AnnounceA11y(Strings.A11y_Menu_Exit_Desc, interrupt: true);

                            this.SafeInvoke(Close);

                            return;
                        }

                        // 組合鍵：Back + X 重設透明度（100%）。
                        if (controller.IsBackHeld)
                        {
                            _isBackUsedAsModifier = true;

                            ResetOpacity();

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

                            // 如果刪除內容太長，報讀字數而非內容。
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

            AnnounceA11y(string.Format(Strings.A11y_Background_Error, ex.Message));
        }
        finally
        {
            try
            {
                initLock?.Release();
            }
            catch (ObjectDisposedException)
            {
                // 忽略已釋放的鎖。
            }
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

        if (activeTs == null)
        {
            return false;
        }

        switch (action)
        {
            case "Up":
                NavigateToolStrip(activeTs, false);

                return true;
            case "Down":
                NavigateToolStrip(activeTs, true);

                return true;
            case "Left":
                {
                    // 如果在子選單中，關閉子選單。
                    if (activeTs is ToolStripDropDown dropDown &&
                        dropDown.OwnerItem != null)
                    {
                        dropDown.Close();

                        return true;
                    }

                    return true;
                }
            case "Right":
                // 遍歷當前活動選單項目。
                foreach (ToolStripItem item in activeTs.Items)
                {
                    // 如果目前選取的項目擁有子選單，則開啟它並進入。
                    if (item.Selected &&
                        item is ToolStripMenuItem tsmi &&
                        tsmi.HasDropDownItems)
                    {
                        // 展開子選單。
                        tsmi.ShowDropDown();

                        // 自動將焦點導向子選單內的第一個有效項目，提升導覽效率。
                        if (tsmi.DropDown.Items.Count > 0)
                        {
                            NavigateToolStrip(tsmi.DropDown, forward: true);
                        }

                        return true;
                    }
                }

                return true;
            case "Confirm":
                // 遍歷當前活動選單項目，找出目前被選取的項。
                foreach (ToolStripItem item in activeTs.Items)
                {
                    if (item.Selected)
                    {
                        // 邏輯優化：若點擊的是含有子選單的項目（如語系切換或進階設定），
                        // A 鍵（Confirm）的行為應改為「展開並自動進入子選單」以提升操作流暢度。
                        if (item is ToolStripMenuItem tsmi &&
                            tsmi.HasDropDownItems)
                        {
                            // 展開子選單。
                            tsmi.ShowDropDown();

                            // 展開後立即將焦點（Select）導向子選單內的第一個有效項目。
                            // NavigateToolStrip 內部會自動跳過分隔線並同步處理 A11y 資訊播報與震動反饋。
                            if (tsmi.DropDown.Items.Count > 0)
                            {
                                NavigateToolStrip(tsmi.DropDown, forward: true);
                            }
                        }
                        else
                        {
                            // 對於一般功能項目，執行點擊動作（通常會伴隨選單自動關閉）。
                            item.PerformClick();
                        }

                        return true;
                    }
                }

                return true;
            case "Cancel":
                {
                    // 判斷：如果是活躍的子選單，B 鍵的行為改為「關閉子選單（退回上一層）」。
                    if (activeTs is ToolStripDropDown dropDown &&
                        dropDown.OwnerItem != null)
                    {
                        dropDown.Close();
                    }
                    else
                    {
                        // 如果已經在最外層的主選單，就直接關閉整個右鍵選單
                        _cmsInput?.Close();
                    }

                    return true;
                }
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

                // 焦點記憶。
                _lastFocusedMenuItem = nextItem;

                // 顯式播報選單項目名稱與描述（包含目前設定值）。
                string? name = nextItem.AccessibleName ?? nextItem.Text,
                    desc = nextItem.AccessibleDescription;

                // 如果是可勾選的項目，播報其狀態。
                if (nextItem is ToolStripMenuItem mi &&
                    mi.CheckOnClick)
                {
                    string status = mi.Checked ?
                        Strings.A11y_Checked :
                        Strings.A11y_Unchecked;

                    name = $"{name}, {status}";
                }

                string announcement = string.IsNullOrEmpty(desc) ?
                    (name ?? string.Empty) :
                    $"{name}. {desc}";

                if (!string.IsNullOrEmpty(announcement))
                {
                    AnnounceA11y(announcement, interrupt: true);
                }

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
        if (TBInput == null ||
            TBInput.IsDisposed)
        {
            return;
        }

        bool hasSelection = TBInput.SelectionLength > 0;

        if (hasSelection || TBInput.SelectionStart > 0)
        {
            if (hasSelection)
            {
                TBInput.SelectionLength = 0;
            }
            // 組合鍵：LB + Left 執行單字跳轉。
            else if (_gamepadController?.IsLeftShoulderHeld == true)
            {
                TBInput.WordJump(false);
            }
            else
            {
                TBInput.SelectionStart--;
            }

            TBInput.ScrollToCaret();

            // 手動報讀游標目前的絕對位置。
            AnnounceA11y(string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);

            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
        }
        else if (TBInput.SelectionStart == 0)
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
        if (TBInput == null ||
            TBInput.IsDisposed)
        {
            return;
        }

        bool hasSelection = TBInput.SelectionLength > 0;

        if (hasSelection ||
            TBInput.SelectionStart < TBInput.Text.Length)
        {
            if (hasSelection)
            {
                TBInput.SelectionStart += TBInput.SelectionLength;
                TBInput.SelectionLength = 0;
            }
            // 組合鍵：LB + Right 執行單字跳轉。
            else if (_gamepadController?.IsLeftShoulderHeld == true)
            {
                TBInput.WordJump(true);
            }
            else
            {
                TBInput.SelectionStart++;
            }

            TBInput.ScrollToCaret();

            // 手動報讀游標目前的絕對位置。
            AnnounceA11y(string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);
        }
        else if (TBInput.SelectionStart == TBInput.Text.Length)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);

            // 只有在撞牆時才震動，避免長按時一直震動。
            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

            // 撞到最右邊。
            AnnounceA11y(Strings.A11y_Nav_Bottom, interrupt: true);
        }
    }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="profile">VibrationProfile</param>
    /// <returns>Task</returns>
    private Task VibrateAsync(VibrationProfile profile)
    {
        // 委派給 Service 處理。
        return FeedbackService.VibrateAsync(
            _gamepadController,
            profile,
            _formCts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// 擴張或縮減文字選取範圍
    /// </summary>
    /// <param name="direction">方向（-1 為左，1 為右）</param>
    private void ExpandSelection(int direction)
    {
        if (ActiveForm != this ||
            _isCapturingHotkey != 0 ||
            TBInput == null ||
            TBInput.IsDisposed)
        {
            return;
        }

        int start = TBInput.SelectionStart,
            length = TBInput.SelectionLength;

        bool success = false;

        // 向左擴張選取。
        if (direction < 0)
        {
            if (start > 0)
            {
                TBInput.SelectionStart = start - 1;
                TBInput.SelectionLength = length + 1;

                success = true;
            }
        }
        else
        {
            // 向右擴張選取。
            if (start + length < TBInput.TextLength)
            {
                TBInput.SelectionLength = length + 1;

                success = true;
            }
        }

        if (success)
        {
            // A11y：報讀目前選取的文字內容。
            if (TBInput.SelectionLength > 0)
            {
                AnnounceA11y(string.Format(Strings.A11y_Selected_Text, TBInput.SelectedText), interrupt: true);
            }

            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
        }
        else
        {
            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();
        }
    }
}