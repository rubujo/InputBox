using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 支援遊戲控制器的訊息對話框
/// A 鍵觸發確認（是／OK／重試），B 鍵觸發取消（否／取消／中止）
/// </summary>
internal sealed class GamepadMessageBox : Form
{
    /// <summary>
    /// 遊戲控制器
    /// </summary>

    private IGamepadController? _gamepadController;

    /// <summary>
    /// 設定遊戲控制器。setter 會自動解除舊控制器訂閱並訂閱新控制器
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

            SyncHintVisibilityWithControllerState();
        }
    }

    /// <summary>
    /// 依目前控制器的即時連線狀態同步無障礙描述，避免對話框剛開啟時要等第二次事件才更新。
    /// </summary>
    private void SyncHintVisibilityWithControllerState()
    {
        try
        {
            UpdateDialogAccessibleDescription();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadMessageBox] 同步無障礙描述失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 訂閱遊戲控制器事件，將 A／B 鍵對應到主／取消按鈕，DPad 左右切換焦點，並監聽連線狀態變更
    /// </summary>
    private void SubscribeGamepadEvents()
    {
        if (_gamepadController == null)
        {
            return;
        }

        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        _gamepadController.APressed += profile.ConfirmOnSouth ? HandleGamepadA : HandleGamepadB;
        _gamepadController.StartPressed += HandleGamepadA;
        _gamepadController.BPressed += profile.ConfirmOnSouth ? HandleGamepadB : HandleGamepadA;
        _gamepadController.BackPressed += HandleGamepadB;
        _gamepadController.LeftPressed += HandleDPadLeft;
        _gamepadController.LeftRepeat += HandleDPadLeft;
        _gamepadController.RightPressed += HandleDPadRight;
        _gamepadController.RightRepeat += HandleDPadRight;
        _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
    }

    /// <summary>
    /// 取消訂閱遊戲控制器事件，避免對話框關閉後仍持有控制器引用或接收事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController == null)
            {
                return;
            }

            _gamepadController.APressed -= HandleGamepadA;
            _gamepadController.APressed -= HandleGamepadB;
            _gamepadController.StartPressed -= HandleGamepadA;
            _gamepadController.BPressed -= HandleGamepadA;
            _gamepadController.BPressed -= HandleGamepadB;
            _gamepadController.BackPressed -= HandleGamepadB;
            _gamepadController.LeftPressed -= HandleDPadLeft;
            _gamepadController.LeftRepeat -= HandleDPadLeft;
            _gamepadController.RightPressed -= HandleDPadRight;
            _gamepadController.RightRepeat -= HandleDPadRight;
            _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadMessageBox] 取消訂閱控制器事件失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 播報器，用於語音播報訊息。
    /// </summary>
    private AnnouncerLabel? _announcer;

    /// <summary>
    /// 取消操作的取消標記。
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// 目前套用的字型（來自 MainForm 的共享字型），
    /// 用於在 DPI 變更時同步更新按鈕字型與最小尺寸
    /// 對話框關閉時會歸零欄位引用，
    /// 但不負責處置共享字型實例
    /// </summary>
    private Font? _currentFont;

    /// <summary>
    /// 上一次套用的 DPI，用於在 DPI 變更時判斷是否需要更新字型與佈局
    /// </summary>
    private float _lastAppliedDpi;

    /// <summary>
    /// A 鍵觸發的主按鈕（確認／是／重試）
    /// </summary>
    private Button? _primaryButton;

    /// <summary>
    /// B 鍵觸發的取消按鈕（取消／否／中止）
    /// </summary>
    private Button? _cancelButton;

    /// <summary>
    /// 所有已建立的按鈕，供字型同步與 DPI 更新使用
    /// </summary>
    private readonly List<Button> _allButtons = [];

    /// <summary>
    /// 圖示欄位（DPI 更新時調整大小）
    /// </summary>
    private PictureBox? _iconPictureBox;

    /// <summary>
    /// 訊息文字標籤（DPI 更新時調整寬度上限）
    /// </summary>
    private Label? _lblText;

    /// <summary>
    /// 對話框圖示類型（用於 A11y 描述）
    /// </summary>
    private MessageBoxIcon _icon;

    /// <summary>
    /// 內容列面板（DPI 更新時調整 Padding）
    /// </summary>
    private TableLayoutPanel? _contentPanel;

    /// <summary>
    /// 按鈕流向面板（DPI 更新時調整 Margin）
    /// </summary>
    private FlowLayoutPanel? _buttonFlow;

    /// <summary>
    /// 建立 GamepadMessageBox
    /// </summary>
    /// <param name="text">對話框主體訊息文字。</param>
    /// <param name="caption">對話框標題列文字。</param>
    /// <param name="buttons">按鈕組合。</param>
    /// <param name="icon">顯示的系統圖示。</param>
    /// <param name="defaultButton">預設焦點按鈕。</param>
    internal GamepadMessageBox(
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        // 基本視窗屬性。
        SuspendLayout();

        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Padding = new Padding(0);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // 繼承圖示：優先從主視窗繼承，保持應用程式視覺識別的一致性。
        Icon = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.Icon ??
            ActiveForm?.Icon;

        // 建立內容面板。
        BuildLayout(text, icon, buttons, defaultButton);

        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// 建立對話框的版面配置
    /// </summary>
    /// <param name="text">對話框主體訊息文字。</param>
    /// <param name="icon">顯示的系統圖示。</param>
    /// <param name="buttons">按鈕組合。</param>
    /// <param name="defaultButton">預設焦點按鈕。</param>
    private void BuildLayout(
        string text,
        MessageBoxIcon icon,
        MessageBoxButtons buttons,
        MessageBoxDefaultButton defaultButton)
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        _icon = icon;

        // 外層垂直面板，由上而下：內容列 + 按鈕列 + 播報器。
        TableLayoutPanel outer = new()
        {
            RowCount = 3,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        // 內容列。
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 按鈕列。
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 播報器。
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.Padding = new Padding(0);

        // 內容列：圖示 + 文字。
        TableLayoutPanel content = new()
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding((int)(12 * scale), (int)(12 * scale), (int)(12 * scale), (int)(6 * scale)),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _contentPanel = content;

        // 圖示（若有）。
        Image? iconImage = GetIconImage(icon);

        if (iconImage != null)
        {
            PictureBox pb = new()
            {
                Image = iconImage,
                SizeMode = PictureBoxSizeMode.StretchImage,
                Width = (int)(48 * scale),
                Height = (int)(48 * scale),
                Margin = new Padding(0, 0, (int)(12 * scale), 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AccessibleName = GetIconAccessibleName(icon),
                AccessibleRole = AccessibleRole.Graphic,
            };

            content.Controls.Add(pb, 0, 0);

            _iconPictureBox = pb;
        }
        else
        {
            // 無圖示時移除圖示欄位寬度。
            content.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 0);
        }

        // 訊息文字。
        Label lblText = new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size((int)(380 * scale), 0),
            Margin = new Padding(0, (int)(4 * scale), 0, (int)(4 * scale)),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Dock = DockStyle.Fill,
            AccessibleRole = AccessibleRole.StaticText,
        };
        content.Controls.Add(lblText, 1, 0);
        _lblText = lblText;

        outer.Controls.Add(content, 0, 0);

        // 按鈕列。
        FlowLayoutPanel buttonFlow = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding((int)(8 * scale), (int)(4 * scale), (int)(8 * scale), (int)(8 * scale)),
            Dock = DockStyle.Right,
        };
        _buttonFlow = buttonFlow;

        BuildButtons(buttons, defaultButton, buttonFlow, scale);

        outer.Controls.Add(buttonFlow, 0, 1);

        // 播報器（A11y）。
        _announcer = new AnnouncerLabel
        {
            AccessibleName = "\u200B",
            Dock = DockStyle.Bottom,
            Height = 1,
            TabStop = false,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
        };
        outer.Controls.Add(_announcer, 0, 2);

        Controls.Add(outer);

        // 對話框的 AccessibleDescription（播報 A／B 按鈕對應關係）。
        UpdateDialogAccessibleDescription();
    }

    /// <summary>
    /// 建立按鈕
    /// </summary>
    /// <param name="buttons">要建立的按鈕類型。</param>
    /// <param name="defaultButton">預設按鈕。</param>
    /// <param name="flow">按鈕容器。</param>
    /// <param name="scale">縮放比例。</param>
    private void BuildButtons(
        MessageBoxButtons buttons,
        MessageBoxDefaultButton defaultButton,
        FlowLayoutPanel flow,
        float scale)
    {
        // 決定按鈕清單（由右至左順序，因為 FlowDirection = RightToLeft）。
        List<(string label, DialogResult result, bool isPrimary, bool isCancel)> specs = GetButtonSpecs(buttons);
        List<Button> createdButtons = [];

        foreach ((string label, DialogResult result, bool isPrimary, bool isCancel) in specs)
        {
            Button btn = CreateEyeTrackerButton(label);

            btn.DialogResult = result;
            btn.Margin = new Padding((int)(4 * scale), 0, 0, 0);

            if (isPrimary)
            {
                _primaryButton = btn;
            }

            if (isCancel)
            {
                _cancelButton = btn;
                CancelButton = btn;
            }

            _allButtons.Add(btn);
            flow.Controls.Add(btn);
            createdButtons.Add(btn);
        }

        // 設定 AcceptButton 到預設按鈕。
        SetAcceptButton(createdButtons, defaultButton);

        // 若 AcceptButton 仍未設定，使用 primary button。
        if (AcceptButton == null &&
            _primaryButton != null)
        {
            AcceptButton = _primaryButton;
        }

        // 依最新的按鈕配置更新無障礙描述。
        UpdateDialogAccessibleDescription();
    }

    /// <summary>
    /// 建立支援眼動追蹤反饋的按鈕，
    /// 並設定適當的 AccessibleName 和 AccessibleDescription 以利螢幕閱讀器播報
    /// </summary>
    /// <param name="label">按鈕標籤文字。</param>
    /// <returns>Button</returns>
    private Button CreateEyeTrackerButton(string label)
    {
        Font font = MainForm.GetSharedA11yFont(DeviceDpi),
            boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, font.FontFamily);

        float scale = DeviceDpi / AppSettings.BaseDpi;

        string description = StripMnemonic(label) ?? label;

        Button btn = new()
        {
            Text = label,
            AccessibleName = description,
            AccessibleDescription = description,
            AccessibleRole = AccessibleRole.PushButton,
            Font = font,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
        };

        btn.FlatAppearance.BorderSize = 0;

        DialogLayoutHelper.UpdateButtonMinimumSize(btn, boldFont, scale, 88, 0, 24, 8);

        btn.AttachEyeTrackerFeedback(
            description,
            font,
            boldFont,
            _cts?.Token ?? CancellationToken.None);

        return btn;
    }

    /// <summary>
    /// 取得按鈕規格清單，包含按鈕標籤、對應的 DialogResult，以及是否為主要動作或取消動作。
    /// </summary>
    /// <param name="buttons">要建立的按鈕類型。</param>
    /// <returns>按鈕規格清單</returns>
    private static List<(string label, DialogResult result, bool isPrimary, bool isCancel)> GetButtonSpecs(
        MessageBoxButtons buttons)
    {
        // 回傳右→左順序（FlowDirection.RightToLeft 下最右邊的先加入顯示在右邊）。
        // 助記詞規則依目前的控制器配置模式動態調整，確保 PlayStation / Nintendo 顯示與 Xbox 行為模式同步。
        // profile 代表目前生效的 Face 鍵配置，供各種 MessageBox 按鈕共用。
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        return buttons switch
        {
            MessageBoxButtons.OK =>
            [
                (profile.FormatConfirmButtonText(Strings.Btn_OK), DialogResult.OK, true, false),
            ],
            MessageBoxButtons.OKCancel =>
            [
                (profile.FormatCancelButtonText(Strings.Btn_Cancel), DialogResult.Cancel, false, true),
                (profile.FormatConfirmButtonText(Strings.Btn_OK), DialogResult.OK, true, false),
            ],
            MessageBoxButtons.YesNo =>
            [
                (profile.FormatCancelButtonText(Strings.Btn_No), DialogResult.No, false, true),
                (profile.FormatConfirmButtonText(Strings.Btn_Yes), DialogResult.Yes, true, false),
            ],
            MessageBoxButtons.YesNoCancel =>
            [
                (profile.FormatCancelButtonText(Strings.Btn_Cancel), DialogResult.Cancel, false, true),
                (Strings.Btn_No, DialogResult.No, false, false),
                (profile.FormatConfirmButtonText(Strings.Btn_Yes), DialogResult.Yes, true, false),
            ],
            MessageBoxButtons.RetryCancel =>
            [
                (profile.FormatCancelButtonText(Strings.Btn_Cancel), DialogResult.Cancel, false, true),
                (profile.FormatConfirmButtonText(Strings.Btn_Retry), DialogResult.Retry, true, false),
            ],
            MessageBoxButtons.AbortRetryIgnore =>
            [
                (Strings.Btn_Ignore, DialogResult.Ignore, false, false),
                (profile.FormatConfirmButtonText(Strings.Btn_Retry), DialogResult.Retry, true, false),
                (profile.FormatCancelButtonText(Strings.Btn_Abort), DialogResult.Abort, false, true),
            ],
            _ =>
            [
                (profile.FormatConfirmButtonText(Strings.Btn_OK), DialogResult.OK, true, false),
            ],
        };
    }

    /// <summary>
    /// 設定 AcceptButton 到預設按鈕，
    /// 根據 MessageBoxDefaultButton 的值對應到視覺上第一個（最左）／第二個／第三個按鈕
    /// </summary>
    /// <param name="buttons">按鈕清單</param>
    /// <param name="defaultButton">預設按鈕</param>
    private void SetAcceptButton(List<Button> buttons, MessageBoxDefaultButton defaultButton)
    {
        // buttons 在 RightToLeft 流向中，index 0 = 最右邊的按鈕。
        // MessageBoxDefaultButton.Button1 對應視覺上第一個（最左）按鈕。
        // 反轉後取對應索引。
        List<Button> leftToRight = [.. buttons];

        leftToRight.Reverse();

        int idx = defaultButton switch
        {
            MessageBoxDefaultButton.Button1 => 0,
            MessageBoxDefaultButton.Button2 => 1,
            MessageBoxDefaultButton.Button3 => 2,
            _ => 0,
        };

        if (idx < leftToRight.Count)
        {
            AcceptButton = leftToRight[idx];
        }
    }

    /// <summary>
    /// 依目前按鈕配置建立控制器動作提示摘要，供無障礙描述使用。
    /// </summary>
    /// <returns>格式化後的控制器提示摘要。</returns>
    private string BuildActionHintSummary()
    {
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();
        string primaryText = profile.FormatPrimaryActionHintText(StripMnemonic(_primaryButton?.Text) ?? Strings.Btn_OK),
            cancelText = string.IsNullOrWhiteSpace(_cancelButton?.Text) ?
                string.Empty :
                profile.FormatCancelActionHintText(StripMnemonic(_cancelButton?.Text) ?? string.Empty);

        return !string.IsNullOrEmpty(cancelText) ?
            string.Format(Strings.GmBox_A11y_Hint, primaryText, cancelText) :
            primaryText;
    }

    /// <summary>
    /// 移除 GetMnemonicText 加入的助記詞後綴，例如「確定 (&amp;A)」→「確定」
    /// </summary>
    /// <param name="text">要處理的文字</param>
    /// <returns>移除助記詞後的文字</returns>
    private static string? StripMnemonic(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // 移除結尾的「 (&X)」或「(&X)」模式。
        int idx = text.LastIndexOf(" (&", StringComparison.Ordinal);

        if (idx < 0)
        {
            idx = text.LastIndexOf("(&", StringComparison.Ordinal);
        }

        return idx > 0 ? text[..idx] : text;
    }

    /// <summary>
    /// 更新對話框的 AccessibleDescription，包含圖示語義、基本描述與按鈕提示，讓螢幕閱讀器能播報完整的操作說明。
    /// </summary>
    private void UpdateDialogAccessibleDescription()
    {
        string? iconLabel = GetIconAccessibleName(_icon);
        string actionHintSummary = _gamepadController?.IsConnected == true ?
            BuildActionHintSummary() :
            string.Empty;
        string baseDesc = string.IsNullOrEmpty(actionHintSummary) ?
            Strings.GmBox_A11y_Dialog_Desc :
            $"{Strings.GmBox_A11y_Dialog_Desc} {actionHintSummary}";

        AccessibleDescription = iconLabel is null ?
            baseDesc :
            $"{iconLabel} {baseDesc}";
    }

    /// <summary>
    /// 取得對應 MessageBoxIcon 的無障礙標籤文字，供 AccessibleDescription 使用
    /// </summary>
    /// <param name="icon">MessageBoxIcon 類型</param>
    /// <returns>圖示的無障礙標籤，若無圖示則為 null</returns>
    private static string? GetIconAccessibleName(MessageBoxIcon icon)
    {
        return icon switch
        {
            MessageBoxIcon.Error => Strings.GmBox_A11y_Icon_Error,
            MessageBoxIcon.Warning => Strings.GmBox_A11y_Icon_Warning,
            MessageBoxIcon.Information => Strings.GmBox_A11y_Icon_Information,
            MessageBoxIcon.Question => Strings.GmBox_A11y_Icon_Question,
            _ => null,
        };
    }

    /// <summary>
    /// 取得對應 MessageBoxIcon 的系統圖示，並轉換為 Bitmap 以供 PictureBox 顯示
    /// </summary>
    /// <param name="icon">MessageBoxIcon 類型</param>
    /// <returns>對應的 Bitmap 圖示，若無則為 null</returns>
    private static Bitmap? GetIconImage(MessageBoxIcon icon)
    {
        return icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error.ToBitmap(),
            MessageBoxIcon.Warning => SystemIcons.Warning.ToBitmap(),
            MessageBoxIcon.Information => SystemIcons.Information.ToBitmap(),
            MessageBoxIcon.Question => SystemIcons.Question.ToBitmap(),
            MessageBoxIcon.None => null,
            _ => null,
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);

            // 套用初始字型
            ApplyFont();

            // 套用智慧定位（排在 Handle 建立後）
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadMessageBox] OnHandleCreated 延遲位置修正失敗：{ex.Message}");
                }
            });

            // 訂閱系統主題變更事件
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "GamepadMessageBox.OnHandleCreated 失敗");
            Debug.WriteLine($"[GamepadMessageBox] OnHandleCreated 失敗：{ex.Message}");
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // 明確激活視窗，確保對話框浮在 MainForm 上方，且焦點正確移入。
        Activate();

        // 修正 AutoSize 展開後的智慧定位。
        ApplySmartPosition();

        // 讓預設按鈕取得焦點，確保螢幕閱讀器正確播報。
        IButtonControl? accept = AcceptButton;
        if (accept is Button btn && btn.CanFocus && !btn.Focused)
        {
            btn.Focus();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);

            this.SafeInvoke(() =>
            {
                try
                {
                    ApplyFont();
                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadMessageBox] OnDpiChanged 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadMessageBox] OnDpiChanged 失敗：{ex.Message}");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (e.Cancel)
        {
            return;
        }

        UnsubscribeGamepadEvents();

        Interlocked.Exchange(ref _announcer, null)?.Dispose();

        Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();

        // 共享字體不由此對話框處置，僅歸零欄位引用。
        _ = Interlocked.Exchange(ref _currentFont, null);

        _allButtons.Clear();

        // 清除版面配置欄位引用（控制項由 Form 負責 Dispose）。
        _iconPictureBox = null;
        _lblText = null;
        _contentPanel = null;
        _buttonFlow = null;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            // 確保靜態事件在視窗控制代碼销毀時被絕對釋放。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    /// <summary>
    /// 套用字型並更新相關控制項的字型與 DPI 相關尺寸
    /// </summary>
    private void ApplyFont()
    {
        if (!DialogLayoutHelper.TryBeginDpiLayout(DeviceDpi, ref _lastAppliedDpi))
        {
            return;
        }

        Font shared = MainForm.GetSharedA11yFont(DeviceDpi);

        _currentFont = shared;

        Font = shared;

        // 同步按鈕字型與抗抖動最小尺寸。
        float scale = DeviceDpi / AppSettings.BaseDpi;

        Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, shared.FontFamily);

        foreach (Button btn in _allButtons)
        {
            if (btn.IsDisposed)
            {
                continue;
            }

            btn.Font = shared;

            DialogLayoutHelper.UpdateButtonMinimumSize(btn, boldFont, scale, 88, 0, 24, 8);
        }

        // 同步更新所有依賴 DPI 的排版尺寸。
        UpdateScaledLayout();
    }

    /// <summary>
    /// 依目前 <see cref="Control.DeviceDpi"/> 重新套用所有硬編碼的排版像素值。
    /// 必須在 <see cref="ApplyFont"/> 之後呼叫，以確保 DPI 防抖已通過。
    /// </summary>
    private void UpdateScaledLayout()
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        MinimumSize = new Size((int)(320 * scale), 0);

        if (_contentPanel is { IsDisposed: false })
        {
            _contentPanel.Padding = new Padding(
                (int)(12 * scale), (int)(12 * scale), (int)(12 * scale), (int)(6 * scale));
        }

        if (_iconPictureBox is { IsDisposed: false })
        {
            _iconPictureBox.Width = (int)(48 * scale);
            _iconPictureBox.Height = (int)(48 * scale);
            _iconPictureBox.Margin = new Padding(0, 0, (int)(12 * scale), 0);
        }

        if (_lblText is { IsDisposed: false })
        {
            _lblText.MaximumSize = new Size((int)(380 * scale), 0);
            _lblText.Margin = new Padding(0, (int)(4 * scale), 0, (int)(4 * scale));
        }

        if (_buttonFlow is { IsDisposed: false })
        {
            _buttonFlow.Margin = new Padding(
                (int)(8 * scale), (int)(4 * scale), (int)(8 * scale), (int)(8 * scale));
        }

        foreach (Button btn in _allButtons)
        {
            if (!btn.IsDisposed)
            {
                btn.Margin = new Padding((int)(4 * scale), 0, 0, 0);
            }
        }
    }

    /// <summary>
    /// 套用智慧定位，將視窗位置限制在可見區域內，避免在多螢幕或 DPI 變更後出現部分或全部在螢幕外的情況
    /// </summary>
    private void ApplySmartPosition()
    {
        if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
        {
            Location = clampedLocation;
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            if (e.Category == UserPreferenceCategory.Accessibility ||
                e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    try
                    {
                        ApplyFont();
                        Invalidate(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GamepadMessageBox] SystemEvents_UserPreferenceChanged 失敗：{ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadMessageBox] SystemEvents_UserPreferenceChanged 失敗：{ex.Message}");
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        // 主視窗在 Deactivate 時會 Pause 控制器；此處重新 Resume，讓對話框能接收控制器輸入。
        // 仿照 NumericInputDialog：等待 50 ms 後再 Resume，避免與 Deactivate 的 Pause 競爭。
        Task.Run(async () =>
        {
            try
            {
                CancellationToken token = _cts?.Token ?? CancellationToken.None;

                token.ThrowIfCancellationRequested();

                await Task.Delay(50, token);

                await this.SafeInvokeAsync(() =>
                {
                    try
                    {
                        _gamepadController?.Resume();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GamepadMessageBox] Resume 失敗：{ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamepadMessageBox] OnActivated 失敗：{ex.Message}");
            }
        },
        _cts?.Token ?? CancellationToken.None)
        .SafeFireAndForget();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        // 視窗失去焦點時立即暫停控制器，防止控制器輸入穿透到背景視窗。
        this.SafeBeginInvoke(() =>
        {
            try
            {
                _gamepadController?.Pause();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamepadMessageBox] Pause 失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 處理遊戲控制器 A 鍵事件，觸發目前焦點按鈕或主按鈕的 Click 事件，並播報相關訊息給螢幕閱讀器
    /// </summary>
    private void HandleGamepadA()
    {
        this.SafeInvoke(() =>
        {
            try
            {
                // 若焦點在某個按鈕上，直接觸發該按鈕；否則觸發主按鈕。
                Button? focused = _allButtons.FirstOrDefault(b => b.Focused && b.Enabled);

                (focused ?? _primaryButton ?? AcceptButton as Button)?.PerformClick();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamepadMessageBox] HandleGamepadA 失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 處理遊戲控制器 B 鍵事件，觸發取消按鈕的 Click 事件（若有），或主按鈕的 Click 事件（無取消按鈕時），並播報相關訊息給螢幕閱讀器
    /// </summary>
    private void HandleGamepadB()
    {
        this.SafeInvoke(() =>
        {
            try
            {
                if (_cancelButton != null)
                {
                    _cancelButton.PerformClick();
                }
                else
                {
                    // 無取消按鈕時（如純 OK），A／B 都觸發主按鈕。
                    (_primaryButton ?? AcceptButton as Button)?.PerformClick();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamepadMessageBox] HandleGamepadB 失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 處理遊戲控制器 DPad 左鍵事件，將焦點移動到上一個可用按鈕，並播報新焦點按鈕名稱給螢幕閱讀器
    /// </summary>
    private void HandleDPadLeft() => CycleFocus(forward: false);

    /// <summary>
    /// 處理遊戲控制器 DPad 右鍵事件，將焦點移動到下一個可用按鈕，並播報新焦點按鈕名稱給螢幕閱讀器
    /// </summary>
    private void HandleDPadRight() => CycleFocus(forward: true);

    /// <summary>
    /// 循環切換按鈕焦點，根據 forward 參數決定方向，並確保只在啟用且可見的按鈕之間切換
    /// </summary>
    /// <param name="forward">是否向前切換焦點。</param>
    private void CycleFocus(bool forward)
    {
        this.SafeInvoke(() =>
        {
            try
            {
                List<Button> available = [.. _allButtons.Where(b => b.Enabled && b.Visible)];

                if (available.Count <= 1)
                {
                    return;
                }

                int currentIdx = available.FindIndex(b => b.Focused);

                int nextIdx = currentIdx < 0 ?
                    (forward ? 0 : available.Count - 1) :
                    forward ?
                        (currentIdx + 1) % available.Count :
                        (currentIdx - 1 + available.Count) % available.Count;

                available[nextIdx].Focus();

                // 播報新焦點按鈕名稱。
                string? name = available[nextIdx].AccessibleName ??
                    StripMnemonic(available[nextIdx].Text);

                if (!string.IsNullOrEmpty(name))
                {
                    AnnounceA11y(name);
                }

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamepadMessageBox] CycleFocus 失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 處理遊戲控制器連線狀態變更事件，更新提示標籤顯示並播報連線狀態給螢幕閱讀器
    /// </summary>
    /// <param name="connected">遊戲控制器是否已連線。</param>
    private void HandleGamepadConnectionChanged(bool connected)
    {
        try
        {
            if (connected)
            {
                _gamepadController?.Resume();
            }

            this.SafeInvoke(() =>
            {
                try
                {
                    // 連線狀態改變時同步更新可存取描述。
                    UpdateDialogAccessibleDescription();

                    // 播報連線狀態。
                    AnnounceA11y(connected ?
                        string.Format(Strings.A11y_Gamepad_Connected, _gamepadController?.DeviceName) :
                        string.Format(Strings.A11y_Gamepad_Disconnected, _gamepadController?.DeviceName));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadMessageBox] HandleGamepadConnectionChanged UI 更新失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadMessageBox] HandleGamepadConnectionChanged 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 播報無障礙訊息，通常用於按鈕焦點變更或控制器連線狀態變更時，讓螢幕閱讀器能即時讀出相關資訊
    /// </summary>
    /// <param name="message">訊息</param>
    private void AnnounceA11y(string message)
    {
        _announcer?.Announce(message);
    }

    /// <summary>
    /// 顯示支援控制器操作的訊息對話框，並回傳使用者選擇的結果
    /// </summary>
    /// <param name="owner">對話框的父視窗（可為 null）。</param>
    /// <param name="text">訊息文字。</param>
    /// <param name="caption">標題列文字。</param>
    /// <param name="buttons">顯示的按鈕組合。</param>
    /// <param name="icon">顯示的圖示。</param>
    /// <param name="defaultButton">預設焦點按鈕。</param>
    /// <param name="gamepad">遊戲控制器（可為 null，此時僅支援鍵盤操作）。</param>
    /// <returns>使用者的選擇結果。</returns>
    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1,
        IGamepadController? gamepad = null)
    {
        using var dlg = new GamepadMessageBox(text, caption, buttons, icon, defaultButton);

        dlg.GamepadController = gamepad;

        // 若父視窗設有 TopMost，對話框也必須是 TopMost，否則會被壓在下方。
        // 原生 MessageBoxW 由 Windows 內部處理 z-order，自訂 Form 需手動同步。
        Form? ownerForm = owner is Form f ? f : FromHandle(owner?.Handle ?? IntPtr.Zero) as Form;

        // owner 為 null 時（例如 AppSettings、Program 的靜態呼叫），
        // 退回至 OpenForms 中第一個 TopMost 視窗，確保對話框不被遮蓋。
        ownerForm ??= Application.OpenForms
            .OfType<Form>()
            .FirstOrDefault(fw => fw.TopMost && fw.IsHandleCreated && !fw.IsDisposed);

        if (ownerForm?.TopMost == true)
        {
            dlg.TopMost = true;
        }

        return owner != null ?
            dlg.ShowDialog(owner) :
            dlg.ShowDialog();
    }

    /// <summary>
    /// 顯示支援控制器操作的訊息對話框（無父視窗）
    /// </summary>
    /// <param name="text"></param>
    /// <param name="caption"></param>
    /// <param name="buttons"></param>
    /// <param name="icon"></param>
    /// <param name="defaultButton"></param>
    /// <param name="gamepad"></param>
    /// <returns>DialogResult</returns>
    public static DialogResult Show(
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1,
        IGamepadController? gamepad = null)
        => Show(null, text, caption, buttons, icon, defaultButton, gamepad);
}