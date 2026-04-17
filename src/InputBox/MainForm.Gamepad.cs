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
    /// 文字邊界回饋的節奏分級。
    /// </summary>
    private enum BoundaryFeedbackStage
    {
        Full,
        SoftRepeat,
        Suppressed
    }

    /// <summary>
    /// 追蹤 Back 鍵是否已作為組合鍵使用（用於防止放開時觸發返回動作）
    /// </summary>
    private bool _isBackUsedAsModifier = false;

    /// <summary>
    /// 肩鍵與板機鍵在主輸入區的組合判斷延遲，避免單擊捷徑與修飾鍵語義互相衝突。
    /// </summary>
    private const int GamepadModifierGraceDelayMs = 120;

    /// <summary>
    /// 連發觸發時的邊界回饋節流時間，避免長按撞牆造成提示音與震動風暴。
    /// </summary>
    private const int RepeatedBoundaryFeedbackThrottleMs = 180;

    /// <summary>
    /// LB + RB + X 的長按結束保護時間。
    /// </summary>
    private const int GamepadExitHoldDelayMs = 800;

    /// <summary>
    /// 在短時間內重複撞到同一個文字邊界時，改用較柔和回饋的視窗時間。
    /// </summary>
    private const int BoundarySoftRepeatWindowMs = 1200;

    /// <summary>
    /// 快速連續翻閱歷程時，用來模擬阻尼滾輪手感的 burst 視窗。
    /// </summary>
    private const int HistoryScrollBurstWindowMs = 220;

    /// <summary>
    /// 右搖桿快速選取時，用來形成拉鏈感的 burst 視窗。
    /// </summary>
    private const int SelectionBurstWindowMs = 110;

    /// <summary>
    /// 文字接近輸入上限時開始提供物理預警的剩餘字元門檻。
    /// </summary>
    private const int TextLimitWarningThreshold = 10;

    /// <summary>
    /// 暫存肩鍵單擊捷徑的取消權杖，用來區分單擊翻頁與肩鍵＋方向鍵的單字跳轉。
    /// </summary>
    private CancellationTokenSource? _leftShoulderShortcutCts;
    private CancellationTokenSource? _rightShoulderShortcutCts;

    /// <summary>
    /// 暫存板機單擊捷徑的取消權杖，用來讓 LT+RT 雙壓優先切換隱私模式。
    /// </summary>
    private CancellationTokenSource? _leftTriggerShortcutCts;
    private CancellationTokenSource? _rightTriggerShortcutCts;

    /// <summary>
    /// 結束程式長按確認流程的取消權杖。
    /// </summary>
    private CancellationTokenSource? _exitHoldCts;

    /// <summary>
    /// 防止 LT+RT 在同一次長按期間重複切換隱私模式。
    /// </summary>
    private int _privacyTriggerComboLatched;

    /// <summary>
    /// 防止 LB + RB + X 在同一次長按期間重複建立結束流程。
    /// </summary>
    private int _exitHoldLatched;

    /// <summary>
    /// 肩鍵作為單字跳轉修飾鍵時，暫時抑制歷程翻頁連發，避免兩種語意衝突。
    /// </summary>
    private DateTime _suppressShoulderPagingUntilUtc = DateTime.MinValue;

    /// <summary>
    /// 追蹤最近一次邊界回饋的 key 與時間，用於長按時節流。
    /// </summary>
    private string _lastBoundaryFeedbackKey = string.Empty;
    private DateTime _lastBoundaryFeedbackUtc = DateTime.MinValue;

    /// <summary>
    /// 記錄最近一次歷程滾輪手感回饋的時間與 burst 等級。
    /// </summary>
    private DateTime _lastHistoryScrollFeedbackUtc = DateTime.MinValue;
    private int _historyScrollBurstLevel;

    /// <summary>
    /// 記錄最近一次右搖桿選取回饋的時間與 burst 等級。
    /// </summary>
    private DateTime _lastSelectionFeedbackUtc = DateTime.MinValue;
    private int _selectionFeedbackBurstLevel;

    /// <summary>
    /// 追蹤主輸入框目前長度，用於偵測接近字數上限時的物理預警。
    /// </summary>
    private int _lastObservedTextLength;
    private int _lastTextLimitWarningBucket = -1;
    private int _suppressNextTextLimitFeedback;
    private DateTime _lastTextLimitWallUtc = DateTime.MinValue;

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
            OnLeftShoulderRepeat: CreateSafeGamepadActionHandler(
                HandleLeftShoulderRepeat),
            OnRightShoulderPressed: CreateSafeGamepadActionHandler(
                HandleRightShoulderAction),
            OnRightShoulderRepeat: CreateSafeGamepadActionHandler(
                HandleRightShoulderRepeat),
            OnLeftTriggerPressed: CreateSafeGamepadActionHandler(
                HandleLeftTriggerAction,
                "LeftTriggerPressed"),
            OnLeftTriggerRepeat: CreateSafeGamepadActionHandler(
                HandleLeftTriggerRepeat),
            OnRightTriggerPressed: CreateSafeGamepadActionHandler(
                HandleRightTriggerAction,
                "RightTriggerPressed"),
            OnRightTriggerRepeat: CreateSafeGamepadActionHandler(
                HandleRightTriggerRepeat),
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

                VibrateNavigationAsync(VibrationSemantic.CursorMove, forward ? 1 : -1).SafeFireAndForget();

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
    /// 處理 LB 鍵行為（片語子選單上一頁，或主輸入區向較舊歷程翻頁）。
    /// </summary>
    private void HandleLeftShoulderAction()
        => HandleShoulderAction(direction: -1, "PhrasePagePrevious", ref _leftShoulderShortcutCts, allowDelayedShortcut: true);

    /// <summary>
    /// 處理 LB 長按連發，讓歷程或片語翻頁可持續捲動。
    /// </summary>
    private void HandleLeftShoulderRepeat()
        => HandleShoulderAction(direction: -1, "PhrasePagePrevious", ref _leftShoulderShortcutCts, allowDelayedShortcut: false);

    /// <summary>
    /// 處理 RB 鍵行為（片語子選單下一頁，或主輸入區向較新歷程翻頁）。
    /// </summary>
    private void HandleRightShoulderAction()
        => HandleShoulderAction(direction: +1, "PhrasePageNext", ref _rightShoulderShortcutCts, allowDelayedShortcut: true);

    /// <summary>
    /// 處理 RB 長按連發，讓歷程或片語翻頁可持續捲動。
    /// </summary>
    private void HandleRightShoulderRepeat()
        => HandleShoulderAction(direction: +1, "PhrasePageNext", ref _rightShoulderShortcutCts, allowDelayedShortcut: false);

    /// <summary>
    /// 統一處理肩鍵單擊與連發的翻頁邏輯。
    /// </summary>
    private void HandleShoulderAction(
        int direction,
        string phrasePagingAction,
        ref CancellationTokenSource? pendingShortcutCts,
        bool allowDelayedShortcut)
    {
        if (HandleContextMenuGamepadInput(phrasePagingAction) ||
            _cmsInput?.Visible == true ||
            IsGamepadInputSuppressed() ||
            IsShoulderPagingTemporarilySuppressed())
        {
            return;
        }

        if (allowDelayedShortcut)
        {
            QueueShoulderHistoryShortcut(direction, ref pendingShortcutCts);
            return;
        }

        CancelPendingShoulderShortcuts();
        NavigateHistoryPage(direction, pageSize: 5);
    }

    /// <summary>
    /// 處理 LT 鍵行為（片語首頁、輸入框行首，或與 RT 組合切換隱私模式）。
    /// </summary>
    private void HandleLeftTriggerAction()
    {
        HandleTriggerShortcut(moveToEnd: false, allowDelayedShortcut: true);
    }

    /// <summary>
    /// 處理 LT 長按連發。
    /// </summary>
    private void HandleLeftTriggerRepeat()
    {
        HandleTriggerShortcut(moveToEnd: false, allowDelayedShortcut: false);
    }

    /// <summary>
    /// 處理 RT 鍵行為（片語末頁、輸入框行尾，或與 LT 組合切換隱私模式）。
    /// </summary>
    private void HandleRightTriggerAction()
    {
        HandleTriggerShortcut(moveToEnd: true, allowDelayedShortcut: true);
    }

    /// <summary>
    /// 處理 RT 長按連發。
    /// </summary>
    private void HandleRightTriggerRepeat()
    {
        HandleTriggerShortcut(moveToEnd: true, allowDelayedShortcut: false);
    }

    /// <summary>
    /// 以短延遲區分肩鍵單擊翻頁與肩鍵作為單字跳轉修飾鍵的情境。
    /// </summary>
    /// <param name="direction">歷程翻頁方向。</param>
    /// <param name="pendingShortcutCts">對應肩鍵的暫存取消權杖欄位。</param>
    private void QueueShoulderHistoryShortcut(int direction, ref CancellationTokenSource? pendingShortcutCts)
    {
        ScheduleDelayedGamepadAction(
            ref pendingShortcutCts,
            () => NavigateHistoryPage(direction, pageSize: 5));
    }

    /// <summary>
    /// 處理 LT／RT 單擊與雙壓組合的行為分流。
    /// </summary>
    /// <param name="moveToEnd">true 代表行尾；false 代表行首。</param>
    private void HandleTriggerShortcut(bool moveToEnd, bool allowDelayedShortcut)
    {
        if (HandleContextMenuGamepadInput(moveToEnd ? "PhrasePageLast" : "PhrasePageFirst") ||
            _cmsInput?.Visible == true ||
            IsGamepadInputSuppressed())
        {
            return;
        }

        if (TryTogglePrivacyModeFromTriggerCombo())
        {
            return;
        }

        // 下一次重新以單鍵按下時，允許再次進行 LT+RT 的一次性切換。
        if (_gamepadController?.IsLeftTriggerHeld != true ||
            _gamepadController?.IsRightTriggerHeld != true)
        {
            Interlocked.Exchange(ref _privacyTriggerComboLatched, 0);
        }

        if (!allowDelayedShortcut)
        {
            CancelPendingTriggerShortcuts();
            MoveCursorToBoundary(moveToEnd);
            return;
        }

        if (moveToEnd)
        {
            ScheduleDelayedGamepadAction(ref _rightTriggerShortcutCts, () =>
            {
                if (!TryTogglePrivacyModeFromTriggerCombo())
                {
                    MoveCursorToBoundary(moveToEnd: true);
                }
            });

            return;
        }

        ScheduleDelayedGamepadAction(ref _leftTriggerShortcutCts, () =>
        {
            if (!TryTogglePrivacyModeFromTriggerCombo())
            {
                MoveCursorToBoundary(moveToEnd: false);
            }
        });
    }

    /// <summary>
    /// 嘗試用 LT+RT 雙壓切換隱私模式，只在同一次組合按壓中觸發一次。
    /// </summary>
    /// <returns>若已處理隱私模式切換則回傳 true。</returns>
    private bool TryTogglePrivacyModeFromTriggerCombo()
    {
        if (_gamepadController?.IsLeftTriggerHeld != true ||
            _gamepadController?.IsRightTriggerHeld != true)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _privacyTriggerComboLatched, 1) != 0)
        {
            return true;
        }

        CancelPendingTriggerShortcuts();

        TogglePrivacyMode();
        int privacyDirection = AppSettings.Current.IsPrivacyMode ? 1 : -1;
        FeedbackService.PlaySound(SystemSounds.Asterisk);
        VibrateNavigationAsync(VibrationSemantic.ModeToggle, privacyDirection, VibrationContext.PrivacyMode).SafeFireAndForget();

        return true;
    }

    /// <summary>
    /// 取消待執行的肩鍵單擊捷徑。
    /// </summary>
    private void CancelPendingShoulderShortcuts()
    {
        Interlocked.Exchange(ref _leftShoulderShortcutCts, null)?.CancelAndDispose();
        Interlocked.Exchange(ref _rightShoulderShortcutCts, null)?.CancelAndDispose();
    }

    /// <summary>
    /// 取消待執行的板機單擊捷徑。
    /// </summary>
    private void CancelPendingTriggerShortcuts()
    {
        Interlocked.Exchange(ref _leftTriggerShortcutCts, null)?.CancelAndDispose();
        Interlocked.Exchange(ref _rightTriggerShortcutCts, null)?.CancelAndDispose();
    }

    /// <summary>
    /// 肩鍵已作為單字跳轉修飾鍵時，短暫抑制翻頁連發，避免雙重動作競爭。
    /// </summary>
    private void SuppressShoulderPagingTemporarily()
    {
        _suppressShoulderPagingUntilUtc = DateTime.UtcNow.AddMilliseconds(GamepadModifierGraceDelayMs + 80);
        CancelPendingShoulderShortcuts();
    }

    /// <summary>
    /// 判斷肩鍵翻頁是否暫時被修飾鍵行為抑制。
    /// </summary>
    private bool IsShoulderPagingTemporarilySuppressed()
        => DateTime.UtcNow < _suppressShoulderPagingUntilUtc;

    /// <summary>
    /// 長按時節流重複的邊界提示音與震動，避免回饋過於密集。
    /// </summary>
    private bool ShouldThrottleRepeatedBoundaryFeedback(string key)
    {
        return GetBoundaryFeedbackStage(key) == BoundaryFeedbackStage.Suppressed;
    }

    /// <summary>
    /// 解析目前文字邊界回饋應採用完整、柔和或抑制模式。
    /// </summary>
    private BoundaryFeedbackStage GetBoundaryFeedbackStage(string key)
    {
        DateTime now = DateTime.UtcNow;

        if (string.Equals(_lastBoundaryFeedbackKey, key, StringComparison.Ordinal))
        {
            double elapsedMs = (now - _lastBoundaryFeedbackUtc).TotalMilliseconds;

            if (elapsedMs < RepeatedBoundaryFeedbackThrottleMs)
            {
                return BoundaryFeedbackStage.Suppressed;
            }

            _lastBoundaryFeedbackUtc = now;

            return elapsedMs < BoundarySoftRepeatWindowMs ?
                BoundaryFeedbackStage.SoftRepeat :
                BoundaryFeedbackStage.Full;
        }

        _lastBoundaryFeedbackKey = key;
        _lastBoundaryFeedbackUtc = now;

        return BoundaryFeedbackStage.Full;
    }

    /// <summary>
    /// 在游標成功離開邊界後重設最近一次撞牆節流視窗。
    /// </summary>
    private void ResetBoundaryFeedbackWindow()
    {
        _lastBoundaryFeedbackKey = string.Empty;
        _lastBoundaryFeedbackUtc = DateTime.MinValue;
    }

    /// <summary>
    /// 推進某類觸覺回饋的 burst 等級，用於快速連發時做輕量阻尼。
    /// </summary>
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
    /// 播放歷程導覽的阻尼滾輪手感回饋。
    /// </summary>
    private void PlayHistoryScrollFeedback(int direction)
    {
        IGamepadController? controller = _gamepadController;

        if (controller == null ||
            !controller.IsConnected)
        {
            return;
        }

        int burstLevel = AdvanceFeedbackBurst(
            ref _lastHistoryScrollFeedbackUtc,
            ref _historyScrollBurstLevel,
            HistoryScrollBurstWindowMs);

        VibrateSequenceAsync(
            VibrationPatterns.GetHistoryScrollSequence(direction, burstLevel, controller.VibrationMotorSupport)).SafeFireAndForget();
    }

    /// <summary>
    /// 暫時抑制下一次文字長度變化所觸發的上限預警；用於歷程載入等程式主導更新。
    /// </summary>
    private void SuppressNextTextLimitFeedback()
    {
        Interlocked.Exchange(ref _suppressNextTextLimitFeedback, 1);
    }

    /// <summary>
    /// 依剩餘字元數分級，目前只在接近上限時回傳有效 bucket。
    /// </summary>
    private static int GetTextLimitWarningBucket(int remainingCharacters)
    {
        return remainingCharacters switch
        {
            <= 0 => 3,
            <= 2 => 2,
            <= 5 => 1,
            <= TextLimitWarningThreshold => 0,
            _ => -1
        };
    }

    /// <summary>
    /// 當主輸入框長度變動時，提供接近字數上限的物理預警與硬牆回饋。
    /// </summary>
    private void HandleTextLimitFeedbackFromLengthChange()
    {
        if (TBInput == null ||
            TBInput.IsDisposed)
        {
            return;
        }

        int currentLength = TBInput.TextLength;

        if (Interlocked.Exchange(ref _suppressNextTextLimitFeedback, 0) != 0)
        {
            _lastObservedTextLength = currentLength;
            return;
        }

        if (currentLength < _lastObservedTextLength)
        {
            _lastObservedTextLength = currentLength;
            _lastTextLimitWarningBucket = currentLength >= AppSettings.MaxInputLength - TextLimitWarningThreshold ?
                GetTextLimitWarningBucket(AppSettings.MaxInputLength - currentLength) :
                -1;
            return;
        }

        int remainingCharacters = AppSettings.MaxInputLength - currentLength;

        if (remainingCharacters > TextLimitWarningThreshold)
        {
            _lastObservedTextLength = currentLength;
            _lastTextLimitWarningBucket = -1;
            return;
        }

        int currentBucket = GetTextLimitWarningBucket(remainingCharacters);

        if (currentLength > _lastObservedTextLength &&
            (currentBucket != _lastTextLimitWarningBucket || remainingCharacters <= 2))
        {
            if (remainingCharacters <= 0)
            {
                FeedbackService.PlaySound(SystemSounds.Beep);
            }

            VibrateSequenceAsync(
                VibrationPatterns.GetTextLimitSequence(
                    remainingCharacters,
                    _gamepadController?.VibrationMotorSupport ?? VibrationMotorSupport.None)).SafeFireAndForget();
        }

        _lastObservedTextLength = currentLength;
        _lastTextLimitWarningBucket = currentBucket;
    }

    /// <summary>
    /// 使用者在字數已滿時仍嘗試輸入一般字元，播放硬牆回饋。
    /// </summary>
    private void HandleTextLimitKeyPress(KeyPressEventArgs e)
    {
        if (TBInput == null ||
            TBInput.IsDisposed ||
            char.IsControl(e.KeyChar) ||
            TBInput.TextLength < AppSettings.MaxInputLength)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastTextLimitWallUtc).TotalMilliseconds < RepeatedBoundaryFeedbackThrottleMs)
        {
            return;
        }

        _lastTextLimitWallUtc = DateTime.UtcNow;
        FeedbackService.PlaySound(SystemSounds.Beep);
        VibrateSequenceAsync(
            VibrationPatterns.GetTextLimitSequence(
                0,
                _gamepadController?.VibrationMotorSupport ?? VibrationMotorSupport.None)).SafeFireAndForget();
    }

    /// <summary>
    /// 根據選取粒度與速度播放不同的右搖桿文字選取回饋。
    /// </summary>
    private void PlaySelectionFeedback(int direction, bool wordGranularity)
    {
        IGamepadController? controller = _gamepadController;

        if (controller == null ||
            !controller.IsConnected)
        {
            return;
        }

        int burstLevel = wordGranularity ? 0 : AdvanceFeedbackBurst(
            ref _lastSelectionFeedbackUtc,
            ref _selectionFeedbackBurstLevel,
            SelectionBurstWindowMs);

        VibrateSequenceAsync(
            VibrationPatterns.GetSelectionSequence(direction, wordGranularity, burstLevel, controller.VibrationMotorSupport)).SafeFireAndForget();
    }

    /// <summary>
    /// 以現有的單字跳轉邏輯推算右搖桿在單字粒度下的選取目標位置。
    /// </summary>
    private int GetWordSelectionCaretTarget(int caret, int direction)
    {
        if (TBInput == null ||
            TBInput.IsDisposed)
        {
            return caret;
        }

        int originalStart = TBInput.SelectionStart;
        int originalLength = TBInput.SelectionLength;

        try
        {
            TBInput.SelectionStart = Math.Clamp(caret, 0, TBInput.TextLength);
            TBInput.SelectionLength = 0;
            TBInput.WordJump(direction > 0);

            return TBInput.SelectionStart;
        }
        finally
        {
            TBInput.SelectionStart = originalStart;
            TBInput.SelectionLength = originalLength;
        }
    }

    /// <summary>
    /// 取得資源字串；若缺少翻譯則回退到預設文字。
    /// </summary>
    private static string GetLocalizedString(string resourceKey, string fallback)
    {
        string? localized = Strings.ResourceManager.GetString(resourceKey, Strings.Culture);
        return string.IsNullOrWhiteSpace(localized) ? fallback : localized;
    }

    /// <summary>
    /// 取消待處理的結束程式長按確認。
    /// </summary>
    private void CancelPendingExitHold()
    {
        Interlocked.Exchange(ref _exitHoldLatched, 0);
        Interlocked.Exchange(ref _exitHoldCts, null)?.CancelAndDispose();
    }

    /// <summary>
    /// 啟動 LB + RB + X 的長按結束保護流程。
    /// </summary>
    private void BeginExitHoldConfirmation(IGamepadController controller)
    {
        if (Interlocked.Exchange(ref _exitHoldLatched, 1) != 0)
        {
            return;
        }

        CancellationTokenSource newExitHoldCts = _formCts.TryCreateLinkedTokenSource() ?? new CancellationTokenSource();
        Interlocked.Exchange(ref _exitHoldCts, newExitHoldCts)?.CancelAndDispose();

        AnnounceA11y(
            GetLocalizedString(
                "A11y_Gamepad_ExitHoldStart",
                "Hold LB + RB + X for 0.8 seconds to exit."),
            interrupt: true);

        FeedbackService.VibrateSequenceAsync(
            controller,
            VibrationPatterns.GetExitHoldSequence(confirmed: false, controller.VibrationMotorSupport),
            newExitHoldCts.Token).SafeFireAndForget();

        ConfirmExitHoldAsync(controller, newExitHoldCts).SafeFireAndForget();
    }

    /// <summary>
    /// 非同步等待長按結束保護時間，確認使用者仍持續按住完整組合鍵後才關閉程式。
    /// </summary>
    private async Task ConfirmExitHoldAsync(IGamepadController controller, CancellationTokenSource exitHoldCts)
    {
        try
        {
            await Task.Delay(GamepadExitHoldDelayMs, exitHoldCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        this.SafeInvoke(() =>
        {
            try
            {
                if (exitHoldCts.IsCancellationRequested ||
                    !controller.IsLeftShoulderHeld ||
                    !controller.IsRightShoulderHeld ||
                    !controller.IsXHeld)
                {
                    return;
                }

                AnnounceA11y(Strings.A11y_Menu_Exit_Desc, interrupt: true);

                FeedbackService.VibrateSequenceAsync(
                    controller,
                    VibrationPatterns.GetExitHoldSequence(confirmed: true, controller.VibrationMotorSupport),
                    CancellationToken.None).SafeFireAndForget();

                Close();
            }
            finally
            {
                CancelPendingExitHold();
            }
        });
    }

    /// <summary>
    /// 以柔和、分級的方式播放文字邊界回饋，避免長按撞牆時造成疲勞。
    /// </summary>
    private void PlayTextBoundaryFeedback(int direction, string boundaryKey, string announcement)
    {
        BoundaryFeedbackStage stage = GetBoundaryFeedbackStage(boundaryKey);

        if (stage == BoundaryFeedbackStage.Suppressed)
        {
            return;
        }

        if (stage == BoundaryFeedbackStage.Full)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);
            VibrateNavigationAsync(VibrationSemantic.Boundary, direction, VibrationContext.TextBoundary).SafeFireAndForget();
            AnnounceA11y(announcement, interrupt: true);
            return;
        }

        VibrateAsync(VibrationPatterns.GetRepeatedBoundaryProfile(direction)).SafeFireAndForget();
    }

    /// <summary>
    /// 讓使用者以獨特震動快速辨識目前作用中的控制器。
    /// </summary>
    private Task IdentifyCurrentControllerAsync()
    {
        IGamepadController? controller = _gamepadController;

        if (controller == null ||
            !controller.IsConnected)
        {
            FeedbackService.PlaySound(SystemSounds.Beep);
            AnnounceA11y(
                GetLocalizedString(
                    "A11y_Gamepad_IdentifyUnavailable",
                    "No active controller is available to identify."),
                interrupt: true);

            return Task.CompletedTask;
        }

        string controllerName = string.IsNullOrWhiteSpace(controller.DeviceName) ?
            GetLocalizedString("Menu_Settings_Gamepad", "Gamepad") :
            controller.DeviceName;

        AnnounceA11y(
            string.Format(
                GetLocalizedString(
                    "A11y_Gamepad_IdentifyStarted",
                    "Identifying current controller: {0}."),
                controllerName),
            interrupt: true);

        return FeedbackService.VibrateSequenceAsync(
            controller,
            VibrationPatterns.GetControllerIdentifySequence(controller.VibrationMotorSupport),
            _formCts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// 當主視窗由快速鍵喚起時，播放一個輕巧的控制器握手序列。
    /// </summary>
    private Task PlayShowInputReadyFeedbackAsync()
    {
        IGamepadController? controller = _gamepadController;

        if (controller == null ||
            !controller.IsConnected)
        {
            return VibrateAsync(VibrationPatterns.ShowInput);
        }

        return FeedbackService.VibrateSequenceAsync(
            controller,
            VibrationPatterns.GetFocusHandshakeSequence(controller.VibrationMotorSupport),
            _formCts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// 以短延遲排程控制器單擊捷徑，讓組合鍵擁有優先權。
    /// </summary>
    /// <param name="pendingShortcutCts">目前捷徑的取消權杖欄位。</param>
    /// <param name="action">延遲後要執行的動作。</param>
    private void ScheduleDelayedGamepadAction(ref CancellationTokenSource? pendingShortcutCts, Action action)
    {
        Interlocked.Exchange(ref pendingShortcutCts, null)?.CancelAndDispose();

        CancellationTokenSource pendingCts = new();
        pendingShortcutCts = pendingCts;

        CancellationToken externalToken = _formCts?.Token ?? CancellationToken.None;

        RunDelayedGamepadActionAsync(pendingCts, action, externalToken).SafeFireAndForget();
    }

    /// <summary>
    /// 非同步等待單擊寬限期，若期間未被組合鍵取消再執行實際動作。
    /// </summary>
    private async Task RunDelayedGamepadActionAsync(
        CancellationTokenSource pendingCts,
        Action action,
        CancellationToken externalToken)
    {
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            pendingCts.Token,
            externalToken);

        try
        {
            await Task.Delay(GamepadModifierGraceDelayMs, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        this.SafeInvoke(() =>
        {
            if (!linkedCts.IsCancellationRequested)
            {
                action();
            }
        });
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

        // 組合鍵：LB + RB + X 改為長按確認，避免誤觸直接結束程式。
        if (controller.IsLeftShoulderHeld &&
            controller.IsRightShoulderHeld)
        {
            BeginExitHoldConfirmation(controller);
            return;
        }

        CancelPendingExitHold();

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

        VibrateNavigationAsync(VibrationSemantic.CursorMove, 1).SafeFireAndForget();
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
        bool usedWordJump = false;

        if (hasSelection ||
            TBInput.SelectionStart > 0)
        {
            if (hasSelection)
            {
                TBInput.SelectionLength = 0;
            }
            // 組合鍵：LB／RB + Left 執行單字跳轉。
            else if (_gamepadController?.IsLeftShoulderHeld == true ||
                     _gamepadController?.IsRightShoulderHeld == true)
            {
                SuppressShoulderPagingTemporarily();
                TBInput.WordJump(false);
                usedWordJump = true;
            }
            else
            {
                TBInput.SelectionStart--;
            }

            TBInput.ScrollToCaret();
            ResetBoundaryFeedbackWindow();

            // 手動報讀游標目前的絕對位置。
            AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                Strings.A11y_Cursor_Move_PrivacySafe :
                string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);

            VibrateNavigationAsync(
                usedWordJump ? VibrationSemantic.WordJump : VibrationSemantic.CursorMove,
                -1,
                usedWordJump ? VibrationContext.TextBoundary : VibrationContext.General).SafeFireAndForget();
        }
        else if (TBInput.SelectionStart == 0)
        {
            PlayTextBoundaryFeedback(-1, "CursorStart", Strings.A11y_Nav_Top);
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
        bool usedWordJump = false;

        if (hasSelection ||
            TBInput.SelectionStart < TBInput.Text.Length)
        {
            if (hasSelection)
            {
                TBInput.SelectionStart += TBInput.SelectionLength;
                TBInput.SelectionLength = 0;
            }
            // 組合鍵：LB／RB + Right 執行單字跳轉。
            else if (_gamepadController?.IsLeftShoulderHeld == true ||
                     _gamepadController?.IsRightShoulderHeld == true)
            {
                SuppressShoulderPagingTemporarily();
                TBInput.WordJump(true);
                usedWordJump = true;
            }
            else
            {
                TBInput.SelectionStart++;
            }

            TBInput.ScrollToCaret();
            ResetBoundaryFeedbackWindow();

            // 手動報讀游標目前的絕對位置。
            AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                Strings.A11y_Cursor_Move_PrivacySafe :
                string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);

            VibrateNavigationAsync(
                usedWordJump ? VibrationSemantic.WordJump : VibrationSemantic.CursorMove,
                1,
                usedWordJump ? VibrationContext.TextBoundary : VibrationContext.General).SafeFireAndForget();
        }
        else if (TBInput.SelectionStart == TBInput.Text.Length)
        {
            PlayTextBoundaryFeedback(1, "CursorEnd", Strings.A11y_Nav_Bottom);
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
            string boundaryKey = moveToEnd ? "CursorEnd" : "CursorStart";
            PlayTextBoundaryFeedback(moveToEnd ? 1 : -1, boundaryKey, moveToEnd ? Strings.A11y_Nav_Bottom : Strings.A11y_Nav_Top);
            return;
        }

        TBInput.SelectionStart = target;
        TBInput.SelectionLength = 0;
        TBInput.ScrollToCaret();
        ResetBoundaryFeedbackWindow();

        AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
            Strings.A11y_Cursor_Move_PrivacySafe :
            string.Format(Strings.A11y_Cursor_Move, TBInput.SelectionStart + 1), true);

        VibrateNavigationAsync(VibrationSemantic.PageSwitch, moveToEnd ? 1 : -1, VibrationContext.TextBoundary).SafeFireAndForget();
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
    /// 以目前控制器播放自訂的多段式震動序列。
    /// </summary>
    private Task VibrateSequenceAsync(IReadOnlyList<VibrationSequenceStep> sequence)
    {
        return FeedbackService.VibrateSequenceAsync(
            _gamepadController,
            sequence,
            _formCts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// 以語意、方向與情境播放最佳化的控制器觸覺序列。
    /// </summary>
    private Task VibrateNavigationAsync(
        VibrationSemantic semantic,
        int direction,
        VibrationContext context = VibrationContext.General)
    {
        return FeedbackService.VibrateNavigationAsync(
            _gamepadController,
            semantic,
            direction,
            context,
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
        bool wordGranularity = _gamepadController?.IsLeftShoulderHeld == true ||
                               _gamepadController?.IsRightShoulderHeld == true;

        if (wordGranularity)
        {
            SuppressShoulderPagingTemporarily();
        }

        int newCaret = wordGranularity ?
            GetWordSelectionCaretTarget(caret, safeDirection) :
            Math.Clamp(caret + safeDirection, 0, TBInput.TextLength);

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

        PlaySelectionFeedback(safeDirection, wordGranularity);
    }
}