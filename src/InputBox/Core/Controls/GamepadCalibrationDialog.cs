using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Input;
using InputBox.Resources;
using System.Diagnostics;
using System.Windows.Forms.Automation;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 顯示遊戲控制器校準狀態的視覺化診斷對話框。
/// </summary>
internal sealed partial class GamepadCalibrationDialog : Form
{
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
