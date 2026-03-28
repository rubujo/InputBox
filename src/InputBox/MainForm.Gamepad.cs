using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
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
            Interlocked.Exchange(ref _gamepadController, null)?.Dispose();

            Interlocked.Exchange(ref _inputContext, null)?.Dispose();

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
                    LoggerService.LogException(ex, "GameInput 初始化失敗，嘗試退避至 XInput");

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

                // 當控制器連線狀態改變時進行廣播。
                controller.ConnectionChanged += (isConnected) =>
                {
                    this.SafeInvoke(() =>
                    {
                        try
                        {
                            UpdateTitle();

                            // 防止重複廣播相同的連線狀態。
                            if (_lastGamepadConnectedState == isConnected)
                            {
                                return;
                            }

                            _lastGamepadConnectedState = isConnected;

                            // 多模態回饋（Multi-Modal Feedback）：
                            // 確保視障、聽障、視聽雙障的使用者皆能透過至少一種通道感知狀態變更。

                            // 1. 語音廣播（視障通道）。
                            string msg = isConnected ?
                                string.Format(Strings.A11y_Gamepad_Connected, controller.DeviceName) :
                                string.Format(Strings.A11y_Gamepad_Disconnected, controller.DeviceName);

                            AnnounceA11y(msg);

                            // 2. 系統音效（聽覺通道，尊重使用者的系統音效設定）。
                            FeedbackService.PlaySound(
                                isConnected ? SystemSounds.Asterisk : SystemSounds.Exclamation);

                            // 3. 觸覺回饋（體感通道，僅連線時——斷線時控制器已離線無法震動）。
                            //    對視聽雙障的使用者而言，這是唯一能感知「控制器已被識別」的通道。
                            //    VibrateAsync 內部已檢查 EnableVibration 設定與 GlobalIntensityMultiplier。
                            if (isConnected)
                            {
                                CancellationToken ct = _formCts?.Token ?? CancellationToken.None;

                                FeedbackService.VibrateAsync(
                                    controller,
                                    VibrationPatterns.ControllerConnected,
                                    ct).SafeFireAndForget();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[控制器] ConnectionChanged 處理失敗：{ex.Message}");
                        }
                    });
                };

                // 補償初始連線狀態：GameInput 的 CreateAsync 工廠模式會在訂閱者附加前
                // 就完成設備偵測並觸發 ConnectionChanged，導致初始事件遺失。
                // 在此統一檢查，確保兩種 API 後端（XInput／GameInput）行為一致。
                if (controller.IsConnected && _lastGamepadConnectedState != true)
                {
                    _lastGamepadConnectedState = true;

                    UpdateTitle();

                    AnnounceA11y(
                        string.Format(Strings.A11y_Gamepad_Connected, controller.DeviceName));

                    FeedbackService.PlaySound(SystemSounds.Asterisk);

                    CancellationToken ct = _formCts?.Token ?? CancellationToken.None;

                    FeedbackService.VibrateAsync(
                        controller,
                        VibrationPatterns.ControllerConnected,
                        ct).SafeFireAndForget();
                }

                // 控制器事件綁定。
                controller.BackPressed += () => this.SafeInvoke(() =>
                {
                    try
                    {
                        // 按下時重置旗標。
                        _isBackUsedAsModifier = false;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });

                controller.BackReleased += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[控制器] BackReleased 處理失敗：{ex.Message}");
                    }
                });

                controller.UpPressed += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });
                controller.DownPressed += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });
                controller.UpRepeat += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });
                controller.DownRepeat += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });
                controller.LeftPressed += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });
                controller.LeftRepeat += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });
                controller.RightPressed += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] RightPressed 處理失敗：{ex.Message}");
                    }
                });
                controller.RightRepeat += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });

                controller.StartPressed += () =>
                {
                    this.SafeInvoke(() =>
                    {
                        try
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
                        }
                        catch (Exception ex)
                        {
                            LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                            Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                        }
                    });
                };

                controller.APressed += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });

                controller.BPressed += () =>
                {
                    this.SafeInvoke(() =>
                    {
                        try
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
                        }
                        catch (Exception ex)
                        {
                            LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                            Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                        }
                    });
                };

                controller.YPressed += () => this.SafeInvoke(() =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                        Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
                    }
                });

                // 右搖桿文字選取實作。
                controller.RSLeftPressed += () => this.SafeInvoke(() =>
                {
                    try
                    {
                        ExpandSelection(-1);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[控制器] RSLeftPressed 失敗：{ex.Message}");
                    }
                });
                controller.RSLeftRepeat += () => this.SafeInvoke(() =>
                {
                    try
                    {
                        ExpandSelection(-1);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[控制器] RSLeftRepeat 失敗：{ex.Message}");
                    }
                });
                controller.RSRightPressed += () => this.SafeInvoke(() =>
                {
                    try
                    {
                        ExpandSelection(1);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[控制器] RSRightPressed 失敗：{ex.Message}");
                    }
                });
                controller.RSRightRepeat += () => this.SafeInvoke(() =>
                {
                    try
                    {
                        ExpandSelection(1);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[控制器] RSRightRepeat 失敗：{ex.Message}");
                    }
                });

                controller.XPressed += () =>
                {
                    this.SafeInvoke(() =>
                    {
                        try
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
                                else if (AppSettings.Current.IsPrivacyMode)
                                {
                                    AnnounceA11y(len > 1 ?
                                        string.Format(Strings.A11y_Delete_Multiple, len) :
                                        Strings.A11y_Delete_Char_PrivacySafe);
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

                                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                                    Strings.A11y_Delete_Char_PrivacySafe :
                                    string.Format(Strings.A11y_Delete_Char, deletedChar));
                            }
                            else
                            {
                                // 撞牆（游標在最前面且沒選取文字）。
                                FeedbackService.PlaySound(SystemSounds.Beep);

                                VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

                                AnnounceA11y(Strings.A11y_Cannot_Delete);
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                            Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
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
                        // A11y：在進入子選單前，先告知目前的選單層級。
                        AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, tsmi.AccessibleName ?? tsmi.Text), interrupt: true);

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
                        // 邏輯最佳化：若點擊的是含有子選單的項目（如語系切換或進階設定），
                        // A 鍵（Confirm）的行為應改為「展開並自動進入子選單」以提升操作流暢度。
                        if (item is ToolStripMenuItem tsmi &&
                            tsmi.HasDropDownItems)
                        {
                            // A11y：在進入子選單前，先告知目前的選單層級。
                            AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, tsmi.AccessibleName ?? tsmi.Text), interrupt: true);

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
                        // A11y：告知已關閉子選單並退回上一層。
                        AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Exit, dropDown.OwnerItem.AccessibleName ?? dropDown.OwnerItem.Text), interrupt: true);

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
                // 優先使用已在 RefreshMenuText 中格式化過的 AccessibleName（包含目前值與勾選狀態）。
                string name = nextItem.AccessibleName ?? nextItem.Text ?? string.Empty,
                    desc = nextItem.AccessibleDescription ?? string.Empty;

                string announcement = string.IsNullOrEmpty(desc) ?
                    name :
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

        if (hasSelection ||
            TBInput.SelectionStart > 0)
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
            AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                Strings.A11y_Cursor_Move_PrivacySafe :
                string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);

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
            AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                Strings.A11y_Cursor_Move_PrivacySafe :
                string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);
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

        // 當目前沒有選取範圍，或是目前的選取範圍與我們的錨點不匹配時，重新設定錨點。
        // 這能確保手動點擊或鍵盤選取後，RS 選取能從正確的位置開始。
        if (TBInput.SelectionLength == 0 ||
            _rsSelectionAnchor == null ||
            (TBInput.SelectionStart != _rsSelectionAnchor.Value &&
             TBInput.SelectionStart + TBInput.SelectionLength != _rsSelectionAnchor.Value))
        {
            _rsSelectionAnchor = TBInput.SelectionStart;
        }

        int anchor = _rsSelectionAnchor.Value;

        // 推算目前的活動邊緣（Caret）。
        // WinForms SelectionStart 始終為較小的索引，因此若 Start 與錨點一致，則 Caret 在右側；否則 Caret 在左側。
        int caret = (TBInput.SelectionStart == anchor) ?
            (anchor + TBInput.SelectionLength) :
            TBInput.SelectionStart;

        // 防禦性寫法：確保方向永遠只會是 -1、0 或 1，杜絕任何溢出造成的邏輯錯亂。
        int safeDirection = Math.Sign(direction);

        int newCaret = Math.Clamp(caret + safeDirection, 0, TBInput.TextLength);

        if (newCaret == caret)
        {
            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

            return;
        }

        // 使用 Win32 EM_SETSEL 設定選取範圍。
        // wParam 為錨點，lParam 為活動邊緣。這能確保視覺上的游標（Caret）正確跟隨活動邊緣，並支援反向縮減。
        User32.SendMessage(TBInput.Handle, (uint)User32.WindowMessage.EM_SETSEL, anchor, newCaret);

        // A11y：報讀目前選取的文字內容。
        if (TBInput.SelectionLength > 0)
        {
            AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                Strings.A11y_Selected_Text_PrivacySafe :
                string.Format(Strings.A11y_Selected_Text, TBInput.SelectedText), interrupt: true);
        }

        VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
    }
}