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
    /// Face 鍵的實體方位，用於在不同控制器模式下統一轉譯為應用程式功能。
    /// </summary>
    private enum GamepadFacePhysicalButton
    {
        /// <summary>
        /// 面部按鍵的下方位置。
        /// </summary>
        South,

        /// <summary>
        /// 面部按鍵的右側位置。
        /// </summary>
        East,

        /// <summary>
        /// 面部按鍵的左側位置。
        /// </summary>
        West,

        /// <summary>
        /// 面部按鍵的上方位置。
        /// </summary>
        North
    }

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
        // 以 try-catch 保護 WaitAsync：若 OnFormClosing 在此指令執行前已搶先處置號誌，
        // 應視為正常關閉流程，靜默退出即可。
        bool acquired;

        try
        {
            acquired = initLock != null && await initLock.WaitAsync(0);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (!acquired)
        {
            return;
        }

        try
        {
            // 安全清理舊的控制器與輸入上下文實例。
            Interlocked.Exchange(ref _gamepadController, null)?.Dispose();

            Interlocked.Exchange(ref _inputContext, null)?.Dispose();

            _inputContext = new FormInputContext(this);

            AppSettings config = AppSettings.Current;
            GamepadRepeatSettings gamepadRepeatSettings = CreateRepeatSettings(config);

            await InitializeConfiguredControllerAsync(config, gamepadRepeatSettings);

            if (_gamepadController != null)
            {
                // 捕獲目前的控制器實例，確保 Lambda 內部存取的安全性。
                IGamepadController controller = _gamepadController;

                GamepadEventBinder eventBinder = new();

                GamepadEventBinder.Bind(controller, CreateGamepadBindingMap(controller));

                // 補償初始連線狀態：GameInput 的 CreateAsync 工廠模式會在訂閱者附加前
                // 就完成設備偵測並觸發 ConnectionChanged，導致初始事件遺失。
                // 在此統一檢查，確保兩種 API 後端（XInput／GameInput）行為一致。
                HandleInitialControllerConnectionState(controller);
            }

            UpdateTitle();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "控制器系統初始化失敗");

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
    /// 由設定快照建立控制器連發設定
    /// </summary>
    /// <param name="config">目前應用程式設定快照。</param>
    /// <returns>控制器連發設定。</returns>
    private static GamepadRepeatSettings CreateRepeatSettings(AppSettings config)
    {
        // 建立 GamepadRepeatSettings。
        return new GamepadRepeatSettings
        {
            InitialDelayFrames = config.RepeatInitialDelayFrames,
            IntervalFrames = config.RepeatIntervalFrames
        };
    }

    /// <summary>
    /// 解析目前生效的 Face 鍵配置，並同步更新執行期偵測到的裝置名稱與穩定識別資訊。
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    /// <returns>目前生效的 Face 鍵配置描述。</returns>
    private static GamepadFaceButtonProfile ResolveGamepadFaceButtonProfile(IGamepadController? controller)
    {
        // UI 顯示仍使用較友善的裝置名稱；Auto 判斷則額外保留較穩定的識別資訊。
        AppSettings.Current.RuntimeDetectedGamepadDeviceName = controller?.IsConnected == true ?
            controller.DeviceName :
            string.Empty;
        AppSettings.Current.RuntimeDetectedGamepadDeviceIdentity = controller?.IsConnected == true ?
            controller.DeviceIdentity :
            string.Empty;

        return GamepadFaceButtonProfile.GetActiveProfile();
    }

    /// <summary>
    /// 在使用者調整控制器模式或裝置連線狀態改變後，重新套用相關顯示文字與助記詞。
    /// </summary>
    /// <param name="announceProfileChange">是否播報配置模式已變更。</param>
    private void ApplyCurrentGamepadFaceButtonMode(bool announceProfileChange = false)
    {
        GamepadFaceButtonProfile profile = ResolveGamepadFaceButtonProfile(_gamepadController);

        ApplyLocalization();

        if (announceProfileChange &&
            _lastAnnouncedGamepadFaceButtonMode != profile.EffectiveLayout)
        {
            _lastAnnouncedGamepadFaceButtonMode = profile.EffectiveLayout;
            AnnounceA11y(GamepadFaceButtonProfile.GetLayoutAppliedAnnouncement(profile.EffectiveLayout), interrupt: true);
        }
    }

    /// <summary>
    /// 將實體 Face 鍵按壓轉譯為目前配置下的應用程式功能。
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    /// <param name="physicalButton">被按下的實體按鍵方位。</param>
    private void HandleFaceButtonAction(IGamepadController controller, GamepadFacePhysicalButton physicalButton)
    {
        GamepadFaceButtonProfile profile = ResolveGamepadFaceButtonProfile(controller);

        switch (physicalButton)
        {
            case GamepadFacePhysicalButton.South:
                if (profile.ConfirmOnSouth)
                {
                    ExecuteGamepadConfirmIfAllowed();
                }
                else
                {
                    HandleBButtonAction(controller);
                }

                return;
            case GamepadFacePhysicalButton.East:
                if (profile.ConfirmOnSouth)
                {
                    HandleBButtonAction(controller);
                }
                else
                {
                    ExecuteGamepadConfirmIfAllowed();
                }

                return;
            case GamepadFacePhysicalButton.West:
                HandleXButtonAction(controller);

                return;
            case GamepadFacePhysicalButton.North:
                OpenContextMenuFromGamepadIfAllowed();

                return;
        }
    }

    /// <summary>
    /// 建立控制器事件與 MainForm 行為的對映表
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    /// <returns>事件對映資料。</returns>
    private GamepadEventBinder.BindingMap CreateGamepadBindingMap(IGamepadController controller)
    {
        return new GamepadEventBinder.BindingMap(
            OnConnectionChanged: CreateConnectionChangedHandler(controller),
            OnBackPressed: CreateSafeGamepadActionHandler(() =>
            {
                // 記錄一次合法的 Back 按下，供 BackReleased 配對使用。
                Interlocked.Exchange(ref _backReleaseArmed, 1);

                // 按下時重置旗標。
                _isBackUsedAsModifier = false;
            }),
            OnBackReleased: CreateDebugOnlyGamepadActionHandler(
                HandleBackReleasedAction,
                "BackReleased",
                "處理失敗"),
            OnUpPressed: CreateSafeGamepadActionHandler(
                () => HandleVerticalGamepadInput(controller, "Up", +0.05f, -1)),
            OnDownPressed: CreateSafeGamepadActionHandler(
                () => HandleVerticalGamepadInput(controller, "Down", -0.05f, +1)),
            OnUpRepeat: CreateSafeGamepadActionHandler(
                () => HandleVerticalGamepadInput(controller, "Up", +0.05f, -1)),
            OnDownRepeat: CreateSafeGamepadActionHandler(
                () => HandleVerticalGamepadInput(controller, "Down", -0.05f, +1)),
            OnLeftPressed: CreateSafeGamepadActionHandler(
                () => HandleHorizontalGamepadInput("Left", MoveCursorLeft)),
            OnLeftRepeat: CreateSafeGamepadActionHandler(
                () => HandleHorizontalGamepadInput("Left", MoveCursorLeft)),
            OnRightPressed: CreateSafeGamepadActionHandler(
                () => HandleHorizontalGamepadInput("Right", MoveCursorRight),
                "RightPressed"),
            OnRightRepeat: CreateSafeGamepadActionHandler(
                () => HandleHorizontalGamepadInput("Right", MoveCursorRight)),
            OnLeftShoulderPressed: CreateSafeGamepadActionHandler(
                HandleLeftShoulderAction),
            OnRightShoulderPressed: CreateSafeGamepadActionHandler(
                HandleRightShoulderAction),
            OnLeftTriggerPressed: CreateSafeGamepadActionHandler(
                HandleLeftTriggerAction,
                "LeftTriggerPressed"),
            OnLeftTriggerRepeat: CreateSafeGamepadActionHandler(
                HandleLeftTriggerAction),
            OnRightTriggerPressed: CreateSafeGamepadActionHandler(
                HandleRightTriggerAction,
                "RightTriggerPressed"),
            OnRightTriggerRepeat: CreateSafeGamepadActionHandler(
                HandleRightTriggerAction),
            OnStartPressed: CreateSafeGamepadActionHandler(
                ExecuteGamepadShowKeyboardIfAllowed),
            OnAPressed: CreateSafeGamepadActionHandler(
                () => HandleFaceButtonAction(controller, GamepadFacePhysicalButton.South)),
            OnBPressed: CreateSafeGamepadActionHandler(
                () => HandleFaceButtonAction(controller, GamepadFacePhysicalButton.East)),
            OnYPressed: CreateSafeGamepadActionHandler(
                () => HandleFaceButtonAction(controller, GamepadFacePhysicalButton.North)),
            OnRSLeftPressed: CreateRightStickSelectionHandler(-1, "RSLeftPressed"),
            OnRSLeftRepeat: CreateRightStickSelectionHandler(-1, "RSLeftRepeat"),
            OnRSRightPressed: CreateRightStickSelectionHandler(1, "RSRightPressed"),
            OnRSRightRepeat: CreateRightStickSelectionHandler(1, "RSRightRepeat"),
            OnXPressed: CreateSafeGamepadActionHandler(
                () => HandleFaceButtonAction(controller, GamepadFacePhysicalButton.West)));
    }

    /// <summary>
    /// 依目前設定初始化控制器後端（GameInput 或 XInput）
    /// </summary>
    /// <param name="config">目前應用程式設定快照。</param>
    /// <param name="gamepadRepeatSettings">控制器連發設定。</param>
    /// <returns>非同步作業。</returns>
    private async Task InitializeConfiguredControllerAsync(AppSettings config, GamepadRepeatSettings gamepadRepeatSettings)
    {
        // 根據設定嘗試建立遊戲控制器實作。
        if (config.GamepadProviderType == AppSettings.GamepadProvider.GameInput)
        {
            await TryInitializeGameInputControllerAsync(gamepadRepeatSettings);

            return;
        }

        // 預設使用 XInput 實作。
        CreateXInputController(gamepadRepeatSettings);
    }

    /// <summary>
    /// 嘗試初始化 GameInput；失敗時退避至 XInput
    /// </summary>
    /// <param name="gamepadRepeatSettings">控制器連發設定。</param>
    /// <returns>非同步作業。</returns>
    private async Task TryInitializeGameInputControllerAsync(GamepadRepeatSettings gamepadRepeatSettings)
    {
        try
        {
            IInputContext? inputContext = _inputContext;

            if (inputContext == null)
            {
                CreateXInputController(gamepadRepeatSettings);

                return;
            }

            // 嘗試初始化 GameInput。
            GameInputGamepadController controller = await GameInputGamepadController.CreateAsync(
                inputContext,
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
                NavigateToolStrip(activeTs, forward: false);

                return true;
            case "Down":
                NavigateToolStrip(activeTs, forward: true);

                return true;
            case "Left":
                // 如果在子選單中，關閉子選單。
                _ = TryCloseActiveSubmenu(activeTs);

                return true;
            case "Right":
                HandleContextMenuRightAction(activeTs);

                return true;
            case "PhrasePagePrevious":
                return TryNavigatePhraseMenuPage(-1);
            case "PhrasePageNext":
                return TryNavigatePhraseMenuPage(1);
            case "PhrasePageFirst":
                return TryJumpPhraseMenuToBoundary(lastPage: false);
            case "PhrasePageLast":
                return TryJumpPhraseMenuToBoundary(lastPage: true);
            case "Confirm":
                HandleContextMenuConfirmAction(activeTs);

                return true;
            case "Cancel":
                HandleContextMenuCancelAction(activeTs);

                return true;
        }

        return false;
    }

    /// <summary>
    /// 取得目前 ToolStrip 中被選取的項目
    /// </summary>
    /// <param name="activeTs">目前活躍的選單。</param>
    /// <returns>被選取的項目；若無則回傳 null。</returns>
    private static ToolStripItem? GetSelectedToolStripItem(ToolStrip activeTs)
    {
        foreach (ToolStripItem item in activeTs.Items)
        {
            if (item.Selected)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// 處理選單 Right 動作（嘗試展開子選單）
    /// </summary>
    /// <param name="activeTs">目前活躍的選單。</param>
    private void HandleContextMenuRightAction(ToolStrip activeTs)
    {
        if (GetSelectedToolStripItem(activeTs) is ToolStripItem selectedForRight)
        {
            // 如果目前選取的項目擁有子選單，則開啟它並進入。
            _ = TryEnterSubmenu(selectedForRight);
        }
    }

    /// <summary>
    /// 處理選單 Confirm 動作（展開子選單或執行項目）
    /// </summary>
    /// <param name="activeTs">目前活躍的選單。</param>
    private void HandleContextMenuConfirmAction(ToolStrip activeTs)
    {
        if (GetSelectedToolStripItem(activeTs) is not ToolStripItem selectedForConfirm)
        {
            return;
        }

        // 邏輯最佳化：若點擊的是含有子選單的項目（如語系切換或進階設定），
        // A 鍵（Confirm）的行為應改為「展開並自動進入子選單」以提升操作流暢度。
        if (!TryEnterSubmenu(selectedForConfirm))
        {
            // 對於一般功能項目，執行點擊動作（通常會伴隨選單自動關閉）。
            selectedForConfirm.PerformClick();
        }
    }

    /// <summary>
    /// 嘗試展開指定選單項目的子選單
    /// </summary>
    /// <param name="item">目標選單項目。</param>
    /// <returns>若成功展開子選單則回傳 true。</returns>
    private bool TryEnterSubmenu(ToolStripItem item)
    {
        if (item is not ToolStripMenuItem tsmi ||
            !tsmi.HasDropDownItems)
        {
            return false;
        }

        // A11y：在進入子選單前，先告知目前的選單層級。
        AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, tsmi.AccessibleName ?? tsmi.Text), interrupt: true);

        // 展開子選單。
        tsmi.ShowDropDown();

        // 展開後立即將焦點導向子選單內的第一個有效項目。
        if (tsmi.DropDown.Items.Count > 0)
        {
            NavigateToolStrip(tsmi.DropDown, forward: true);
        }

        return true;
    }

    /// <summary>
    /// 處理選單 Cancel 動作（優先退回上一層，否則關閉主選單）
    /// </summary>
    /// <param name="activeTs">目前活躍的選單。</param>
    private void HandleContextMenuCancelAction(ToolStrip activeTs)
    {
        // 判斷：如果是活躍的子選單，B 鍵的行為改為「關閉子選單（退回上一層）」。
        if (TryCloseActiveSubmenu(activeTs, announceExit: true))
        {
            return;
        }

        // 如果已經在最外層的主選單，就直接關閉整個右鍵選單。
        _cmsInput?.Close();
    }

    /// <summary>
    /// 嘗試關閉目前活躍子選單
    /// </summary>
    /// <param name="activeTs">目前活躍的選單。</param>
    /// <param name="announceExit">是否播報離開子選單訊息。</param>
    /// <returns>若成功關閉子選單則回傳 true。</returns>
    private bool TryCloseActiveSubmenu(ToolStrip activeTs, bool announceExit = false)
    {
        if (activeTs is not ToolStripDropDown dropDown ||
            dropDown.OwnerItem == null)
        {
            return false;
        }

        if (announceExit)
        {
            // A11y：告知已關閉子選單並退回上一層。
            AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Exit, dropDown.OwnerItem.AccessibleName ?? dropDown.OwnerItem.Text), interrupt: true);
        }

        dropDown.Close();

        return true;
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

        ToolStripItem? current = GetSelectedToolStripItem(ts);

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
    /// 判斷目前是否應抑制控制器輸入（非作用中或擷取模式）。
    /// </summary>
    /// <returns>若應抑制控制器輸入則回傳 true。</returns>
    private bool IsGamepadInputSuppressed()
    {
        return ActiveForm != this ||
               _inputState.IsHotkeyCaptureActive;
    }

    /// <summary>
    /// 判斷指定控制器動作是否應略過。
    /// </summary>
    /// <param name="action">控制器動作名稱。</param>
    /// <returns>若應略過該動作則回傳 true。</returns>
    private bool ShouldSkipGamepadAction(string action)
    {
        return HandleContextMenuGamepadInput(action) ||
               IsGamepadInputSuppressed();
    }

    /// <summary>
    /// 建立控制器連線變更事件處理委派
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    /// <returns>連線狀態變更事件處理委派。</returns>
    private Action<bool> CreateConnectionChangedHandler(IGamepadController controller)
    {
        return isConnected =>
        {
            HandleControllerConnectionChanged(controller, isConnected);
        };
    }

    /// <summary>
    /// 處理 Back 釋放行為（必要時返回前景視窗）
    /// </summary>
    private void HandleBackReleasedAction()
    {
        if (ShouldSkipGamepadAction("Cancel"))
        {
            return;
        }

        // 只接受有對應 BackPressed 的放開事件，忽略恢復輪詢後的孤兒 BackReleased。
        if (Interlocked.Exchange(ref _backReleaseArmed, 0) == 0)
        {
            return;
        }

        if (IsGamepadReturnSuppressed())
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

    /// <summary>
    /// 補償控制器初始連線狀態遺失事件
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    private void HandleInitialControllerConnectionState(IGamepadController controller)
    {
        if (!controller.IsConnected ||
            _lastGamepadConnectedState == true)
        {
            return;
        }

        _lastGamepadConnectedState = true;

        GamepadFaceButtonProfile profile = ResolveGamepadFaceButtonProfile(controller);
        _lastAnnouncedGamepadFaceButtonMode = profile.EffectiveLayout;

        ApplyCurrentGamepadFaceButtonMode();
        UpdateTitle();

        AnnounceA11y(
            $"{string.Format(Strings.A11y_Gamepad_Connected, controller.DeviceName)} {GamepadFaceButtonProfile.GetLayoutAppliedAnnouncement(profile.EffectiveLayout)}");

        FeedbackService.PlaySound(SystemSounds.Asterisk);

        CancellationToken ct = _formCts?.Token ?? CancellationToken.None;

        FeedbackService.VibrateAsync(
            controller,
            VibrationPatterns.ControllerConnected,
            ct).SafeFireAndForget();
    }

    /// <summary>
    /// 處理控制器連線狀態變更並提供多模態回饋
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    /// <param name="isConnected">新的連線狀態。</param>
    private void HandleControllerConnectionChanged(IGamepadController controller, bool isConnected)
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

                GamepadFaceButtonProfile profile = ResolveGamepadFaceButtonProfile(isConnected ? controller : null);
                _lastAnnouncedGamepadFaceButtonMode = profile.EffectiveLayout;
                ApplyCurrentGamepadFaceButtonMode();

                // 多模態回饋（Multi-Modal Feedback）：
                // 確保視障、聽障、視聽雙障的使用者皆能透過至少一種通道感知狀態變更。
                string msg = isConnected ?
                    $"{string.Format(Strings.A11y_Gamepad_Connected, controller.DeviceName)} {GamepadFaceButtonProfile.GetLayoutAppliedAnnouncement(profile.EffectiveLayout)}" :
                    string.Format(Strings.A11y_Gamepad_Disconnected, controller.DeviceName);

                AnnounceA11y(msg);

                FeedbackService.PlaySound(
                    isConnected ? SystemSounds.Asterisk : SystemSounds.Exclamation);

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
                LoggerService.LogException(ex, "HandleControllerConnectionChanged 處理失敗");

                Debug.WriteLine($"[控制器] ConnectionChanged 處理失敗：{ex.Message}");
            }
        });
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
    /// 在允許條件下執行控制器確認行為。
    /// </summary>
    private void ExecuteGamepadConfirmIfAllowed()
    {
        if (ShouldSkipGamepadAction("Confirm"))
        {
            return;
        }

        ExecuteConfirmAction();
    }

    /// <summary>
    /// 在允許條件下以控制器開啟觸控鍵盤
    /// </summary>
    private void ExecuteGamepadShowKeyboardIfAllowed()
    {
        if (ShouldSkipGamepadAction("Confirm"))
        {
            return;
        }

        // Start 鍵統一作為「開啟觸控鍵盤」入口，
        // 即使 TBInput 已有文字也可叫出鍵盤進行修正。
        ShowTouchKeyboard();
    }

    /// <summary>
    /// 處理垂直方向輸入（歷程導覽或透明度調整）
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    /// <param name="action">控制器動作名稱。</param>
    /// <param name="opacityDelta">透明度調整幅度。</param>
    /// <param name="historyDirection">歷程導覽方向。</param>
    private void HandleVerticalGamepadInput(
        IGamepadController controller,
        string action,
        float opacityDelta,
        int historyDirection)
    {
        if (ShouldSkipGamepadAction(action))
        {
            return;
        }

        // 組合鍵（含連發）：Back + Up／Down 調整透明度。
        if (controller.IsBackHeld)
        {
            _isBackUsedAsModifier = true;

            AdjustOpacity(opacityDelta);

            return;
        }

        NavigateHistory(historyDirection);
    }

    /// <summary>
    /// 處理水平方向輸入
    /// </summary>
    /// <param name="action">控制器動作名稱。</param>
    /// <param name="moveAction">實際移動游標的動作。</param>
    private void HandleHorizontalGamepadInput(string action, Action moveAction)
    {
        if (ShouldSkipGamepadAction(action))
        {
            return;
        }

        moveAction();
    }

    /// <summary>
    /// 處理 LB 鍵行為（片語子選單上一頁）。
    /// </summary>
    private void HandleLeftShoulderAction()
    {
        _ = HandleContextMenuGamepadInput("PhrasePagePrevious");
    }

    /// <summary>
    /// 處理 RB 鍵行為（片語子選單下一頁）。
    /// </summary>
    private void HandleRightShoulderAction()
    {
        _ = HandleContextMenuGamepadInput("PhrasePageNext");
    }

    /// <summary>
    /// 處理 LT 鍵行為（片語首頁或輸入框行首）。
    /// </summary>
    private void HandleLeftTriggerAction()
    {
        if (HandleContextMenuGamepadInput("PhrasePageFirst") ||
            _cmsInput?.Visible == true ||
            IsGamepadInputSuppressed())
        {
            return;
        }

        MoveCursorToBoundary(moveToEnd: false);
    }

    /// <summary>
    /// 處理 RT 鍵行為（片語末頁或輸入框行尾）。
    /// </summary>
    private void HandleRightTriggerAction()
    {
        if (HandleContextMenuGamepadInput("PhrasePageLast") ||
            _cmsInput?.Visible == true ||
            IsGamepadInputSuppressed())
        {
            return;
        }

        MoveCursorToBoundary(moveToEnd: true);
    }

    /// <summary>
    /// 處理 B 鍵行為（取消、返回或清空）
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    private void HandleBButtonAction(IGamepadController controller)
    {
        if (HandleContextMenuGamepadInput("Cancel"))
        {
            return;
        }

        if (TryCancelCaptureModeByBButton())
        {
            return;
        }

        if (ActiveForm != this)
        {
            return;
        }

        if (controller.IsLeftShoulderHeld &&
            controller.IsRightShoulderHeld)
        {
            if (IsGamepadReturnSuppressed())
            {
                return;
            }

            HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();

            return;
        }

        ClearInputByBButton();
    }

    /// <summary>
    /// 處理 X 鍵行為（刪除、透明度重設或關閉程式）
    /// </summary>
    /// <param name="controller">目前控制器實例。</param>
    private void HandleXButtonAction(IGamepadController controller)
    {
        // 選單開啟時 X 鍵不執行刪除，除非有特殊需求。
        if (_cmsInput != null &&
            _cmsInput.Visible)
        {
            return;
        }

        if (ActiveForm != this ||
            _inputState.IsHotkeyCaptureActive)
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

            return;
        }

        if (TBInput.SelectionStart > 0)
        {
            // 如果沒有選取且游標不在最前面，刪除前一個字元。
            int position = TBInput.SelectionStart;

            char deletedChar = TBInput.Text[position - 1];

            TBInput.Select(position - 1, 1);
            TBInput.SelectedText = string.Empty;

            AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                Strings.A11y_Delete_Char_PrivacySafe :
                string.Format(Strings.A11y_Delete_Char, deletedChar));

            return;
        }

        // 撞牆（游標在最前面且沒選取文字）。
        FeedbackService.PlaySound(SystemSounds.Beep);

        VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

        AnnounceA11y(Strings.A11y_Cannot_Delete);
    }

    /// <summary>
    /// 嘗試以 B 鍵取消快速鍵擷取模式
    /// </summary>
    /// <returns>若已處理擷取模式取消則回傳 true。</returns>
    private bool TryCancelCaptureModeByBButton()
    {
        // 按鍵擷取模式下的取消處理。
        if (!_inputState.IsHotkeyCaptureActive)
        {
            return false;
        }

        RestoreUIFromCaptureMode();

        // 告知擷取已取消。
        AnnounceA11y(Strings.A11y_Capture_Cancelled);

        // 播放警告音。
        FeedbackService.PlaySound(SystemSounds.Beep);

        return true;
    }

    /// <summary>
    /// 處理 B 鍵觸發的清空輸入流程
    /// </summary>
    private void ClearInputByBButton()
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

    /// <summary>
    /// 在允許條件下以控制器開啟右鍵選單
    /// </summary>
    private void OpenContextMenuFromGamepadIfAllowed()
    {
        if (IsGamepadInputSuppressed() ||
            _cmsInput == null ||
            _cmsInput.Visible)
        {
            return;
        }

        // 在文字方塊下方開啟選單。
        _cmsInput.Show(this, new Point(TBInput.Left, TBInput.Bottom));

        SelectFirstVisibleMenuItemAndAnnounce(_cmsInput);

        VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
    }

    /// <summary>
    /// 處理右搖桿文字選取輸入
    /// </summary>
    /// <param name="direction">選取方向（-1 左、1 右）。</param>
    private void HandleRightStickSelectionInput(int direction)
    {
        ExpandSelection(direction);
    }

    /// <summary>
    /// 建立右搖桿選取事件處理委派。
    /// </summary>
    /// <param name="direction">選取方向（-1 左、1 右）。</param>
    /// <param name="actionName">動作名稱（用於除錯輸出）。</param>
    /// <returns>包裝後的事件處理委派。</returns>
    private Action CreateRightStickSelectionHandler(int direction, string actionName)
    {
        return CreateDebugOnlyGamepadActionHandler(
            () => HandleRightStickSelectionInput(direction),
            actionName,
            "失敗");
    }

    /// <summary>
    /// 建立僅輸出 Debug 訊息的控制器事件處理包裝器。
    /// </summary>
    /// <param name="action">實際要執行的動作。</param>
    /// <param name="actionName">動作名稱（用於除錯輸出）。</param>
    /// <param name="failureLabel">失敗標籤文字。</param>
    /// <returns>包裝後的事件處理委派。</returns>
    private Action CreateDebugOnlyGamepadActionHandler(Action action, string actionName, string failureLabel)
    {
        return () => this.SafeInvoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[控制器] {actionName} {failureLabel}：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 建立具例外保護與統一記錄的控制器事件處理包裝器
    /// </summary>
    /// <param name="action">實際要執行的動作。</param>
    /// <param name="actionName">可選動作名稱（用於除錯輸出）。</param>
    /// <returns>包裝後的事件處理委派。</returns>
    private Action CreateSafeGamepadActionHandler(Action action, string? actionName = null)
    {
        return () => this.SafeInvoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "[控制器] 事件處理失敗");

                if (!string.IsNullOrEmpty(actionName))
                {
                    Debug.WriteLine($"[控制器] {actionName} 處理失敗：{ex.Message}");

                    return;
                }

                Debug.WriteLine($"[控制器] 處理失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 選取第一個可操作選單項目並播報其資訊
    /// </summary>
    /// <param name="menu">目標右鍵選單。</param>
    private void SelectFirstVisibleMenuItemAndAnnounce(ContextMenuStrip menu)
    {
        if (ContextMenuBuilder.TrySelectFirstVisibleItem(
            menu,
            Strings.A11y_Checked,
            Strings.A11y_Unchecked,
            out string announcement))
        {
            AnnounceA11y(announcement, interrupt: true);
        }
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
    /// 直接跳到文字開頭或結尾。
    /// </summary>
    /// <param name="moveToEnd">true = 跳到結尾；false = 跳到開頭。</param>
    private void MoveCursorToBoundary(bool moveToEnd)
    {
        if (TBInput == null ||
            TBInput.IsDisposed)
        {
            return;
        }

        int target = moveToEnd ? TBInput.Text.Length : 0;

        if (TBInput.SelectionLength == 0 &&
            TBInput.SelectionStart == target)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);
            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();
            AnnounceA11y(moveToEnd ? Strings.A11y_Nav_Bottom : Strings.A11y_Nav_Top, interrupt: true);

            return;
        }

        TBInput.SelectionStart = target;
        TBInput.SelectionLength = 0;
        TBInput.ScrollToCaret();

        AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
            Strings.A11y_Cursor_Move_PrivacySafe :
            string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);

        VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
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
            _inputState.IsHotkeyCaptureActive ||
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