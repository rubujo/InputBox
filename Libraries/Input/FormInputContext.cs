namespace InputBox.Libraries.Input;

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
    private bool _disposed;

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
        // 處理最小化。
        _form.SizeChanged += OnFormStateChanged;
        _form.FormClosed += OnFormClosed;
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
        UpdateState();
    }

    /// <summary>
    /// Form 關閉
    /// </summary>
    /// <param name="sender">object?</param>
    /// <param name="e">EventArgs</param>
    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _isInputActive = false;
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
            isNotMinimized = _form.WindowState != FormWindowState.Minimized;

        _isInputActive = isVisible &&
            isNotMinimized;
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
        _form.VisibleChanged -= OnFormStateChanged;
        _form.SizeChanged -= OnFormStateChanged;
        _form.FormClosed -= OnFormClosed;
    }
}