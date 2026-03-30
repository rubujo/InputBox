using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms.Automation;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 專門用於數值輸入的對話框
/// </summary>
internal sealed class NumericInputDialog : Form
{
    /// <summary>
    /// 繼承自 NumericUpDown 以公開受保護的成員方法
    /// </summary>
    /// <param name="parent">父對話框實例</param>
    private sealed class AccessibleNumericUpDown(NumericInputDialog parent) : NumericUpDown
    {
        /// <summary>
        /// NumericInputDialog
        /// </summary>
        private readonly NumericInputDialog _parent = parent;

        public override void UpButton()
        {
            try
            {
                decimal oldValue = Value;

                base.UpButton();

                if (Value == oldValue)
                {
                    _parent.HandleBoundaryHit(true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] UpButton 失敗：{ex.Message}");
            }
        }

        public override void DownButton()
        {
            try
            {
                decimal oldValue = Value;

                base.DownButton();

                if (Value == oldValue)
                {
                    _parent.HandleBoundaryHit(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] DownButton 失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 主動觸發無障礙狀態變更通知
        /// </summary>
        public void NotifyAccessibilityChange()
        {
            try
            {
                // 主動通知輔助科技（AT）數值已變更。
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] NotifyAccessibilityChange 失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 強制驗證編輯文字，確保 Value 屬性與目前輸入內容同步
        /// </summary>
        public void ValidateValue()
        {
            try
            {
                ValidateEditText();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] ValidateValue 失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 覆寫滑鼠滾輪行為，確保「一格跳一格」
        /// </summary>
        /// <param name="e">MouseEventArgs</param>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            try
            {
                // 強制攔截並阻斷 Windows 系統的「一次捲動多行」設定（預設為 3）。
                if (e is HandledMouseEventArgs hme)
                {
                    hme.Handled = true;
                }

                // 手動精確執行單次增減，不調用 base.OnMouseWheel(e)。
                if (e.Delta > 0)
                {
                    UpButton();
                }
                else if (e.Delta < 0)
                {
                    DownButton();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericUpDown] OnMouseWheel 失敗：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 預設數值
    /// </summary>
    private readonly decimal _defaultValue;

    /// <summary>
    /// AccessibleNumericUpDown 實例
    /// </summary>
    private AccessibleNumericUpDown? _nud;

    /// <summary>
    /// 3x2 網格連動區容器
    /// </summary>
    private TableLayoutPanel? _tlpGrid;

    /// <summary>
    /// 確定按鈕
    /// </summary>
    private Button? _btnOk;

    /// <summary>
    /// 取消按鈕
    /// </summary>
    private Button? _btnCancel;

    /// <summary>
    /// 增加按鈕
    /// </summary>
    private Button? _btnPlus;

    /// <summary>
    /// 減少按鈕
    /// </summary>
    private Button? _btnMinus;

    /// <summary>
    /// 重設按鈕
    /// </summary>
    private Button? _btnReset;

    /// <summary>
    /// 用於 A11y 廣播的 Label
    /// </summary>
    private AnnouncerLabel? _announcer;

    /// <summary>
    /// 統一放大的 A11y 字型
    /// </summary>
    private Font? _a11yFont;

    /// <summary>
    /// NUD 專屬放大字型（來自共享快取，絕對禁止在此處手動處置）
    /// </summary>
    private Font? _nudFont;

    /// <summary>
    /// 用於管理對話框生命週期內非同步任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// 右搖桿虛擬選取的起點錨點
    /// </summary>
    private int? _rsSelectionAnchor = null;

    /// <summary>
    /// A11y 廣播防抖用的序號
    /// </summary>
    private long _a11yDebounceId = 0;

    /// <summary>
    /// 上一次建立的游標寬度
    /// </summary>
    private int _lastCaretWidth = -1;

    /// <summary>
    /// 上一次建立的游標高度
    /// </summary>
    private int _lastCaretHeight = -1;

    /// <summary>
    /// 用於管理警示動畫的中斷控制
    /// </summary>
    private CancellationTokenSource? _alertCts;

    /// <summary>
    /// 是否正在閃爍（用於防止重複觸發閃爍效果）
    /// </summary>
    private volatile int _isFlashing = 0;

    /// <summary>
    /// Gamepad 控制介面
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 取得目前數值
    /// </summary>
    public decimal Value => _nud?.Value ?? 0m;

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
            // 如果控制器相同，不執行任何操作。
            if (ReferenceEquals(_gamepadController, value))
            {
                return;
            }

            // 安全清理舊控制器的訂閱。
            UnsubscribeGamepadEvents();

            _gamepadController = value;

            if (_gamepadController != null)
            {
                // 訂閱新控制器事件。
                _gamepadController.UpPressed += HandlePlus;
                _gamepadController.UpRepeat += HandlePlus;
                _gamepadController.DownPressed += HandleMinus;
                _gamepadController.DownRepeat += HandleMinus;
                _gamepadController.LeftPressed += HandleLeft;
                _gamepadController.LeftRepeat += HandleLeft;
                _gamepadController.RightPressed += HandleRight;
                _gamepadController.RightRepeat += HandleRight;
                _gamepadController.RSLeftPressed += HandleRSLeft;
                _gamepadController.RSLeftRepeat += HandleRSLeft;
                _gamepadController.RSRightPressed += HandleRSRight;
                _gamepadController.RSRightRepeat += HandleRSRight;
                _gamepadController.APressed += HandleGamepadA;
                _gamepadController.StartPressed += HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed += HandleCancel;
                _gamepadController.BackPressed += HandleCancel;
                _gamepadController.XPressed += HandleBackspace;
                _gamepadController.YPressed += HandleReset;
                // 已由 D‑Pad (`LeftPressed` / `RightPressed`) 處理游標移動，移除 LT/RT 綁定以避免語意重複。
                _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
            }
        }
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        try
        {
            base.OnResizeEnd(e);

            // 拖曳結束時執行智慧定位修正。
            ApplySmartPosition();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] OnResizeEnd 失敗：{ex.Message}");
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);

            UpdateMinimumSize();

            // 使用 SafeBeginInvoke 讓字型替換邏輯排在 Handle 建立完成「之後」才執行。
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    // 套用透明度。
                    UpdateOpacity();

                    // 執行初始位置檢查。
                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] OnHandleCreated 延遲邏輯失敗：{ex.Message}");
                }
            });

            // 先解除再訂閱靜態事件，防止 Handle 重建時產生重複訂閱。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] OnHandleCreated 失敗：{ex.Message}");
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);

            // 當 DPI 變更時，強制全視窗重新讀取全域共享字體。
            this.SafeInvoke(() =>
            {
                try
                {
                    // 重新取得 A11y 字型實例（從快取池取得）。
                    _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi);

                    // 同步更新所有按鈕的基礎字體。
                    if (_a11yFont != null)
                    {
                        _btnOk!.Font = _a11yFont;
                        _btnCancel!.Font = _a11yFont;
                        _btnPlus!.Font = _a11yFont;
                        _btnMinus!.Font = _a11yFont;
                        _btnReset!.Font = _a11yFont;
                    }

                    // 數值顯示區字體需重新依據新縮放比例建立。
                    if (_a11yFont != null &&
                        _nud != null)
                    {
                        // 2.0x 放大字體（來自共享快取，不需手動回收）。
                        _nudFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, _a11yFont.FontFamily, 2.0f);

                        _nud.Font = _nudFont;
                    }

                    // 更新佈局約束。
                    UpdateMinimumSize();

                    // 強制所有控制項重新套用最新主題與字體。
                    UpdateFocusVisuals(_nud?.Focused == true || (_nud?.ContainsFocus == true));

                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] OnDpiChanged 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] OnDpiChanged 失敗：{ex.Message}");
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
                        UpdateMinimumSize();

                        UpdateFocusVisuals(_nud?.Focused == true || (_nud?.ContainsFocus == true));
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[NumericInputDialog] SystemEvents 更新失敗");

                        Debug.WriteLine($"[NumericInputDialog] SystemEvents 更新失敗：{ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "[NumericInputDialog] SystemEvents 處理失敗");

            Debug.WriteLine($"[NumericInputDialog] SystemEvents 處理失敗：{ex.Message}");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            // 確保靜態事件在視窗控制項控制代碼銷毀時被絕對釋放。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                // 處置取消權杖來源（主要任務與 Flash Alert 各自獨立處置）。
                Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
                Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

                // 原子化處置 UI 控制項與資源。
                Interlocked.Exchange(ref _nud, null)?.Dispose();
                Interlocked.Exchange(ref _tlpGrid, null)?.Dispose();
                Interlocked.Exchange(ref _btnOk, null)?.Dispose();
                Interlocked.Exchange(ref _btnCancel, null)?.Dispose();
                Interlocked.Exchange(ref _btnPlus, null)?.Dispose();
                Interlocked.Exchange(ref _btnMinus, null)?.Dispose();
                Interlocked.Exchange(ref _btnReset, null)?.Dispose();

                // 共享資源僅歸零，由 Program.cs 統一釋放。
                Interlocked.Exchange(ref _nudFont, null);
                Interlocked.Exchange(ref _a11yFont, null);

                Interlocked.Exchange(ref _announcer, null)?.Dispose();

                // 解除控制器事件訂閱，防止記憶體洩漏（_gamepadController 生命週期由外部管理）。
                UnsubscribeGamepadEvents();

                // 確保靜態事件在視窗處置時被絕對釋放，防止 Handle 未建立時的洩漏。
                SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 取消訂閱控制器事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.UpPressed -= HandlePlus;
                _gamepadController.UpRepeat -= HandlePlus;
                _gamepadController.DownPressed -= HandleMinus;
                _gamepadController.DownRepeat -= HandleMinus;
                _gamepadController.LeftPressed -= HandleLeft;
                _gamepadController.LeftRepeat -= HandleLeft;
                _gamepadController.RightPressed -= HandleRight;
                _gamepadController.RightRepeat -= HandleRight;
                _gamepadController.RSLeftPressed -= HandleRSLeft;
                _gamepadController.RSLeftRepeat -= HandleRSLeft;
                _gamepadController.RSRightPressed -= HandleRSRight;
                _gamepadController.RSRightRepeat -= HandleRSRight;
                _gamepadController.APressed -= HandleGamepadA;
                _gamepadController.StartPressed -= HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed -= HandleCancel;
                _gamepadController.BackPressed -= HandleCancel;
                _gamepadController.XPressed -= HandleBackspace;
                _gamepadController.YPressed -= HandleReset;
                // LT/RT 綁定已移除（由 D‑Pad 處理游標移動），無需解除訂閱。
                _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] 取消訂閱控制器事件失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理控制器連線狀態變更
    /// </summary>
    /// <param name="connected">是否已連線</param>
    private void HandleGamepadConnectionChanged(bool connected)
    {
        try
        {
            if (connected)
            {
                _gamepadController?.Resume();
            }

            // 告知使用者控制器連線狀態變更。
            AnnounceA11y(connected ?
                string.Format(Strings.A11y_Gamepad_Connected, _gamepadController?.DeviceName) :
                string.Format(Strings.A11y_Gamepad_Disconnected, _gamepadController?.DeviceName));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] 控制器連線變更處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理邊界撞擊效果
    /// </summary>
    /// <param name="isUpperLimit">是否為上限</param>
    internal void HandleBoundaryHit(bool isUpperLimit)
    {
        this.SafeInvoke(() =>
        {
            try
            {
                // 利用 _isFlashing 作為防呆機制，避免控制器長按連發時造成音效與語音播報卡頓。
                if (_isFlashing == 0)
                {
                    FeedbackService.PlaySound(SystemSounds.Beep);

                    FeedbackService.VibrateAsync(
                            _gamepadController,
                            VibrationPatterns.ActionFail,
                            _cts?.Token ?? CancellationToken.None)
                        .SafeFireAndForget();

                    AnnounceA11y(isUpperLimit ?
                        Strings.A11y_Value_Max :
                        Strings.A11y_Value_Min, true);

                    FlashAlertAsync().SafeFireAndForget();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] HandleBoundaryHit 失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 處理數值增加，並保留焦點以支援眼動儀連發與鍵盤連點
    /// </summary>
    private void HandlePlus() => this.SafeInvoke(() =>
    {
        try
        {
            _nud?.UpButton();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandlePlus 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理數值減少，並保留焦點以支援眼動儀連發與鍵盤連點
    /// </summary>
    private void HandleMinus() => this.SafeInvoke(() =>
    {
        try
        {
            _nud?.DownButton();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleMinus 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理游標移動
    /// </summary>
    /// <param name="forward">是否向右（前進）移動</param>
    private void MoveCaret(bool forward) => this.SafeInvoke(() =>
    {
        try
        {
            if (_nud == null)
            {
                return;
            }

            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (textBox == null ||
                textBox.IsDisposed)
            {
                return;
            }

            bool hasSelection = textBox.SelectionLength > 0;

            bool canMove = forward ?
                (hasSelection || textBox.SelectionStart < textBox.TextLength) :
                (hasSelection || textBox.SelectionStart > 0);

            if (canMove)
            {
                if (hasSelection)
                {
                    if (forward)
                    {
                        textBox.SelectionStart += textBox.SelectionLength;
                    }

                    textBox.SelectionLength = 0;
                }
                // 組合鍵：LB + 方向鍵 執行單字跳轉。
                else if (_gamepadController?.IsLeftShoulderHeld == true)
                {
                    textBox.WordJump(forward);
                }
                else
                {
                    if (forward)
                    {
                        textBox.SelectionStart++;
                    }
                    else
                    {
                        textBox.SelectionStart--;
                    }
                }

                textBox.ScrollToCaret();

                // 取得目前游標位置（1-based 報讀）。
                int pos = textBox.SelectionStart;

                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.A11y_Cursor_Move_PrivacySafe :
                    string.Format(Strings.A11y_Cursor_Move, pos + 1), true);

                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.CursorMove,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
            else
            {
                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ActionFail,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] MoveCaret 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理游標左移
    /// </summary>
    private void HandleLeft() => MoveCaret(false);

    /// <summary>
    /// 處理游標右移
    /// </summary>
    private void HandleRight() => MoveCaret(true);

    /// <summary>
    /// 處理選取範圍擴張
    /// </summary>
    /// <param name="forward">是否向右擴張</param>
    private void ExpandSelection(bool forward) => this.SafeInvoke(() =>
    {
        try
        {
            if (_nud == null)
            {
                return;
            }

            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (textBox == null ||
                textBox.IsDisposed)
            {
                return;
            }

            // 當目前沒有選取範圍，或是目前的選取範圍與我們的錨點不匹配時，重新設定錨點。
            if (textBox.SelectionLength == 0 ||
                _rsSelectionAnchor == null ||
                (textBox.SelectionStart != _rsSelectionAnchor.Value &&
                 textBox.SelectionStart + textBox.SelectionLength != _rsSelectionAnchor.Value))
            {
                _rsSelectionAnchor = textBox.SelectionStart;
            }

            int anchor = _rsSelectionAnchor.Value;

            // 推算活動邊緣。
            int caret = (textBox.SelectionStart == anchor) ?
                (anchor + textBox.SelectionLength) :
                textBox.SelectionStart;

            int direction = forward ? 1 : -1;

            int newCaret = Math.Clamp(caret + direction, 0, textBox.TextLength);

            if (newCaret == caret)
            {
                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ActionFail,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                return;
            }

            // 使用 Win32 EM_SETSEL 設定選取範圍。
            User32.SendMessage(textBox.Handle, (uint)User32.WindowMessage.EM_SETSEL, anchor, newCaret);

            if (textBox.SelectionLength > 0)
            {
                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.A11y_Selected_Text_PrivacySafe :
                    string.Format(Strings.A11y_Selected_Text, textBox.SelectedText), true);
            }

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] ExpandSelection 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理選取左向擴張
    /// </summary>
    private void HandleRSLeft() => ExpandSelection(false);

    /// <summary>
    /// 處理選取右向擴張
    /// </summary>
    private void HandleRSRight() => ExpandSelection(true);

    /// <summary>
    /// 處理刪除按鍵（Backspace 與 Delete）的共用邏輯
    /// </summary>
    /// <param name="textBox">目標 TextBox</param>
    /// <param name="isBackspace">是否為 Backspace 鍵</param>
    private void HandleDeleteKey(TextBox textBox, bool isBackspace)
    {
        try
        {
            // 擷取刪除前的狀態。
            string oldText = textBox.Text;

            int oldStart = textBox.SelectionStart,
                oldLen = textBox.SelectionLength;

            // 檢查是否可以刪除。
            bool cannotDelete = isBackspace ?
                (oldLen == 0 && oldStart == 0) :
                (oldLen == 0 && oldStart == oldText.Length);

            if (cannotDelete)
            {
                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ActionFail,
                        _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                if (isBackspace)
                {
                    AnnounceA11y(Strings.A11y_Cannot_Delete, true);
                }

                return;
            }

            this.SafeBeginInvoke(() =>
            {
                try
                {
                    if (textBox.IsDisposed)
                    {
                        return;
                    }

                    // 比對內容是否真的減少。
                    if (textBox.TextLength < oldText.Length)
                    {
                        if (oldLen > 0)
                        {
                            AnnounceA11y(string.Format(Strings.A11y_Delete_Multiple, oldLen), true);
                        }
                        else
                        {
                            int deleteIndex = isBackspace ?
                                oldStart - 1 :
                                oldStart;

                            if (deleteIndex >= 0 &&
                                deleteIndex < oldText.Length)
                            {
                                if (AppSettings.Current.IsPrivacyMode)
                                {
                                    AnnounceA11y(Strings.A11y_Delete_Char_PrivacySafe, true);
                                }
                                else
                                {
                                    char deletedChar = oldText[deleteIndex];

                                    AnnounceA11y(string.Format(Strings.A11y_Delete_Char, deletedChar), true);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] HandleDeleteKey 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleDeleteKey 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 處理確認按鍵事件
    /// </summary>
    private void HandleConfirm() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            _nud?.ValidateValue();

            // 發送與控制器對等的震動與 A11y 播報。
            FeedbackService.PlaySound(SystemSounds.Asterisk);

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CopySuccess,
                    _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            AnnounceA11y(Strings.A11y_Returning);

            DialogResult = DialogResult.OK;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleConfirm 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理取消按鍵事件
    /// </summary>
    private void HandleCancel() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            // 發送與控制器對等的震動（比照返回動作）。
            FeedbackService.PlaySound(SystemSounds.Exclamation);

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.ReturnStart,
                    _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            // A11y 播報在 FormClosing 中已處理。

            DialogResult = DialogResult.Cancel;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleCancel 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理重設按鍵事件
    /// </summary>
    private void HandleReset() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                !IsHandleCreated ||
                _nud == null)
            {
                return;
            }

            _nud.Value = Math.Clamp(_defaultValue, _nud.Minimum, _nud.Maximum);
            _nud.Focus();

            FeedbackService.PlaySound(SystemSounds.Asterisk);

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleReset 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// A 鍵：按鈕 → PerformClick；輸入框為空時開啟觸控鍵盤；其餘情境走確認驗證。
    /// </summary>
    private void HandleGamepadA() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveControl is Button btn)
            {
                btn.PerformClick();
                return;
            }

            if (_nud != null && (_nud.Focused || _nud.ContainsFocus))
            {
                TextBox? tb = _nud.Controls.OfType<TextBox>().FirstOrDefault();
                if (tb != null && string.IsNullOrWhiteSpace(tb.Text))
                {
                    ShowTouchKeyboard(tb);
                    return;
                }
            }

            HandleConfirm();
        }
        catch (Exception ex) { Debug.WriteLine($"[NumericInputDialog] HandleGamepadA 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// Start 鍵：在輸入框焦點時直接開啟觸控式鍵盤
    /// </summary>
    private void HandleOpenTouchKeyboardFromGamepad() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = _nud?.Controls.OfType<TextBox>().FirstOrDefault();
            if (tb != null)
            {
                ShowTouchKeyboard(tb);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[NumericInputDialog] HandleOpenTouchKeyboardFromGamepad 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// 開啟觸控式鍵盤
    /// </summary>
    private void ShowTouchKeyboard(TextBox tb)
    {
        if (tb.CanFocus &&
            !tb.Focused)
        {
            tb.Focus();
        }

        AnnounceA11y(Strings.A11y_Opening_Keyboard, interrupt: true);

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, _cts?.Token ?? CancellationToken.None);

                if (TouchKeyboardService.IsVisible()) return;

                await this.SafeInvokeAsync(() =>
                {
                    try
                    {
                        bool opened = TouchKeyboardService.TryOpen();

                        if (opened)
                        {
                            FeedbackService.PlaySound(SystemSounds.Asterisk);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NumericInputDialog] ShowTouchKeyboard 內層失敗: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] ShowTouchKeyboard 背景工作失敗: {ex.Message}");
            }
        }, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
    }

    /// <summary>
    /// X 鍵：刪除選取文字或游標前一字元
    /// </summary>
    private void HandleBackspace() => this.SafeInvoke(() =>
    {
        try
        {
            if (_nud == null)
            {
                return;
            }

            TextBox? tb = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (tb == null ||
                tb.IsDisposed ||
                tb.ReadOnly)
            {
                return;
            }

            // 實施手動字串處理以符合「不模擬按鍵」的安全性紅線。
            // 透過直接操作 TextBox.Text，能觸發 NumericUpDown 的內部驗證機制且不依賴 Win32 訊息注入。
            if (tb.SelectionLength > 0)
            {
                tb.SelectedText = string.Empty;
            }
            else if (tb.SelectionStart > 0)
            {
                int start = tb.SelectionStart;

                tb.Text = tb.Text.Remove(start - 1, 1);
                tb.SelectionStart = start - 1;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleBackspace 失敗: {ex.Message}");
        }
    });

    /// <summary>
    /// LT 鍵：向左導覽游標
    /// </summary>
    private void HandleLTNav() => MoveCaret(false);

    /// <summary>
    /// RT 鍵：向右導覽游標
    /// </summary>
    private void HandleRTNav() => MoveCaret(true);

    /// <summary>
    /// 執行視覺警示閃爍效果
    /// </summary>
    /// <returns>Task</returns>
    private async Task FlashAlertAsync()
    {
        if (IsDisposed ||
            !IsHandleCreated ||
            Interlocked.CompareExchange(ref _isFlashing, 1, 0) != 0)
        {
            return;
        }

        // 先建立新 CTS 的本地引用，再原子交換出舊實例，最後從本地引用取 Token。
        // 此模式防止「Exchange 後、Token 取用前」另一執行緒將欄位置 null 引發 NullReferenceException。
        CancellationTokenSource newAlertCts = CancellationTokenSource
            .CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);

        Interlocked.Exchange(ref _alertCts, newAlertCts)?.CancelAndDispose();

        CancellationToken token = newAlertCts.Token;

        try
        {
            bool isDark = this.IsDarkModeActive();

            // 決定警示色。
            Color alertColor = SystemInformation.HighContrast ?
                SystemColors.Highlight :
                (isDark ? Color.Firebrick : Color.DarkOrange);

            void ApplyAlertVisuals(float intensity)
            {
                if (IsDisposed ||
                    !IsHandleCreated ||
                    _nud == null)
                {
                    return;
                }

                if (SystemInformation.HighContrast)
                {
                    bool isAlert = intensity > 0.5f;

                    Color hcBack = isAlert ?
                            alertColor :
                            SystemColors.Window,
                        hcFore = isAlert ?
                            SystemColors.HighlightText :
                            SystemColors.WindowText;

                    _nud.UpdateRecursive(hcBack, hcFore);
                }
                else
                {
                    Color pureBase = isDark ?
                        Color.White :
                        Color.Black;

                    int rN = (int)(pureBase.R + (alertColor.R - pureBase.R) * intensity),
                        gN = (int)(pureBase.G + (alertColor.G - pureBase.G) * intensity),
                        bN = (int)(pureBase.B + (alertColor.B - pureBase.B) * intensity);

                    Color flashColor = Color.FromArgb(255, rN, gN, bN);
                    // WCAG 相對亮度精確切換閾值（crossover L≈0.1791），修復 YUV≈128 近似在切換帶（intensity≈0.75）
                    // 導致文字對比跌破 AA（3.5~4.2:1）的問題。修復後全程 ≥4.64:1 AA；
                    // 14f bold 大型文字全程 ≥4.5:1 AAA。
                    static float FLin(int c) { float f = c / 255f; return f <= 0.04045f ? f / 12.92f : MathF.Pow((f + 0.055f) / 1.055f, 2.4f); }
                    Color flashFore = (0.2126f * FLin(flashColor.R) + 0.7152f * FLin(flashColor.G) + 0.0722f * FLin(flashColor.B)) > 0.1791f
                        ? Color.Black
                        : Color.White;

                    _nud.UpdateRecursive(flashColor, flashFore);
                }
            }

            if (!SystemInformation.UIEffectsEnabled)
            {
                await this.SafeInvokeAsync(() => ApplyAlertVisuals(1.0f));

                await Task.Delay(800, token);

                return;
            }

            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(AppSettings.TargetFrameTimeMs));

            long startTime = Stopwatch.GetTimestamp();

            while (await timer.WaitForNextTickAsync(token))
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTime;

                double elapsedMs = (double)elapsedTicks / Stopwatch.Frequency * 1000.0;

                if (elapsedMs >= AppSettings.PhotoSafeFrequencyMs)
                {
                    break;
                }

                double angle = elapsedMs / AppSettings.PhotoSafeFrequencyMs * 2.0 * Math.PI - (Math.PI / 2.0);

                float intensity = (float)((Math.Sin(angle) + 1.0) / 2.0);

                await this.SafeInvokeAsync(() => ApplyAlertVisuals(intensity));
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] FlashAlertAsync 失敗：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isFlashing, 0);
            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            // 確保 UI 狀態還原。
            this.SafeInvoke(() =>
            {
                try
                {
                    if (IsDisposed ||
                        !IsHandleCreated ||
                        _nud == null)
                    {
                        return;
                    }

                    // 關鍵修正：遞歸重設顏色，觸發 .NET 10 原生主題引擎還原正確配色，防止閃爍殘留。
                    _nud.ResetThemeRecursive();

                    // 恢復焦點視覺狀態。
                    UpdateFocusVisuals(_nud.Focused || _nud.ContainsFocus);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] FlashAlertAsync 還原失敗：{ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// 內部 A11y 廣播方法
    /// </summary>
    /// <param name="message">要廣播的訊息</param>
    /// <param name="interrupt">是否中斷目前的廣播</param>
    private void AnnounceA11y(
        string message,
        bool interrupt = false)
    {
        if (IsDisposed ||
            string.IsNullOrEmpty(message))
        {
            return;
        }

        if (Owner is MainForm mainForm)
        {
            mainForm.AnnounceA11y(message, interrupt);
        }
        else
        {
            long currentId = Interlocked.Increment(ref _a11yDebounceId);

            Task.Run(async () =>
            {
                try
                {
                    // 統一 Audio Ducking 避讓延遲。
                    await Task.Delay(AppSettings.AudioDuckingDelayMs, _cts?.Token ?? CancellationToken.None);

                    if (Interlocked.Read(ref _a11yDebounceId) == currentId &&
                        !IsDisposed &&
                        IsHandleCreated)
                    {
                        await this.SafeInvokeAsync(() =>
                            _announcer?.Announce(message, interrupt && AppSettings.Current.A11yInterruptEnabled));
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消。
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[A11y] 對話框本地廣播失敗：{ex.Message}");
                }
            },
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
        }
    }

    /// <summary>
    /// 更新控制項焦點狀態的視覺表現
    /// </summary>
    /// <param name="isFocused">指示控制項是否具有焦點</param>
    private void UpdateFocusVisuals(bool isFocused)
    {
        try
        {
            if (_nud == null ||
                _nud.IsDisposed)
            {
                return;
            }

            // 綜合判斷：只要數值框本身有焦點，或是旁邊的加減按鈕有焦點，數值框都應該保持高亮。
            bool shouldHighlight = isFocused ||
                (_btnPlus != null && _btnPlus.Focused) ||
                (_btnMinus != null && _btnMinus.Focused);

            if (shouldHighlight)
            {
                if (SystemInformation.HighContrast)
                {
                    _nud.UpdateRecursive(SystemColors.Highlight, SystemColors.HighlightText);
                }
                else
                {
                    bool isDark = this.IsDarkModeActive();

                    // 淺色模式：黑底白字、深色模式：白底黑字。
                    _nud.UpdateRecursive(
                        isDark ? Color.White : Color.Black,
                        isDark ? Color.Black : Color.White);
                }
            }
            else
            {
                _nud.ResetThemeRecursive();
            }

            // 當具有焦點時，強化游標。
            if (isFocused)
            {
                UpdateCaretWidth();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateFocusVisuals 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 根據 DPI 與無障礙設定更新數值框內部游標寬度
    /// </summary>
    private void UpdateCaretWidth()
    {
        try
        {
            if (_nud == null ||
                _nud.IsDisposed ||
                !_nud.IsHandleCreated)
            {
                return;
            }

            // 尋找 NUD 內部的 TextBox 子控制項。
            TextBox? innerTextBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (innerTextBox == null ||
                !innerTextBox.IsHandleCreated)
            {
                return;
            }

            // 基礎寬度 3px，隨 DPI 縮放。
            float scale = DeviceDpi / AppSettings.BaseDpi;

            int caretWidth = (int)Math.Max(3, 3 * scale);

            // 高對比模式下額外加粗。
            if (SystemInformation.HighContrast)
            {
                caretWidth += (int)(2 * scale);
            }

            int caretHeight = innerTextBox.Height;

            // 若寬高與上次一致，則略過 Win32 API 調用，減少 UI 閃爍感。
            if (caretWidth == _lastCaretWidth &&
                caretHeight == _lastCaretHeight)
            {
                return;
            }

            _lastCaretWidth = caretWidth;
            _lastCaretHeight = caretHeight;

            // 使用 Win32 API 重新建立游標。
            if (User32.CreateCaret(innerTextBox.Handle, IntPtr.Zero, caretWidth, caretHeight))
            {
                User32.ShowCaret(innerTextBox.Handle);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateCaretWidth 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新視窗不透明度。
    /// </summary>
    private void UpdateOpacity()
    {
        try
        {
            // 根據規範，若系統開啟高對比模式，則強制為 1.0 以確保絕對可讀性。
            if (SystemInformation.HighContrast)
            {
                Opacity = 1.0;

                return;
            }

            // 數值輸入框鎖定 1.0 不透明度以確保輸入清晰度。
            Opacity = 1.0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateOpacity 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新視窗最小尺寸與按鈕佈局約束
    /// </summary>
    private void UpdateMinimumSize()
    {
        try
        {
            float scale = DeviceDpi / AppSettings.BaseDpi;

            // 內容感知：更新所有按鈕的佈局約束（Bold 預測）。
            // 確保按鈕在獲得焦點變為粗體時，物理邊界保持絕對靜止，達成 Zero-Jitter。
            UpdateButtonConstraints(_btnOk, scale);
            UpdateButtonConstraints(_btnCancel, scale);
            UpdateButtonConstraints(_btnPlus, scale);
            UpdateButtonConstraints(_btnMinus, scale);
            UpdateButtonConstraints(_btnReset, scale);

            // 使用 SizeFromClientSize 進行精確測量。
            // 確保 MinimumSize 包含標題列與邊框，防止內容在高 DPI 或不同視窗風格下被裁剪。
            Size minClientSize = new((int)(450 * scale), (int)(250 * scale));

            Size desiredMinWindowSize = SizeFromClientSize(minClientSize);

            Rectangle workArea = Screen.GetWorkingArea(this);
            int maxFitW = Math.Max(1, workArea.Width - 40),
                maxFitH = Math.Max(1, workArea.Height - 40);

            Size clampedMinWindowSize = new(
                Math.Min(desiredMinWindowSize.Width, maxFitW),
                Math.Min(desiredMinWindowSize.Height, maxFitH));

            MinimumSize = clampedMinWindowSize;

            // 如果目前尺寸超出可視區或低於測量地板，則強制修正。
            if (Width < MinimumSize.Width ||
                Height < MinimumSize.Height ||
                Width > maxFitW ||
                Height > maxFitH)
            {
                // 邊界檢查：確保最小值不超過最大值，防止 Math.Clamp 拋出異常。
                int finalMaxW = Math.Max(MinimumSize.Width, maxFitW),
                    finalMaxH = Math.Max(MinimumSize.Height, maxFitH);

                Size = new Size(
                    Math.Clamp(Width, MinimumSize.Width, finalMaxW),
                    Math.Clamp(Height, MinimumSize.Height, finalMaxH));

                // 佈局擴張後，執行智慧定位檢查。
                ApplySmartPosition();
            }

            // 高對比模式下強制 100% 不透明度。
            if (SystemInformation.HighContrast)
            {
                UpdateOpacity();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新單個按鈕的佈局約束與最小尺寸鎖定
    /// </summary>
    /// <param name="btn">目標按鈕</param>
    /// <param name="scale">目前 DPI 縮放比例</param>
    private void UpdateButtonConstraints(Button? btn, float scale)
    {
        try
        {
            if (btn == null ||
                btn.IsDisposed)
            {
                return;
            }

            // 重置 MinimumSize 以便重新測量。
            btn.MinimumSize = Size.Empty;

            // 取得專屬於此視窗 DPI 的 Bold 字體實例。
            Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, btn.Font.FontFamily);

            // 眼動儀友善：抗抖動寬度鎖定（Anti-Jitter Lock）。
            // 使用 TextRenderer 預先測量 Bold 狀態下的文字寬度，並將其鎖定為 MinimumSize。
            Size boldTextSize = TextRenderer.MeasureText(btn.Text, boldFont);

            int baseMinWidth = Math.Max((int)(120 * scale), boldTextSize.Width + (int)(32 * scale)),
                baseMinHeight = Math.Max((int)(60 * scale), boldTextSize.Height + (int)(24 * scale));

            btn.MinimumSize = new Size(baseMinWidth, baseMinHeight);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateButtonConstraints 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 執行智慧定位修正，確保視窗不會跑出螢幕邊界
    /// </summary>
    private void ApplySmartPosition()
    {
        // 脫離目前的佈局計算循環，確保所有 StartPosition 與 AutoSize 已處理完畢。
        this.SafeBeginInvoke(() =>
        {
            try
            {
                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                // 強制同步最新的實體佈局尺寸（關鍵：確保 Width／Height 是縮放後的真實值）。
                PerformLayout();

                // 以對話框中心點所在的螢幕為準。
                Screen screen = Screen.FromControl(this);

                Rectangle workArea = screen.WorkingArea;

                int newX = Location.X,
                    newY = Location.Y;

                bool adjusted = false;

                // 檢查右邊界。
                if (newX + Width > workArea.Right)
                {
                    newX = workArea.Right - Width;

                    adjusted = true;
                }

                // 檢查左邊界。
                if (newX < workArea.Left)
                {
                    newX = workArea.Left;

                    adjusted = true;
                }

                // 檢查下邊界。
                if (newY + Height > workArea.Bottom)
                {
                    newY = workArea.Bottom - Height;

                    adjusted = true;
                }

                // 檢查上邊界。
                if (newY < workArea.Top)
                {
                    newY = workArea.Top;

                    adjusted = true;
                }

                if (adjusted)
                {
                    Location = new Point(newX, newY);

                    // 告知使用者視窗已修正位置。
                    (Owner as MainForm)?.AnnounceA11y(Strings.A11y_SnapBack);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] ApplySmartPosition 失敗：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 初始化對話框組件
    /// </summary>
    /// <param name="title">對話框標題</param>
    /// <param name="currentValue">當前數值</param>
    /// <param name="defaultValue">預設數值</param>
    /// <param name="decimalPlaces">小數位數</param>
    /// <param name="increment">增量值</param>
    /// <param name="minimum">最小值</param>
    /// <param name="maximum">最大值</param>
    public NumericInputDialog(
        string title,
        decimal currentValue,
        decimal defaultValue,
        int decimalPlaces,
        decimal increment,
        decimal minimum,
        decimal maximum)
    {
        // 邊界驗證：確保最小值不超過最大值，防止 Math.Clamp 拋出異常。
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        _defaultValue = defaultValue;

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AccessibleName = title;
        AccessibleRole = AccessibleRole.Dialog;
        DoubleBuffered = true;

        // 建立 A11y 廣播器（作為備援）。
        _announcer = new AnnouncerLabel
        {
            AccessibleName = "\u00A0"
        };

        // 繼承圖示：優先從主視窗繼承，保持應用程式視覺識別的一致性。
        Icon = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.Icon ??
            ActiveForm?.Icon;

        // 根據 DPI 縮放比例計算佈局參數。
        float scale = DeviceDpi / AppSettings.BaseDpi;

        // 取得共享的 A11y 放大字型（預設為 Regular）。
        _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi);

        // 數值顯示區字體：使用 2.0x 的放大倍率以突顯數值。
        // 注意：此字體為對話框特有，需獨立建立且強制為粗體。
        _nudFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, _a11yFont.FontFamily, 2.0f);

        // 主佈局容器。
        TableLayoutPanel tlpMain = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding((int)(30 * scale)),
            // 設定為 Grouping 角色，協助輔助科技識別這是一個邏輯區塊。
            AccessibleRole = AccessibleRole.Grouping,
            AccessibleName = title
        };

        Label lblPrompt = new()
        {
            Text = string.Format(Strings.Msg_EnterValue, title),
            AccessibleName = string.Format(Strings.Msg_EnterValue, title),
            AutoSize = true,
            MaximumSize = new Size((int)(500 * scale), 0),
            Margin = new Padding(0, 0, 0, (int)(25 * scale)),
            Font = _a11yFont,
            // AccessibleRole.StaticText + 作為首要 Label：
            // WinForms 的 AccessibleRole 列舉不含 Heading，
            // 此標籤置於對話框最頂端，文字內容已充分描述區段目的，
            // 滿足 WCAG 2.4.10 的精神（區段標題，AAA）。
            AccessibleRole = AccessibleRole.StaticText
        };

        // 3x2 網格連動區。
        _tlpGrid = new TableLayoutPanel()
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            AccessibleRole = AccessibleRole.Grouping,
            AccessibleName = string.Format(Strings.Msg_EnterValue, title),
            AccessibleDescription = Strings.A11y_Grid_Numeric_Desc
        };
        _tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 核心控制項建立。

        _nud = new AccessibleNumericUpDown(this)
        {
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            Minimum = minimum,
            Maximum = maximum,
            // 使用 Math.Clamp 確保數值在有效範圍內，防止異常。
            Value = Math.Clamp(currentValue, minimum, maximum),
            TextAlign = HorizontalAlignment.Center,
            // 動態縮放寬度，高度則由 Font 自動決定。
            Width = (int)(220 * scale),
            Font = _nudFont,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            Margin = new Padding((int)(15 * scale), (int)(12 * scale), (int)(15 * scale), (int)(12 * scale)),
            AccessibleName = string.Format(Strings.Msg_EnterValue, title),
            // A11y 描述：報讀目前值與有效範圍。
            AccessibleDescription = string.Format(Strings.A11y_Value_Range_Desc, currentValue, minimum, maximum),
            AccessibleRole = AccessibleRole.SpinButton,
            TabIndex = 1,
            Anchor = AnchorStyles.None
        };

        _nud.Enter += (s, e) =>
        {
            try
            {
                UpdateFocusVisuals(true);

                UpdateCaretWidth();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] _nud.Enter 失敗：{ex.Message}");
            }
        };
        _nud.Leave += (s, e) =>
        {
            try
            {
                Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

                User32.DestroyCaret();

                // 重置游標快取，確保下次焦點進入時能正確重建游標
                _lastCaretWidth = -1;
                _lastCaretHeight = -1;

                UpdateFocusVisuals(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] _nud.Leave 失敗：{ex.Message}");
            }
        };

        _nud.KeyDown += (s, e) =>
        {
            try
            {
                // 處理鍵盤左右鍵位移／選取 A11y 同步。
                if (e.KeyCode == Keys.Left ||
                    e.KeyCode == Keys.Right)
                {
                    // 使用 SafeBeginInvoke 確保在 Windows 原生位移處理完成後，才擷取最新的游標位置。
                    this.SafeBeginInvoke(() =>
                    {
                        try
                        {
                            if (_nud == null ||
                                _nud.IsDisposed)
                            {
                                return;
                            }

                            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

                            if (textBox == null ||
                                textBox.IsDisposed)
                            {
                                return;
                            }

                            if (e.Shift)
                            {
                                if (textBox.SelectionLength > 0)
                                {
                                    AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                                        Strings.A11y_Selected_Text_PrivacySafe :
                                        string.Format(Strings.A11y_Selected_Text, textBox.SelectedText), true);
                                }
                            }
                            else
                            {
                                // 取得目前游標所在的絕對索引（1-based 報讀）。
                                int pos = textBox.SelectionStart;

                                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                                    Strings.A11y_Cursor_Move_PrivacySafe :
                                    string.Format(Strings.A11y_Cursor_Move, pos + 1), true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] KeyDown 位移同步失敗：{ex.Message}");
                        }
                    });
                }

                // Home、End、Ctrl + Left／Right：跳轉游標。
                if (e.KeyCode == Keys.Home ||
                    e.KeyCode == Keys.End ||
                    (e.Control && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)))
                {
                    this.SafeBeginInvoke(() =>
                    {
                        try
                        {
                            if (_nud == null ||
                                _nud.IsDisposed)
                            {
                                return;
                            }

                            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

                            if (textBox != null &&
                                !textBox.IsDisposed)
                            {
                                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                                    Strings.A11y_Cursor_Move_PrivacySafe :
                                    string.Format(Strings.A11y_Cursor_Move, textBox.SelectionStart + 1), true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] KeyDown 跳轉同步失敗：{ex.Message}");
                        }
                    });
                }

                // Backspace 或 Delete：刪除文字。
                if (e.KeyCode == Keys.Back ||
                    e.KeyCode == Keys.Delete)
                {
                    if (_nud == null ||
                        _nud.IsDisposed)
                    {
                        return;
                    }

                    TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

                    if (textBox == null ||
                        textBox.IsDisposed)
                    {
                        return;
                    }

                    HandleDeleteKey(textBox, e.KeyCode == Keys.Back);
                }

                // 確保Home／End 在改變數值時（WinForms 原生行為）也能透過 SafeBeginInvoke 觸發數值變更廣播。
                if (e.KeyCode == Keys.Home ||
                    e.KeyCode == Keys.End)
                {
                    this.SafeBeginInvoke(() =>
                    {
                        try
                        {
                            if (_nud == null ||
                                _nud.IsDisposed)
                            {
                                return;
                            }

                            // 主動通知數值變更，這會協助螢幕閱讀器抓取最新狀態。
                            _nud.NotifyAccessibilityChange();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] KeyDown 邊界通知失敗：{ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] KeyDown 處理失敗：{ex.Message}");
            }
        };

        // 當數值改變時，同步更新無障礙描述，並主動廣播最新數值。
        _nud.ValueChanged += (s, e) =>
        {
            try
            {
                if (_nud == null)
                {
                    return;
                }

                _nud.AccessibleDescription = string.Format(
                    Strings.A11y_Value_Range_Desc,
                    _nud.Value,
                    _nud.Minimum,
                    _nud.Maximum);

                // 廣播包含項目名稱的完整訊息（例如：「不透明度：50%」）。
                string valStr = _nud.DecimalPlaces > 0 ?
                    _nud.Value.ToString($"F{_nud.DecimalPlaces}") :
                    _nud.Value.ToString("F0");

                // 如果是對話框標題（如「不透明度」），則組合起來播報。
                string announcement = $"{title}：{valStr}";

                // 如果有小數位數（如 0.1），檢查是否需要格式化為百分比。
                if (title == Strings.Settings_WindowOpacity)
                {
                    announcement = string.Format(Strings.A11y_Opacity_Changed, _nud.Value / 100);
                }

                AnnounceA11y(announcement, interrupt: true);

                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.CursorMove)
                    .SafeFireAndForget();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] ValueChanged 失敗：{ex.Message}");
            }
        };

        // 按鈕連動 NUD 高亮邏輯。

        // 第 1 列：數值加減。
        _btnMinus = CreateEyeTrackerButton(
            Strings.Btn_Minus,
            Strings.A11y_Btn_Minus_Desc,
            scale,
            _a11yFont,
            (active) =>
            {
                try
                {
                    UpdateFocusVisuals(
                        active ||
                        (_nud?.Focused == true) ||
                        (_nud?.ContainsFocus == true));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] _btnMinus 焦點回呼失敗：{ex.Message}");
                }
            });

        AttachAutoRepeat(_btnMinus, HandleMinus);

        _btnMinus.Anchor = AnchorStyles.None;
        _btnMinus.TabIndex = 0;

        _btnPlus = CreateEyeTrackerButton(
            Strings.Btn_Plus,
            Strings.A11y_Btn_Plus_Desc,
            scale,
            _a11yFont,
            (active) =>
            {
                try
                {
                    UpdateFocusVisuals(
                        active ||
                        (_nud?.Focused == true) ||
                        (_nud?.ContainsFocus == true));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] _btnPlus 焦點回呼失敗：{ex.Message}");
                }
            });

        AttachAutoRepeat(_btnPlus, HandlePlus);

        _btnPlus.Anchor = AnchorStyles.None;
        _btnPlus.TabIndex = 2;

        // 第 2 列：操作按鈕。
        _btnOk = CreateEyeTrackerButton(
            ControlExtensions.GetMnemonicText(Strings.Btn_OK, 'A'),
            Strings.A11y_Btn_OK_Desc,
            scale,
            _a11yFont);
        _btnOk.Click += (s, e) => HandleConfirm();
        _btnOk.Anchor = AnchorStyles.None;
        _btnOk.TabIndex = 5;

        _btnCancel = CreateEyeTrackerButton(
            ControlExtensions.GetMnemonicText(Strings.Btn_Cancel, 'B'),
            Strings.A11y_Btn_Cancel_Desc,
            scale,
            _a11yFont);
        _btnCancel.Click += (s, e) => HandleCancel();
        _btnCancel.Anchor = AnchorStyles.None;
        _btnCancel.TabIndex = 3;

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        _btnReset = CreateEyeTrackerButton(
            ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'Y'),
            string.Format(Strings.A11y_Btn_SetDefault_Desc, _defaultValue),
            scale,
            _a11yFont);
        _btnReset.Click += (s, e) => HandleReset();
        _btnReset.Anchor = AnchorStyles.None;
        _btnReset.TabIndex = 4;

        // 填充 3x2 網格。
        _tlpGrid.Controls.Add(_btnMinus, 0, 0);
        _tlpGrid.Controls.Add(_nud, 1, 0);
        _tlpGrid.Controls.Add(_btnPlus, 2, 0);
        _tlpGrid.Controls.Add(_btnOk, 2, 1);
        _tlpGrid.Controls.Add(_btnCancel, 0, 1);
        _tlpGrid.Controls.Add(_btnReset, 1, 1);

        tlpMain.Controls.Add(lblPrompt, 0, 0);
        tlpMain.Controls.Add(_tlpGrid, 0, 1);

        // 將大小設為 1x1 並移至不可見區域，避免 Size.Empty 被 UIA 剔除。
        _announcer.Size = new Size(1, 1);
        _announcer.Location = new Point(-100, -100);
        Controls.Add(_announcer);
        Controls.Add(tlpMain);

        Shown += (s, e) =>
        {
            try
            {
                // 協調 LiveRegion：當彈出對話框時，暫時停用主視窗的廣播器 LiveSetting，防止訊息干擾。
                if (Owner is MainForm mainForm)
                {
                    mainForm.SetA11yLiveSetting(AutomationLiveSetting.Off);
                }

                // 強化 Context 播報：包含提示文字、目前值與有效範圍。
                string contextMessage = $"{lblPrompt.Text} {string.Format(Strings.A11y_Value_Range_Desc, Value, minimum, maximum)}";

                AnnounceA11y(contextMessage);

                _nud?.Focus();

                UpdateFocusVisuals(true);

                // 執行智慧定位修正，確保視窗初次顯示時不會跑出螢幕邊界。
                ApplySmartPosition();

                this.SafeBeginInvoke(() =>
                {
                    try
                    {
                        _nud?.Select(0, _nud.Text.Length);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NumericInputDialog] 選取文字失敗：{ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] Shown 處理失敗：{ex.Message}");
            }
        };

        Activated += (s, e) =>
        {
            Task.Run(async () =>
            {
                try
                {
                    CancellationToken token = _cts?.Token ?? CancellationToken.None;

                    // 在執行任何延遲操作前先檢查 Token。
                    token.ThrowIfCancellationRequested();

                    await Task.Delay(50, token);

                    // 使用 SafeInvokeAsync 確保在 Handle 毀損或視窗關閉時不會拋出例外。
                    // 內部已包含 IsDisposed 與 IsHandleCreated 檢查。
                    await this.SafeInvokeAsync(() =>
                    {
                        try
                        {
                            _gamepadController?.Resume();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] 恢復控制器失敗：{ex.Message}");
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不進行報錯。
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Activated] 恢復控制器狀態失敗：{ex.Message}");
                }
            },
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
        };

        Deactivate += (s, e) =>
        {
            try
            {
                // 根據規範：視窗失去焦點時必須立即暫停控制器，防止背景誤觸。
                this.SafeBeginInvoke(() =>
                {
                    try
                    {
                        _gamepadController?.Pause();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NumericInputDialog] 暫停控制器失敗：{ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] Deactivate 處理失敗：{ex.Message}");
            }
        };

        FormClosing += (s, e) =>
        {
            try
            {
                if (Owner is MainForm mainForm)
                {
                    mainForm.SetA11yLiveSetting(AutomationLiveSetting.Polite);
                }

                UnsubscribeGamepadEvents();

                if (DialogResult == DialogResult.Cancel)
                {
                    (Owner as MainForm)?.AnnounceA11y(Strings.A11y_Cancelled);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] FormClosing 處理失敗：{ex.Message}");
            }
        };
    }

    /// <summary>
    /// 建立符合眼動儀最佳實踐的按鈕
    /// </summary>
    /// <param name="text">按鈕顯示的文字</param>
    /// <param name="description">按鈕的輔助描述</param>
    /// <param name="scale">縮放比例</param>
    /// <param name="font">字型</param>
    /// <param name="onFocusStateChanged">焦點狀態變更的回呼函式</param>
    /// <returns>建立的按鈕</returns>
    private Button CreateEyeTrackerButton(
        string text,
        string description,
        float scale,
        Font font,
        Action<bool>? onFocusStateChanged = null)
    {
        // 取得專屬於此視窗 DPI 的 Bold 字體實例。
        // 注意：此字體來自 MainForm 的共享快取池，絕對禁止在此處手動 Dispose，
        // 否則會導致其他正在使用同字體的視窗（如 MainForm）發生 GDI 異常。
        Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, font.FontFamily);

        // 眼動儀友善：抗抖動寬度鎖定（Anti-Jitter Lock）。
        // 使用 TextRenderer 預先測量 Bold 字型的最大寬度，並將其鎖定為按鈕的 MinimumSize，
        // 確保按鈕在獲得焦點變為粗體時，物理邊界保持絕對靜止，達成 Zero-Jitter。
        Size boldTextSize = TextRenderer.MeasureText(text, boldFont);

        int baseMinWidth = Math.Max((int)(120 * scale), boldTextSize.Width + (int)(32 * scale)),
            baseMinHeight = Math.Max((int)(60 * scale), boldTextSize.Height + (int)(24 * scale));

        Button btn = new()
        {
            Text = text,
            AccessibleName = text,
            AccessibleDescription = description,
            AccessibleRole = AccessibleRole.PushButton,
            Font = font,
            AutoSize = true,
            MinimumSize = new Size(baseMinWidth, baseMinHeight),
            Margin = new Padding((int)(12 * scale)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Empty,
            ForeColor = Color.Empty
        };

        // 消除 WinForms 原生 AcceptButton 產生的粗邊框。
        btn.FlatAppearance.BorderSize = 0;

        Padding originalPadding = btn.Padding;

        float dwellProgress = 0f;

        long animationId = 0;

        bool isHovered = false;

        // 用來精準追蹤滑鼠「按壓中」的實體狀態。
        bool isPressed = false;

        btn.MouseEnter += (s, e) =>
        {
            // 防止背景誤觸（Prevent Background Midas Touch）。
            if (ActiveForm != this)
            {
                return;
            }

            isHovered = true;

            onFocusStateChanged?.Invoke(true);

            StartAnimationFeedback();
        };

        btn.MouseLeave += (s, e) =>
        {
            isHovered = false;

            // 防呆：按著滑鼠拖到按鈕外，應解除按壓狀態。
            isPressed = false;

            onFocusStateChanged?.Invoke(btn.Focused);

            StopFeedback();
        };

        // 監聽滑鼠左鍵的按下與鬆開，精準控制 isPressed 狀態。
        btn.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                isPressed = true;

                StartAnimationFeedback();
            }
        };

        btn.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                isPressed = false;

                StartAnimationFeedback();
            }
        };

        btn.GotFocus += (s, e) =>
        {
            onFocusStateChanged?.Invoke(true);

            // 根據規範：鍵盤焦點僅執行強烈靜態視覺回饋，不啟動填滿動畫。
            ApplyStrongVisual();
        };

        btn.LostFocus += (s, e) =>
        {
            // 防呆：失去焦點時強制解除按壓狀態。
            isPressed = false;

            onFocusStateChanged?.Invoke(isHovered);

            StopFeedback();
        };

        // 點擊後重置進度，並執行視線重新接合（Gaze Re-engagement）。
        btn.Click += async (s, e) =>
        {
            // 打斷目前的動畫並將進度歸零。
            Interlocked.Increment(ref animationId);

            dwellProgress = 0f;

            btn.Invalidate();

            await Task.Yield();

            if (btn.IsDisposed)
            {
                return;
            }

            // 視線重新接合（Gaze Re-engagement）。
            if (!btn.IsDisposed &&
                btn.Enabled)
            {
                Point cursorPos = btn.PointToClient(Cursor.Position);

                // 檢查游標是否還在按鈕的範圍內
                if (btn.ClientRectangle.Contains(cursorPos))
                {
                    isHovered = true;

                    // 恢復溫和的 Hover 狀態並重啟進度條。
                    StartAnimationFeedback();
                }
                else
                {
                    isHovered = false;

                    StopFeedback();
                }
            }
        };

        void ApplyStrongVisual()
        {
            // 打斷目前的動畫。
            Interlocked.Increment(ref animationId);

            dwellProgress = 0f;

            // 強烈視覺：純鍵盤焦點或實體按壓中。
            if (SystemInformation.HighContrast)
            {
                btn.BackColor = SystemColors.Highlight;
                btn.ForeColor = SystemColors.HighlightText;
            }
            else
            {
                bool isDark = btn.IsDarkModeActive();

                if (isPressed)
                {
                    btn.BackColor = isDark ?
                        Color.FromArgb(255, 200, 120) :
                        Color.FromArgb(28, 28, 28);
                    btn.ForeColor = isDark ?
                        Color.Black :
                        Color.White;
                }
                else
                {
                    btn.BackColor = isDark ?
                        Color.White :
                        Color.Black;
                    btn.ForeColor = isDark ?
                        Color.Black :
                        Color.White;
                }
            }

            // 加粗（大）。
            if (!ReferenceEquals(btn.Font, boldFont))
            {
                btn.Font = boldFont;
            }

            btn.AccessibleDescription = isPressed ?
                $"{description} ({Strings.A11y_State_Pressed})" :
                $"{description} ({Strings.A11y_State_Focused})";
            btn.Padding = new Padding(0);
            btn.Invalidate();
        }

        void StartAnimationFeedback()
        {
            if (!btn.Enabled)
            {
                return;
            }

            // 分離式回饋原則：
            // 1. 實體按壓中，或「具備鍵盤焦點且非滑鼠懸停」時，執行強烈靜態視覺。
            // 2. 其餘情況（主要是 isHovered 為真時），必須啟動 Dwell 動畫以支援 Re-gaze。
            if (isPressed ||
                (btn.Focused && !isHovered))
            {
                ApplyStrongVisual();

                return;
            }

            long id = Interlocked.Increment(ref animationId);

            dwellProgress = 0f;

            // 溫和視覺：滑鼠／眼控懸停（Hover）。
            if (SystemInformation.HighContrast)
            {
                btn.BackColor = SystemColors.HotTrack;
                btn.ForeColor = SystemColors.HighlightText;
            }
            else
            {
                bool isDark = btn.IsDarkModeActive();

                btn.BackColor = isDark ?
                    Color.FromArgb(60, 60, 60) :
                    Color.FromArgb(220, 220, 220);
                btn.ForeColor = isDark ?
                    Color.White :
                    Color.Black;
            }

            // 不加粗，維持一般字體（小）。
            if (!ReferenceEquals(btn.Font, font))
            {
                btn.Font = font;
            }

            btn.AccessibleDescription = $"{description} ({Strings.A11y_State_Hover})";

            // 動畫回饋：只要有 Hover，啟動 Dwell！
            btn.RunDwellAnimationAsync(
                    id,
                    () => Interlocked.Read(ref animationId),
                    (p) => dwellProgress = p,
                    ct: _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            btn.Padding = new Padding(0);
            btn.Invalidate();
        }

        void StopFeedback()
        {
            Interlocked.Increment(ref animationId);

            dwellProgress = 0f;

            if (btn.Focused)
            {
                // 失去滑鼠懸停但仍有鍵盤焦點：切換回強烈視覺。
                ApplyStrongVisual();
            }
            else if (isHovered)
            {
                // 失去鍵盤焦點但仍有滑鼠懸停：切換回動畫狀態。
                StartAnimationFeedback();
            }
            else
            {
                // 徹底失去所有關注，還原為原始狀態。
                btn.BackColor = Color.Empty;
                btn.ForeColor = Color.Empty;

                if (!ReferenceEquals(btn.Font, font))
                {
                    btn.Font = font;
                }

                btn.Padding = originalPadding;
                btn.AccessibleDescription = description;
            }

            btn.Invalidate();
        }

        btn.Paint += (s, e) =>
        {
            if (btn == null)
            {
                return;
            }

            // 動態存取最新的 DeviceDpi，避免靜態捕獲舊 DPI 導致跨螢幕拖曳時繪圖偏移。
            float currentScale = btn.DeviceDpi / AppSettings.BaseDpi;

            bool isDark = btn.IsDarkModeActive();

            // 停用態：統一使用共用非色彩提示（虛線邊框 + 斜線）。
            if (btn.TryDrawDisabledButtonCue(e.Graphics, isDark, currentScale))
            {
                return;
            }

            // 判斷該按鈕是否為目前對話框的預設動作按鈕（AcceptButton）。
            // 只有當目前焦點「不在任何按鈕上」時，預設按鈕才顯示焦點邊框，避免雙焦點誤導。
            bool isDefault = ReferenceEquals(AcceptButton, btn) &&
                ActiveControl is not Button &&
                btn.Enabled;

            // 基礎邊框（預設狀態）。
            // 由於 btn.FlatAppearance.BorderSize = 0，必須手動繪製預設邊框，否則會跟背景融合。
            if (!btn.Focused &&
                !isHovered &&
                !isDefault)
            {
                btn.DrawButtonBaseBorder(e.Graphics, isDark, currentScale);
            }

            // 繪製焦點與 Hover 邊框（Focus／Hover Border）。
            if (btn.Focused ||
                isHovered ||
                isDefault)
            {

                bool isStrongVisual = isPressed ||
                    (btn.Focused && !isHovered);

                // 邊框色依 btn 的互動狀態動態選取，對齊 BtnCopy 與 BtnClose 的情境感知邏輯，
                // 確保在強視覺（Focus／Pressed）與中性（懸停灰／isDefault 系統色）下皆達 WCAG AAA：
                Color borderColor = btn.GetButtonInteractiveBorderColor(isStrongVisual, isDark);

                btn.DrawButtonInteractiveBorder(
                    e.Graphics,
                    borderColor,
                    currentScale,
                    out int inset,
                    out int borderThickness);

                if (!SystemInformation.HighContrast &&
                    isPressed)
                {
                    btn.DrawPressedInnerCue(e.Graphics, currentScale, inset, borderThickness);
                }
            }

            // 後繪製注視進度條（Dwell Feedback）。
            // 使用紋理（Hatch Pattern）補償全色盲使用者。
            if (dwellProgress > 0)
            {
                int barHeight = (int)(6 * currentScale),
                    barWidth = (int)(btn.Width * dwellProgress);

                Rectangle barRect = new(0, btn.Height - barHeight, barWidth, barHeight);

                if (SystemInformation.HighContrast)
                {
                    using Brush barBrush = new SolidBrush(SystemColors.HighlightText);

                    e.Graphics.FillRectangle(barBrush, barRect);
                }
                else
                {
                    // 進度條繪製於懸停灰底之上（非焦點黑／白底），選用綠色系以與焦點藍、警示橘形成三色語意分工。
                    // 淺色懸停底（#DCDCDC）→ Green 3.75:1；深色懸停底（#3C3C3C）→ LimeGreen 5.21:1。
                    // 全類型 CVD 最低對比：淺色 3.50:1、深色 3.45:1，均符合 WCAG 1.4.11 非文字 UI ≥ 3:1。
                    Color baseColor = isDark ?
                            Color.LimeGreen :
                            Color.Green,
                        hatchColor = isDark ?
                            // DarkGreen on LimeGreen = 3.51:1（全 CVD ≥ 3.45:1）。
                            Color.DarkGreen :
                            // PaleGreen on Green = 4.06:1（全 CVD ≥ 3.50:1）。
                            Color.PaleGreen;

                    // 雙重編碼：實心背景 + 斜向條紋紋理。
                    using Brush bgBrush = new SolidBrush(baseColor);
                    using Brush hatchBrush = new HatchBrush(
                        HatchStyle.BackwardDiagonal,
                        hatchColor,
                        Color.Transparent);

                    e.Graphics.FillRectangle(bgBrush, barRect);
                    e.Graphics.FillRectangle(hatchBrush, barRect);
                }
            }
        };

        return btn;
    }

    /// <summary>
    /// 為按鈕附加「長按連發（Auto-Repeat）」功能，並完美避開原生 Click 的衝突
    /// </summary>
    /// <param name="btn">Button</param>
    /// <param name="action">Action</param>
    private void AttachAutoRepeat(Button? btn, Action action)
    {
        if (btn == null)
        {
            return;
        }

        CancellationTokenSource? repeatCts = null;

        bool suppressNextClick = false;

        // 滑鼠按下時，啟動連發任務。
        btn.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left ||
                !btn.Enabled)
            {
                return;
            }

            suppressNextClick = false;

            repeatCts?.CancelAndDispose();
            repeatCts = CancellationTokenSource
                .CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);

            CancellationToken token = repeatCts.Token;

            // 取得目前連發設定（對齊控制器標準）。
            AppSettings.GamepadConfigSnapshot config = AppSettings.Current.GamepadSettings;

            int initialDelayMs = (int)(config.RepeatInitialDelayFrames * AppSettings.TargetFrameTimeMs),
                intervalMs = (int)(config.RepeatIntervalFrames * AppSettings.TargetFrameTimeMs);

            // 啟動連發背景任務。
            Task.Run(async () =>
            {
                try
                {
                    // 初始防呆延遲。
                    await Task.Delay(initialDelayMs, token);

                    this.SafeInvoke(() => suppressNextClick = true);

                    // 連發頻率：對齊目標基準。
                    using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(intervalMs));

                    while (await timer.WaitForNextTickAsync(token))
                    {
                        this.SafeInvoke(action);
                    }
                }
                catch (OperationCanceledException)
                {

                }
            },
            token)
            .SafeFireAndForget();
        };

        // 任何中斷動作都會停止連發。
        void StopRepeat()
        {
            repeatCts?.CancelAndDispose();
            repeatCts = null;
        }

        btn.MouseUp += (s, e) => StopRepeat();
        btn.MouseLeave += (s, e) => StopRepeat();
        btn.LostFocus += (s, e) => StopRepeat();

        // 接管 Click 事件：執行動作，或過濾掉長按鬆開時產生的多餘點擊。
        btn.Click += (s, e) =>
        {
            if (suppressNextClick)
            {
                suppressNextClick = false;

                return;
            }

            action();
        };

        btn.Disposed += (s, e) => StopRepeat();
    }
}