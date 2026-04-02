using InputBox.Core.Services;
using System.Diagnostics;

namespace InputBox.Core.Input;

/// <summary>
/// FormInputContext
/// </summary>
internal sealed class FormInputContext : IInputContext, IDisposable
{
    /// <summary>
    /// Form
    /// </summary>
    private readonly Form _form;

    /// <summary>
    /// Input 是否啟用
    /// <para>使用 volatile 確保背景執行緒能讀到最新的值。</para>
    /// </summary>
    private volatile bool _isInputActive;

    /// <summary>
    /// 是否已處置
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// FormInputContext
    /// </summary>
    /// <param name="form">Form</param>
    public FormInputContext(Form form)
    {
        _form = form;

        // 初始狀態檢查。
        UpdateState();

        // 訂閱事件以被動更新狀態。
        _form.VisibleChanged += OnFormStateChanged;
        _form.Activated += OnFormStateChanged;
        _form.Deactivate += OnFormStateChanged;
        // 處理最小化。
        _form.SizeChanged += OnFormStateChanged;
        _form.FormClosed += OnFormClosed;

        // 輔助捕捉子對話框的焦點轉移與切換空窗期。
        Application.Idle += OnApplicationIdle;
    }

    /// <summary>
    /// Input 是否啟用
    /// </summary>
    public bool IsInputActive
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            // 直接讀取變數，無需 Invoke，零效能消耗。
            return _isInputActive;
        }
    }

    /// <summary>
    /// Form 狀態變更
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">EventArgs</param>
    private void OnFormStateChanged(object? sender, EventArgs e)
    {
        try
        {
            UpdateState();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "FormInputContext.OnFormStateChanged 處理失敗");

            Debug.WriteLine($"[事件] Form 狀態變更處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 系統閒置時的輔助更新
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">EventArgs</param>
    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        try
        {
            UpdateState();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "FormInputContext.OnApplicationIdle 處理失敗");

            Debug.WriteLine($"[事件] ApplicationIdle處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// Form 關閉
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">EventArgs</param>
    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        try
        {
            _isInputActive = false;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "FormInputContext.OnFormClosed 處理失敗");

            Debug.WriteLine($"[事件] FormClosed處理失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新狀態
    /// </summary>
    private void UpdateState()
    {
        if (_form.IsDisposed)
        {
            _isInputActive = false;

            return;
        }

        // 這些判斷只會在 UI 執行緒觸發事件時執行，不會影響背景輪詢效能。
        bool isVisible = _form.Visible,
            isNotMinimized = _form.WindowState != FormWindowState.Minimized,
            isActive = Form.ActiveForm != null;

        _isInputActive = isVisible &&
            isNotMinimized &&
            isActive;
    }

    /// <summary>
    /// 處置
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 解除訂閱，防止記憶體洩漏。
        // 統一包裹在 try-catch 中，防止視窗已處置或關閉時序導致的例外。
        try
        {
            _form.VisibleChanged -= OnFormStateChanged;
            _form.Activated -= OnFormStateChanged;
            _form.Deactivate -= OnFormStateChanged;
            _form.SizeChanged -= OnFormStateChanged;
            _form.FormClosed -= OnFormClosed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"表單事件解除訂閱失敗，已忽略：{ex.Message}");
        }

        // 解除閒置訂閱。
        // 注意：Application.Idle 與 UI 訊息迴圈掛鉤，
        // 建議在 UI 執行緒執行解除。
        try
        {
            Application.Idle -= OnApplicationIdle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"閒置事件解除訂閱失敗，已忽略：{ex.Message}");
        }
    }
}