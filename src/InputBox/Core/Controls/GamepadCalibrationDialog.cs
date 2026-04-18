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
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            TabStop = false;
        }
    }

    private IGamepadController? _gamepadController;
    private CancellationTokenSource? _cts = new();
    private AnnouncerLabel? _announcer;
    private BufferedPanel? _surface;
    private Label? _lblIntro;
    private Label? _lblStatus;
    private Button? _btnReset;
    private Button? _btnClose;
    private FlowLayoutPanel? _buttonRow;
    private TableLayoutPanel? _layoutHost;
    private Font? _a11yFont;
    private float _lastAppliedDpi = -1f;
    private GamepadCalibrationSnapshot _snapshot = GamepadCalibrationSnapshot.Empty;
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

        _a11yFont = InputBox.MainForm.GetSharedA11yFont(DeviceDpi);
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

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    _a11yFont = InputBox.MainForm.GetSharedA11yFont(DeviceDpi);

                    if (_a11yFont != null)
                    {
                        if (_lblIntro != null)
                        {
                            _lblIntro.Font = _a11yFont;
                        }

                        if (_lblStatus != null)
                        {
                            _lblStatus.Font = _a11yFont;
                        }

                        if (_btnReset != null)
                        {
                            _btnReset.Font = _a11yFont;
                        }

                        if (_btnClose != null)
                        {
                            _btnClose.Font = _a11yFont;
                        }
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

    private void HandleDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.Handled = true;
        }
    }

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

    private void HandleDPadPrevious()
    {
        if (ShouldHandleDirectionalFocusNavigation(_gamepadController?.CurrentCalibrationSnapshot ?? _snapshot))
        {
            FocusPreviousButton();
        }
    }

    private void HandleDPadNext()
    {
        if (ShouldHandleDirectionalFocusNavigation(_gamepadController?.CurrentCalibrationSnapshot ?? _snapshot))
        {
            FocusNextButton();
        }
    }

    private void FocusPreviousButton() => MoveButtonFocus(-1);

    private void FocusNextButton() => MoveButtonFocus(+1);

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

    private void UpdateStatusText()
    {
        if (_lblStatus == null)
        {
            return;
        }

        _lblStatus.Text = FormatStatusText(_snapshot);

        if (_btnReset != null)
        {
            _btnReset.Enabled = _snapshot.IsConnected;
        }
    }

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

    internal static bool ShouldHandleDirectionalFocusNavigation(GamepadCalibrationSnapshot snapshot)
    {
        if (!snapshot.IsConnected)
        {
            return true;
        }

        float normalizedDeadzone = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(
            Math.Max(snapshot.ThumbDeadzoneEnter, snapshot.ThumbDeadzoneExit));
        float navigationThreshold = Math.Clamp(MathF.Max(normalizedDeadzone, 0.16f) + 0.02f, 0.12f, 0.35f);

        return MathF.Abs(snapshot.RawLeftX) <= navigationThreshold &&
               MathF.Abs(snapshot.RawLeftY) <= navigationThreshold &&
               MathF.Abs(snapshot.CorrectedLeftX) <= navigationThreshold &&
               MathF.Abs(snapshot.CorrectedLeftY) <= navigationThreshold;
    }

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

    private static string FormatAxis(float value)
    {
        return value.ToString("+0.00;-0.00;0.00");
    }

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

        int margin = 12;
        RectangleF contentBounds = new(
            clientRect.Left + margin,
            clientRect.Top + margin,
            clientRect.Width - (margin * 2),
            clientRect.Height - (margin * 2));

        Color axisColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.DimGray;
        Color deadzoneColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(72, 120, 120, 120);
        Color rawColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(90, 90, 90);
        Color correctedColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.DodgerBlue;

        using Pen outerPen = new(axisColor, 2f);
        using Pen crossPen = new(axisColor, 1f) { DashStyle = DashStyle.Dash };
        using Pen deadzonePen = new(deadzoneColor, 2f);
        using Pen deadzoneExitPen = new(deadzoneColor, 1.5f) { DashStyle = DashStyle.Dash };
        using Pen rawPen = new(rawColor, 2f);
        using Pen correctedOutlinePen = new(axisColor, 1.5f);
        using SolidBrush deadzoneFillBrush = new(Color.FromArgb(SystemInformation.HighContrast ? 36 : 22, deadzoneColor));
        using SolidBrush rawBrush = new(Color.FromArgb(SystemInformation.HighContrast ? 80 : 40, rawColor));
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

        float labelHeight = 54f;
        float plotGap = 16f;
        float plotWidth = (contentBounds.Width - plotGap) / 2f;
        float plotSize = Math.Min(plotWidth, contentBounds.Height - labelHeight - 6f);
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

        graphics.FillEllipse(centerBrush, centerX - 3f, centerY - 3f, 6f, 6f);

        PointF rawPoint = GamepadCalibrationVisualizerMapper.MapToCanvas(plotBounds, rawX, rawY),
            correctedPoint = GamepadCalibrationVisualizerMapper.MapToCanvas(plotBounds, correctedX, correctedY);

        graphics.DrawLine(rawPen, centerX, centerY, rawPoint.X, rawPoint.Y);

        PointF[] diamond =
        [
            new PointF(rawPoint.X, rawPoint.Y - 8f),
            new PointF(rawPoint.X + 8f, rawPoint.Y),
            new PointF(rawPoint.X, rawPoint.Y + 8f),
            new PointF(rawPoint.X - 8f, rawPoint.Y)
        ];
        graphics.FillPolygon(rawBrush, diamond);
        graphics.DrawPolygon(rawPen, diamond);
        graphics.FillEllipse(correctedBrush, correctedPoint.X - 6f, correctedPoint.Y - 6f, 12f, 12f);
        graphics.DrawEllipse(correctedOutlinePen, correctedPoint.X - 6f, correctedPoint.Y - 6f, 12f, 12f);

        Rectangle labelBounds = Rectangle.Round(new RectangleF(plotBounds.Left + 12f, plotBounds.Top - 44f, plotBounds.Width - 24f, 30f));
        graphics.FillRectangle(SystemBrushes.Window, labelBounds);
        TextRenderer.DrawText(
            graphics,
            label,
            _a11yFont ?? Font,
            labelBounds,
            axisColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

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

            Font boldFont = InputBox.MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, (_a11yFont ?? Font).FontFamily);
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

    private Button CreateEyeTrackerButton(string text, string accessibleName, string description, float scale, Font font)
    {
        Font boldFont = InputBox.MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, font.FontFamily);
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

    private InputBox.MainForm? GetOwnerMainForm()
    {
        return Owner as InputBox.MainForm ?? Application.OpenForms.OfType<InputBox.MainForm>().FirstOrDefault();
    }

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
