using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms.Automation;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 顯示遊戲控制器校準狀態的視覺化診斷對話框。
/// </summary>
internal sealed class GamepadCalibrationDialog : Form
{
    /// <summary>
    /// 使用雙緩衝避免繪圖閃爍。
    /// </summary>
    private sealed class BufferedPanel : Panel
    {
        /// <summary>
        /// 初始化 BufferedPanel，啟用雙緩衝與縮放重繪。
        /// </summary>
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            TabStop = false;
        }
    }

    /// <summary>
    /// 目前指派的遊戲控制器實例。
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 對話框取消權杖來源，用於中止背景工作。
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// 無障礙廣播標籤。
    /// </summary>
    private AnnouncerLabel? _announcer;

    /// <summary>
    /// 繪製搖桿校準視覺化的雙緩衝畫布面板。
    /// </summary>
    private BufferedPanel? _surface;

    /// <summary>
    /// 說明文字標籤。
    /// </summary>
    private Label? _lblIntro;

    /// <summary>
    /// 校準狀態文字標籤。
    /// </summary>
    private Label? _lblStatus;

    /// <summary>
    /// 重設校準按鈕。
    /// </summary>
    private Button? _btnReset;

    /// <summary>
    /// 關閉對話框按鈕。
    /// </summary>
    private Button? _btnClose;

    /// <summary>
    /// 按鈕列容器。
    /// </summary>
    private FlowLayoutPanel? _buttonRow;

    /// <summary>
    /// 整體版面配置容器。
    /// </summary>
    private TableLayoutPanel? _layoutHost;

    /// <summary>
    /// 無障礙字型，依目前 DPI 共用。
    /// </summary>
    private Font? _a11yFont;

    /// <summary>
    /// 上一次套用版面的 DPI 值；用於避免重複計算。
    /// </summary>
    private float _lastAppliedDpi = -1f;

    /// <summary>
    /// 目前控制器校準狀態快照。
    /// </summary>
    private GamepadCalibrationSnapshot _snapshot = GamepadCalibrationSnapshot.Empty;

    /// <summary>
    /// 定期重新整理校準快照與畫布的計時器。
    /// </summary>
    private System.Windows.Forms.Timer? _refreshTimer;

    /// <summary>
    /// 指派控制器實例，並同步診斷快照與事件訂閱。
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
                SubscribeGamepadEvents();
            }

            UpdateSnapshotFromController();
        }
    }

    /// <summary>
    /// 初始化遊戲控制器校準診斷對話框，建立版面與控制項。
    /// </summary>
    public GamepadCalibrationDialog()
    {
        SuspendLayout();

        float scale = DeviceDpi / AppSettings.BaseDpi;

        Text = Strings.Dialog_GamepadCalibrationVisualizer_Title;
        AccessibleName = Strings.Dialog_GamepadCalibrationVisualizer_Title;
        AccessibleDescription = Strings.Dialog_GamepadCalibrationVisualizer_Desc;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScroll = true;
        BackColor = Color.Empty;
        ForeColor = Color.Empty;
        Padding = new Padding((int)(8 * scale), (int)(8 * scale), (int)(8 * scale), (int)(10 * scale));
        Icon = Application.OpenForms.OfType<InputBox.MainForm>().FirstOrDefault()?.Icon ?? ActiveForm?.Icon;

        _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi);
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        _lblIntro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size((int)(560 * scale), 0),
            Margin = new Padding(0, 0, 0, (int)(6 * scale)),
            Text = Strings.Dialog_GamepadCalibrationVisualizer_Desc,
            AccessibleRole = AccessibleRole.StaticText,
            Font = _a11yFont
        };

        _surface = new BufferedPanel
        {
            AccessibleName = Strings.Dialog_GamepadCalibrationVisualizer_CanvasName,
            AccessibleDescription = Strings.Dialog_GamepadCalibrationVisualizer_Canvas_Desc,
            AccessibleRole = AccessibleRole.Graphic,
            Margin = new Padding(0, 0, 0, (int)(6 * scale)),
            MinimumSize = new Size((int)(560 * scale), (int)(280 * scale)),
            Size = new Size((int)(560 * scale), (int)(280 * scale)),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Empty,
            ForeColor = Color.Empty
        };
        _surface.Paint += HandleSurfacePaint;

        _lblStatus = new Label
        {
            AutoSize = false,
            MinimumSize = new Size((int)(560 * scale), (int)(112 * scale)),
            Size = new Size((int)(560 * scale), (int)(112 * scale)),
            Margin = new Padding(0, 0, 0, (int)(2 * scale)),
            Padding = new Padding((int)(12 * scale), (int)(8 * scale), (int)(12 * scale), (int)(8 * scale)),
            BorderStyle = BorderStyle.FixedSingle,
            Font = _a11yFont,
            AccessibleRole = AccessibleRole.StaticText,
            TextAlign = ContentAlignment.TopLeft
        };

        _btnReset = CreateEyeTrackerButton(
            profile.FormatConfirmButtonText(Strings.Menu_Gamepad_ResetCalibration),
            Strings.Menu_Gamepad_ResetCalibration,
            Strings.Menu_Gamepad_ResetCalibration_Desc,
            scale,
            _a11yFont);
        _btnReset.Click += (s, e) => ResetCalibration();

        _btnClose = CreateEyeTrackerButton(
            profile.FormatCancelButtonText(Strings.Btn_Cancel),
            Strings.Btn_Cancel,
            Strings.A11y_Btn_Cancel_Desc,
            scale,
            _a11yFont);
        _btnClose.Click += (s, e) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        _buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0, 0, 0, (int)(2 * scale))
        };
        _buttonRow.Controls.Add(_btnClose);
        _buttonRow.Controls.Add(_btnReset);

        _layoutHost = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0)
        };
        _layoutHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layoutHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layoutHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layoutHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layoutHost.Controls.Add(_lblIntro, 0, 0);
        _layoutHost.Controls.Add(_surface, 0, 1);
        _layoutHost.Controls.Add(_lblStatus, 0, 2);
        _layoutHost.Controls.Add(_buttonRow, 0, 3);

        _announcer = new AnnouncerLabel
        {
            Name = "LblCalibrationA11yAnnouncer",
            AccessibleName = "\u200B",
            Visible = true,
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            TabStop = false,
            Parent = this
        };

        Controls.Add(_layoutHost);

        int dialogWidth = (int)(600 * scale);
        int preferredHeight = _layoutHost.GetPreferredSize(new Size(dialogWidth - Padding.Horizontal, 0)).Height + Padding.Vertical;

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 33,
            Enabled = false
        };
        _refreshTimer.Tick += (s, e) =>
        {
            UpdateSnapshotFromController();
        };

        AcceptButton = _btnReset;
        CancelButton = _btnClose;
        ClientSize = new Size(dialogWidth, preferredHeight);
        MinimumSize = SizeFromClientSize(ClientSize);

        Shown += HandleShown;
        Activated += HandleActivated;
        Deactivate += HandleDeactivate;
        FormClosing += HandleFormClosing;
        KeyDown += HandleDialogKeyDown;

        UpdateSnapshotFromController();

        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// 視窗 Handle 建立後，更新最小尺寸並定位至初始位置。
    /// </summary>
    /// <param name="e">事件引數。</param>
    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);
            UpdateMinimumSize(forceRecalculate: true);
            this.SafeBeginInvoke(ApplySmartPosition);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] OnHandleCreated 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 使用者完成調整視窗大小後，重新套用智慧定位。
    /// </summary>
    /// <param name="e">事件引數。</param>
    protected override void OnResizeEnd(EventArgs e)
    {
        try
        {
            base.OnResizeEnd(e);
            ApplySmartPosition();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] OnResizeEnd 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// DPI 變更時更新字型、最小尺寸，並重新套用智慧定位。
    /// </summary>
    /// <param name="e">包含新舊 DPI 值的事件引數。</param>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi);

                    if (_a11yFont != null)
                    {
                        _lblIntro?.Font = _a11yFont;

                        _lblStatus?.Font = _a11yFont;

                        _btnReset?.Font = _a11yFont;

                        _btnClose?.Font = _a11yFont;
                    }

                    UpdateMinimumSize(forceRecalculate: true);
                    ApplySmartPosition();
                    _surface?.Invalidate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadCalibrationDialog] OnDpiChanged 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] OnDpiChanged 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 對話框顯示後啟動重新整理計時器並播報開啟訊息。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">事件引數。</param>
    private void HandleShown(object? sender, EventArgs e)
    {
        try
        {
            GetOwnerMainForm()?.SetA11yLiveSetting(AutomationLiveSetting.Off);
            _refreshTimer?.Start();
            _btnReset?.Focus();
            _announcer?.Announce(Strings.A11y_Gamepad_CalibrationVisualizer_Open, false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] Shown 處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 對話框取得焦點後延遲恢復控制器輸入。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">事件引數。</param>
    private void HandleActivated(object? sender, EventArgs e)
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(50, _cts?.Token ?? CancellationToken.None);

                this.SafeBeginInvoke(() =>
                {
                    if (IsDisposed ||
                        ActiveForm != this)
                    {
                        return;
                    }

                    _gamepadController?.Resume();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamepadCalibrationDialog] Activated 處理失敗：{ex.Message}");
            }
        }).SafeFireAndForget();
    }

    /// <summary>
    /// 對話框失去焦點後，若無其他前景視窗則暫停控制器輸入。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">事件引數。</param>
    private void HandleDeactivate(object? sender, EventArgs e)
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    if (ActiveForm == null)
                    {
                        _gamepadController?.Pause();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadCalibrationDialog] 暫停控制器失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] Deactivate 處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 對話框關閉前停止計時器、取消訂閱事件並播報關閉訊息。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">包含關閉原因的事件引數。</param>
    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            _refreshTimer?.Stop();
            UnsubscribeGamepadEvents();
            GetOwnerMainForm()?.SetA11yLiveSetting(AutomationLiveSetting.Polite);

            if (DialogResult == DialogResult.Cancel)
            {
                _announcer?.Announce(Strings.A11y_Cancelled, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] FormClosing 處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 攔截鍵盤輸入，按下 Escape 時關閉對話框。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">包含按鍵資訊的事件引數。</param>
    private void HandleDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 訂閱目前控制器的所有輸入事件。
    /// </summary>
    private void SubscribeGamepadEvents()
    {
        if (_gamepadController == null)
        {
            return;
        }

        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        _gamepadController.APressed += profile.ConfirmOnSouth ? HandleGamepadConfirm : HandleGamepadCancel;
        _gamepadController.StartPressed += HandleGamepadConfirm;
        _gamepadController.BPressed += profile.ConfirmOnSouth ? HandleGamepadCancel : HandleGamepadConfirm;
        _gamepadController.BackPressed += HandleGamepadCancel;
        _gamepadController.YPressed += HandleGamescopeSurfaceRecovery;
        _gamepadController.LeftPressed += HandleDPadPrevious;
        _gamepadController.LeftRepeat += HandleDPadPrevious;
        _gamepadController.UpPressed += HandleDPadPrevious;
        _gamepadController.UpRepeat += HandleDPadPrevious;
        _gamepadController.RightPressed += HandleDPadNext;
        _gamepadController.RightRepeat += HandleDPadNext;
        _gamepadController.DownPressed += HandleDPadNext;
        _gamepadController.DownRepeat += HandleDPadNext;
        _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
    }

    /// <summary>
    /// 取消訂閱目前控制器的所有輸入事件。
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController == null)
            {
                return;
            }

            _gamepadController.APressed -= HandleGamepadConfirm;
            _gamepadController.APressed -= HandleGamepadCancel;
            _gamepadController.StartPressed -= HandleGamepadConfirm;
            _gamepadController.BPressed -= HandleGamepadConfirm;
            _gamepadController.BPressed -= HandleGamepadCancel;
            _gamepadController.BackPressed -= HandleGamepadCancel;
            _gamepadController.YPressed -= HandleGamescopeSurfaceRecovery;
            _gamepadController.LeftPressed -= HandleDPadPrevious;
            _gamepadController.LeftRepeat -= HandleDPadPrevious;
            _gamepadController.UpPressed -= HandleDPadPrevious;
            _gamepadController.UpRepeat -= HandleDPadPrevious;
            _gamepadController.RightPressed -= HandleDPadNext;
            _gamepadController.RightRepeat -= HandleDPadNext;
            _gamepadController.DownPressed -= HandleDPadNext;
            _gamepadController.DownRepeat -= HandleDPadNext;
            _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] 取消訂閱控制器事件失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 控制器連線狀態變更時更新快照並播報連線訊息。
    /// </summary>
    /// <param name="isConnected">控制器是否已連線。</param>
    private void HandleGamepadConnectionChanged(bool isConnected)
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                UpdateSnapshotFromController();
                _announcer?.Announce(FormatConnectionAnnouncement(isConnected, _gamepadController?.DeviceName), true);
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "GamepadCalibrationDialog.HandleGamepadConnectionChanged 失敗");
            Debug.WriteLine($"[GamepadCalibrationDialog] 控制器連線變更處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理控制器確認按鍵，觸發目前焦點按鈕或預設按鈕的點擊。
    /// </summary>
    private void HandleGamepadConfirm()
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    if (ActiveControl is Button activeButton &&
                        activeButton.Enabled)
                    {
                        activeButton.PerformClick();
                    }
                    else
                    {
                        (_btnReset ?? AcceptButton as Button)?.PerformClick();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadConfirm UI 失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadConfirm 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理控制器取消按鍵，觸發關閉按鈕或直接關閉對話框。
    /// </summary>
    private void HandleGamepadCancel()
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    if (_btnClose != null &&
                        !_btnClose.IsDisposed)
                    {
                        _btnClose.PerformClick();
                    }
                    else
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadCancel UI 失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] HandleGamepadCancel 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理 Gamescope 專用 surface recovery 組合鍵。
    /// </summary>
    private void HandleGamescopeSurfaceRecovery()
    {
        GamescopeSurfaceRecovery.TryRecoverFromGamepadChord(
            this,
            RecreateHandle,
            _gamepadController,
            context: "GamepadCalibrationDialog Gamescope surface recovery 失敗");
    }

    /// <summary>
    /// 處理 D-Pad 向前（左/上）輸入，在搖桿不干擾時移動焦點至前一個按鈕。
    /// </summary>
    private void HandleDPadPrevious()
    {
        if (ShouldHandleDirectionalFocusNavigation(_gamepadController?.CurrentCalibrationSnapshot ?? _snapshot))
        {
            FocusPreviousButton();
        }
    }

    /// <summary>
    /// 處理 D-Pad 向後（右/下）輸入，在搖桿不干擾時移動焦點至下一個按鈕。
    /// </summary>
    private void HandleDPadNext()
    {
        if (ShouldHandleDirectionalFocusNavigation(_gamepadController?.CurrentCalibrationSnapshot ?? _snapshot))
        {
            FocusNextButton();
        }
    }

    /// <summary>
    /// 將焦點移至前一個按鈕。
    /// </summary>
    private void FocusPreviousButton() => MoveButtonFocus(-1);

    /// <summary>
    /// 將焦點移至下一個按鈕。
    /// </summary>
    private void FocusNextButton() => MoveButtonFocus(+1);

    /// <summary>
    /// 依方向在重設與關閉按鈕之間移動焦點。
    /// </summary>
    /// <param name="direction">方向值；負值移往重設按鈕，正值移往關閉按鈕。</param>
    private void MoveButtonFocus(int direction)
    {
        try
        {
            this.SafeBeginInvoke(() =>
            {
                if (IsDisposed || _btnReset == null || _btnClose == null)
                {
                    return;
                }

                Button nextButton = direction < 0 ?
                    ActiveControl == _btnClose ? _btnReset : _btnClose :
                    ActiveControl == _btnReset ? _btnClose : _btnReset;

                nextButton.Focus();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] MoveButtonFocus 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 從控制器取得最新校準快照，更新狀態文字並觸發畫面重繪。
    /// </summary>
    private void UpdateSnapshotFromController()
    {
        GamepadCalibrationSnapshot snapshot = _gamepadController?.CurrentCalibrationSnapshot ?? new GamepadCalibrationSnapshot
        {
            IsConnected = false,
            ThumbDeadzoneEnter = AppSettings.Current.ThumbDeadzoneEnter,
            ThumbDeadzoneExit = AppSettings.Current.ThumbDeadzoneExit,
            TimestampUtc = DateTime.UtcNow
        };

        _snapshot = snapshot;
        UpdateStatusText();
        _surface?.Invalidate();
    }

    /// <summary>
    /// 依目前快照更新狀態標籤文字，並同步重設按鈕的啟用狀態。
    /// </summary>
    private void UpdateStatusText()
    {
        if (_lblStatus == null)
        {
            return;
        }

        _lblStatus.Text = FormatStatusText(_snapshot);

        _btnReset?.Enabled = _snapshot.IsConnected;
    }

    /// <summary>
    /// 依連線狀態與裝置名稱產生無障礙廣播訊息字串。
    /// </summary>
    /// <param name="isConnected">控制器是否已連線。</param>
    /// <param name="deviceName">裝置名稱；可為 null。</param>
    /// <returns>格式化後的連線狀態廣播訊息。</returns>
    internal static string FormatConnectionAnnouncement(bool isConnected, string? deviceName)
    {
        string template = isConnected ?
            Strings.A11y_Gamepad_Connected :
            Strings.A11y_Gamepad_Disconnected;

        string message = string.Format(template, deviceName?.Trim() ?? string.Empty);

        while (message.Contains("  ", StringComparison.Ordinal))
        {
            message = message.Replace("  ", " ", StringComparison.Ordinal);
        }

        return message.Replace(" .", ".", StringComparison.Ordinal).Trim();
    }

    /// <summary>
    /// 判斷目前搖桿是否靜止到足以安全處理方向焦點移動，避免搖桿偏移誤觸焦點導覽。
    /// </summary>
    /// <param name="snapshot">目前的校準狀態快照。</param>
    /// <returns>若搖桿偏移量低於安全閾值（或控制器未連線）則回傳 true。</returns>
    internal static bool ShouldHandleDirectionalFocusNavigation(GamepadCalibrationSnapshot snapshot)
    {
        if (!snapshot.IsConnected)
        {
            return true;
        }

        float normalizedDeadzone = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(
            Math.Max(snapshot.ThumbDeadzoneEnter, snapshot.ThumbDeadzoneExit));

        // 門檻必須低於 normalizedDeadzone，確保 LS 剛跨越 ThumbDeadzoneEnter
        // 觸發搖桿→D-Pad 映射的瞬間，保護邏輯已阻止方向焦點移動。
        float navigationThreshold = Math.Clamp(normalizedDeadzone * 0.75f, 0.06f, 0.18f);

        return MathF.Abs(snapshot.RawLeftX) <= navigationThreshold &&
               MathF.Abs(snapshot.RawLeftY) <= navigationThreshold &&
               MathF.Abs(snapshot.CorrectedLeftX) <= navigationThreshold &&
               MathF.Abs(snapshot.CorrectedLeftY) <= navigationThreshold;
    }

    /// <summary>
    /// 依校準快照產生狀態文字標籤內容。
    /// </summary>
    /// <param name="snapshot">目前的校準狀態快照。</param>
    /// <returns>格式化後的狀態文字；控制器未連線時回傳中斷連線提示訊息。</returns>
    internal static string FormatStatusText(GamepadCalibrationSnapshot snapshot)
    {
        if (!snapshot.IsConnected)
        {
            return Strings.Dialog_GamepadCalibrationVisualizer_StatusDisconnected;
        }

        return string.Format(
            Strings.Dialog_GamepadCalibrationVisualizer_StatusConnected,
            FormatAxis(snapshot.RawLeftX),
            FormatAxis(snapshot.RawLeftY),
            FormatAxis(snapshot.CorrectedLeftX),
            FormatAxis(snapshot.CorrectedLeftY),
            FormatAxis(snapshot.RawRightX),
            FormatAxis(snapshot.RawRightY),
            FormatAxis(snapshot.CorrectedRightX),
            FormatAxis(snapshot.CorrectedRightY),
            snapshot.ThumbDeadzoneEnter,
            snapshot.ThumbDeadzoneExit);
    }

    /// <summary>
    /// 將正規化軸值格式化為帶符號的兩位小數字串。
    /// </summary>
    /// <param name="value">正規化軸值（-1.0 ~ 1.0）。</param>
    /// <returns>格式化後的字串，例如 "+0.75" 或 "-0.12"。</returns>
    private static string FormatAxis(float value)
    {
        return value.ToString("+0.00;-0.00;0.00");
    }

    /// <summary>
    /// 重設控制器校準資料，播放音效與震動回饋並更新快照。
    /// </summary>
    private void ResetCalibration()
    {
        try
        {
            _gamepadController?.ResetCalibration();
            SystemSounds.Asterisk.Play();
            PlayResetCalibrationFeedbackAsync().SafeFireAndForget();
            UpdateSnapshotFromController();
            _announcer?.Announce(Strings.A11y_Gamepad_CalibrationReset, true);
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "重設校準視覺化狀態失敗");
            Debug.WriteLine($"[GamepadCalibrationDialog] ResetCalibration 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 播放重設校準的三段式震動序列回饋。
    /// </summary>
    /// <returns>代表震動序列播放過程的非同步工作。</returns>
    private async Task PlayResetCalibrationFeedbackAsync()
    {
        IGamepadController? controller = _gamepadController;

        if (controller == null)
        {
            return;
        }

        CancellationToken token = _cts?.Token ?? CancellationToken.None;

        VibrationProfile[] sequence =
        [
            new(22000, 26, 1.0f, 0.18f, 0.52f, 0.04f),
            new(22000, 26, 0.18f, 1.0f, 0.04f, 0.52f),
            new(18000, 22, 0.62f, 0.62f, 0.18f, 0.18f)
        ];

        foreach (VibrationProfile profile in sequence)
        {
            token.ThrowIfCancellationRequested();
            await controller.VibrateAsync(profile, VibrationPriority.Normal, token);
            await Task.Delay(20, token);
        }
    }

    /// <summary>
    /// 繪製校準視覺化畫布，包含雙搖桿軌跡圖與死區圓圈。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">包含繪圖 Graphics 的事件引數。</param>
    private void HandleSurfacePaint(object? sender, PaintEventArgs e)
    {
        if (_surface == null)
        {
            return;
        }

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SystemColors.Window);

        Rectangle clientRect = _surface.ClientRectangle;

        if (clientRect.Width <= 20 || clientRect.Height <= 20)
        {
            return;
        }

        float s = DeviceDpi / AppSettings.BaseDpi;
        int margin = (int)(12 * s);
        RectangleF contentBounds = new(
            clientRect.Left + margin,
            clientRect.Top + margin,
            clientRect.Width - (margin * 2),
            clientRect.Height - (margin * 2));

        Color axisColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.DimGray;
        Color deadzoneColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(72, 120, 120, 120);
        Color rawColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(90, 90, 90);
        Color correctedColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.DodgerBlue;

        using Pen outerPen = new(axisColor, 2f * s);
        using Pen crossPen = new(axisColor, 1.5f * s) { DashStyle = DashStyle.Dash };
        using Pen deadzonePen = new(deadzoneColor, 2.5f * s);
        using Pen deadzoneExitPen = new(deadzoneColor, 2f * s) { DashStyle = DashStyle.Dash };
        using Pen rawPen = new(rawColor, 2.5f * s);
        using Pen correctedOutlinePen = new(axisColor, 2f * s);
        using SolidBrush deadzoneFillBrush = new(Color.FromArgb(SystemInformation.HighContrast ? 60 : 48, deadzoneColor));
        using SolidBrush rawBrush = new(Color.FromArgb(SystemInformation.HighContrast ? 100 : 64, rawColor));
        using SolidBrush correctedBrush = new(correctedColor);
        using SolidBrush centerBrush = new(axisColor);

        if (!_snapshot.IsConnected)
        {
            TextRenderer.DrawText(
                graphics,
                Strings.Dialog_GamepadCalibrationVisualizer_StatusDisconnected,
                _a11yFont ?? Font,
                Rectangle.Round(contentBounds),
                axisColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

            return;
        }

        float labelHeight = 54f * s;
        float plotGap = 16f * s;
        float plotVerticalPadding = 6f * s;
        float plotWidth = (contentBounds.Width - plotGap) / 2f;
        float plotSize = Math.Min(plotWidth, contentBounds.Height - labelHeight - plotVerticalPadding);
        float verticalOffset = (contentBounds.Height - labelHeight - plotSize) / 2f;

        RectangleF leftPlot = new(
            contentBounds.Left + ((plotWidth - plotSize) / 2f),
            contentBounds.Top + labelHeight + verticalOffset,
            plotSize,
            plotSize);
        RectangleF rightPlot = new(
            contentBounds.Left + plotWidth + plotGap + ((plotWidth - plotSize) / 2f),
            contentBounds.Top + labelHeight + verticalOffset,
            plotSize,
            plotSize);

        DrawStickPlot(graphics, leftPlot, "LS", _snapshot.RawLeftX, _snapshot.RawLeftY, _snapshot.CorrectedLeftX, _snapshot.CorrectedLeftY, axisColor, outerPen, crossPen, deadzonePen, deadzoneExitPen, rawPen, correctedOutlinePen, deadzoneFillBrush, rawBrush, correctedBrush, centerBrush);
        DrawStickPlot(graphics, rightPlot, "RS", _snapshot.RawRightX, _snapshot.RawRightY, _snapshot.CorrectedRightX, _snapshot.CorrectedRightY, axisColor, outerPen, crossPen, deadzonePen, deadzoneExitPen, rawPen, correctedOutlinePen, deadzoneFillBrush, rawBrush, correctedBrush, centerBrush);
    }

    /// <summary>
    /// 在指定範圍內繪製單一搖桿的校準圖，包含死區、原始與修正後軌跡。
    /// </summary>
    /// <param name="graphics">目標 GDI+ 繪圖物件。</param>
    /// <param name="plotBounds">搖桿圖的像素邊界矩形。</param>
    /// <param name="label">搖桿標籤（如 "LS" 或 "RS"）。</param>
    /// <param name="rawX">原始 X 軸正規化值（-1.0 ~ 1.0）。</param>
    /// <param name="rawY">原始 Y 軸正規化值（-1.0 ~ 1.0）。</param>
    /// <param name="correctedX">死區修正後 X 軸正規化值。</param>
    /// <param name="correctedY">死區修正後 Y 軸正規化值。</param>
    /// <param name="axisColor">座標軸與外框顏色。</param>
    /// <param name="outerPen">外框圓圈畫筆。</param>
    /// <param name="crossPen">十字準線畫筆。</param>
    /// <param name="deadzonePen">進入死區圓圈畫筆。</param>
    /// <param name="deadzoneExitPen">退出死區虛線圓圈畫筆。</param>
    /// <param name="rawPen">原始軌跡線畫筆。</param>
    /// <param name="correctedOutlinePen">修正點外框畫筆。</param>
    /// <param name="deadzoneFillBrush">死區填滿筆刷。</param>
    /// <param name="rawBrush">原始位置填滿筆刷。</param>
    /// <param name="correctedBrush">修正後位置填滿筆刷。</param>
    /// <param name="centerBrush">中心點填滿筆刷。</param>
    private void DrawStickPlot(Graphics graphics, RectangleF plotBounds, string label, float rawX, float rawY, float correctedX, float correctedY, Color axisColor, Pen outerPen, Pen crossPen, Pen deadzonePen, Pen deadzoneExitPen, Pen rawPen, Pen correctedOutlinePen, Brush deadzoneFillBrush, Brush rawBrush, Brush correctedBrush, Brush centerBrush)
    {
        graphics.DrawEllipse(outerPen, plotBounds);

        float centerX = plotBounds.Left + (plotBounds.Width / 2f),
            centerY = plotBounds.Top + (plotBounds.Height / 2f);

        graphics.DrawLine(crossPen, plotBounds.Left, centerY, plotBounds.Right, centerY);
        graphics.DrawLine(crossPen, centerX, plotBounds.Top, centerX, plotBounds.Bottom);

        float deadzoneRadius = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(_snapshot.ThumbDeadzoneEnter) * (plotBounds.Width / 2f);
        graphics.FillEllipse(deadzoneFillBrush, centerX - deadzoneRadius, centerY - deadzoneRadius, deadzoneRadius * 2f, deadzoneRadius * 2f);
        graphics.DrawEllipse(deadzonePen, centerX - deadzoneRadius, centerY - deadzoneRadius, deadzoneRadius * 2f, deadzoneRadius * 2f);

        float exitDeadzoneRadius = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(_snapshot.ThumbDeadzoneExit) * (plotBounds.Width / 2f);
        graphics.DrawEllipse(deadzoneExitPen, centerX - exitDeadzoneRadius, centerY - exitDeadzoneRadius, exitDeadzoneRadius * 2f, exitDeadzoneRadius * 2f);

        float dpiScale = DeviceDpi / AppSettings.BaseDpi;
        graphics.FillEllipse(centerBrush, centerX - 3f * dpiScale, centerY - 3f * dpiScale, 6f * dpiScale, 6f * dpiScale);

        PointF rawPoint = GamepadCalibrationVisualizerMapper.MapToCanvas(plotBounds, rawX, rawY),
            correctedPoint = GamepadCalibrationVisualizerMapper.MapToCanvas(plotBounds, correctedX, correctedY);

        graphics.DrawLine(rawPen, centerX, centerY, rawPoint.X, rawPoint.Y);

        float dm = 8f * dpiScale;
        PointF[] diamond =
        [
            new PointF(rawPoint.X, rawPoint.Y - dm),
            new PointF(rawPoint.X + dm, rawPoint.Y),
            new PointF(rawPoint.X, rawPoint.Y + dm),
            new PointF(rawPoint.X - dm, rawPoint.Y)
        ];
        graphics.FillPolygon(rawBrush, diamond);
        graphics.DrawPolygon(rawPen, diamond);
        float cr = 6f * dpiScale;
        graphics.FillEllipse(correctedBrush, correctedPoint.X - cr, correctedPoint.Y - cr, cr * 2f, cr * 2f);
        graphics.DrawEllipse(correctedOutlinePen, correctedPoint.X - cr, correctedPoint.Y - cr, cr * 2f, cr * 2f);

        Rectangle labelBounds = Rectangle.Round(new RectangleF(plotBounds.Left + 12f * dpiScale, plotBounds.Top - 44f * dpiScale, plotBounds.Width - 24f * dpiScale, 30f * dpiScale));
        graphics.FillRectangle(SystemBrushes.Window, labelBounds);
        TextRenderer.DrawText(
            graphics,
            label,
            _a11yFont ?? Font,
            labelBounds,
            axisColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// 依目前 DPI 與可用工作區重新計算並套用對話框的最小尺寸。
    /// </summary>
    /// <param name="forceRecalculate">是否強制重新計算，忽略 DPI 未變更的快取防呆。</param>
    private void UpdateMinimumSize(bool forceRecalculate = false)
    {
        try
        {
            if (_layoutHost == null ||
                _surface == null ||
                _lblIntro == null ||
                _lblStatus == null ||
                _btnReset == null ||
                _btnClose == null ||
                _buttonRow == null)
            {
                return;
            }

            float currentDpi = DeviceDpi;

            if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi, forceRecalculate))
            {
                return;
            }

            float scale = currentDpi / AppSettings.BaseDpi;
            Rectangle workArea = Screen.GetWorkingArea(this);
            (int maxFitWidth, int maxFitHeight) = DialogLayoutHelper.GetMaxFitSize(workArea);

            int targetWindowWidth = Math.Clamp((int)(600 * scale), Math.Min(maxFitWidth, (int)(420 * scale)), maxFitWidth);
            int contentWidth = Math.Max(220, targetWindowWidth - Padding.Horizontal);

            _lblIntro.MaximumSize = new Size(contentWidth, 0);
            _lblIntro.Margin = new Padding(0, 0, 0, (int)(6 * scale));

            Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, (_a11yFont ?? Font).FontFamily);
            DialogLayoutHelper.UpdateButtonMinimumSize(_btnReset, boldFont, scale, 120, 56, 32, 20);
            DialogLayoutHelper.UpdateButtonMinimumSize(_btnClose, boldFont, scale, 120, 56, 32, 20);

            int buttonHeight = _buttonRow.GetPreferredSize(new Size(contentWidth, 0)).Height;
            int introHeight = _lblIntro.GetPreferredSize(new Size(contentWidth, 0)).Height;
            int statusHeight = Math.Max((int)(96 * scale), _lblStatus.GetPreferredSize(new Size(contentWidth, 0)).Height);
            int availableSurfaceHeight = Math.Max((int)(160 * scale), maxFitHeight - Padding.Vertical - introHeight - statusHeight - buttonHeight - (int)(36 * scale));
            int surfaceHeight = Math.Min((int)(280 * scale), availableSurfaceHeight);

            _surface.MinimumSize = new Size(contentWidth, surfaceHeight);
            _surface.Size = _surface.MinimumSize;
            _lblStatus.MinimumSize = new Size(contentWidth, statusHeight);
            _lblStatus.Size = _lblStatus.MinimumSize;
            _buttonRow.WrapContents = _buttonRow.GetPreferredSize(Size.Empty).Width > contentWidth;

            int preferredHeight = _layoutHost.GetPreferredSize(new Size(contentWidth, 0)).Height + Padding.Vertical;
            int targetWindowHeight = Math.Min(preferredHeight, maxFitHeight);
            int minWindowWidth = Math.Min(targetWindowWidth, maxFitWidth);
            int minWindowHeight = Math.Min(targetWindowHeight, maxFitHeight);

            ClientSize = new Size(minWindowWidth, targetWindowHeight);
            DialogLayoutHelper.ClampFormSize(this, minWindowWidth, minWindowHeight, maxFitWidth, maxFitHeight, ApplySmartPosition);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] UpdateMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 將對話框位置限制在螢幕可視範圍內，避免視窗超出邊界。
    /// </summary>
    private void ApplySmartPosition()
    {
        try
        {
            if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
            {
                Location = clampedLocation;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] ApplySmartPosition 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 建立符合眼球追蹤規格的大型按鈕，並附加懸停回饋。
    /// </summary>
    /// <param name="text">按鈕顯示文字。</param>
    /// <param name="accessibleName">按鈕無障礙名稱。</param>
    /// <param name="description">按鈕無障礙描述與懸停提示。</param>
    /// <param name="scale">目前 DPI 縮放比例。</param>
    /// <param name="font">按鈕字型。</param>
    /// <returns>已設定樣式與無障礙屬性的按鈕執行個體。</returns>
    private Button CreateEyeTrackerButton(string text, string accessibleName, string description, float scale, Font font)
    {
        Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, font.FontFamily);
        Size boldTextSize = TextRenderer.MeasureText(text, boldFont);

        Button btn = new()
        {
            Text = text,
            AccessibleName = accessibleName,
            AccessibleDescription = description,
            AccessibleRole = AccessibleRole.PushButton,
            Font = font,
            AutoSize = true,
            MinimumSize = new Size(Math.Max((int)(120 * scale), boldTextSize.Width + (int)(32 * scale)), Math.Max((int)(56 * scale), boldTextSize.Height + (int)(20 * scale))),
            Margin = new Padding((int)(8 * scale), (int)(6 * scale), (int)(8 * scale), (int)(4 * scale)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Empty,
            ForeColor = Color.Empty
        };

        btn.FlatAppearance.BorderSize = 0;

        btn.AttachEyeTrackerFeedback(
            description,
            font,
            boldFont,
            _cts?.Token ?? CancellationToken.None);

        return btn;
    }

    /// <summary>
    /// 取得擁有此對話框的主視窗實例。
    /// </summary>
    /// <returns>MainForm 實例；若無法取得則回傳 null。</returns>
    private InputBox.MainForm? GetOwnerMainForm()
    {
        return Owner as InputBox.MainForm ?? Application.OpenForms.OfType<InputBox.MainForm>().FirstOrDefault();
    }

    /// <summary>
    /// 釋放受管理資源，停止計時器並清除所有控制項參考。
    /// </summary>
    /// <param name="disposing">true 表示由 Dispose() 呼叫；false 表示由完成項呼叫。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeGamepadEvents();
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
            Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
            _surface?.Dispose();
            _lblIntro?.Dispose();
            _btnReset?.Dispose();
            _btnClose?.Dispose();
            _buttonRow?.Dispose();
            _layoutHost?.Dispose();
            _lblStatus?.Dispose();
            _announcer?.Dispose();

            _surface = null;
            _lblIntro = null;
            _btnReset = null;
            _btnClose = null;
            _buttonRow = null;
            _layoutHost = null;
            _lblStatus = null;
            _announcer = null;
            _refreshTimer = null;
            _a11yFont = null;
        }

        base.Dispose(disposing);
    }
}
