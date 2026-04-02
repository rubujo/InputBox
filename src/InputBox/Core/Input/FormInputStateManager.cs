namespace InputBox.Core.Input;

/// <summary>
/// 表單輸入狀態旗標管理器，集中封裝 Interlocked／Volatile 轉換邏輯
/// </summary>
internal sealed class FormInputStateManager
{
    private int _isReturning;
    private int _isShowingTouchKeyboard;
    private int _isFlashing;
    private int _isProcessingActivated;
    private int _isCapturingHotkey;

    /// <summary>
    /// 是否正在快速鍵擷取模式
    /// </summary>
    public bool IsHotkeyCaptureActive => Volatile.Read(ref _isCapturingHotkey) != 0;

    /// <summary>
    /// 是否正在顯示觸控鍵盤
    /// </summary>
    public bool IsShowingTouchKeyboard => Volatile.Read(ref _isShowingTouchKeyboard) != 0;

    /// <summary>
    /// 嘗試進入返回前景視窗流程
    /// </summary>
    /// <returns>若成功進入流程則回傳 true。</returns>
    public bool TryBeginReturning() => Interlocked.CompareExchange(ref _isReturning, 1, 0) == 0;

    /// <summary>
    /// 結束返回前景視窗流程
    /// </summary>
    public void EndReturning() => Interlocked.Exchange(ref _isReturning, 0);

    /// <summary>
    /// 嘗試進入觸控鍵盤顯示流程
    /// </summary>
    /// <returns>若成功進入流程則回傳 true。</returns>
    public bool TryBeginTouchKeyboard() => Interlocked.CompareExchange(ref _isShowingTouchKeyboard, 1, 0) == 0;

    /// <summary>
    /// 結束觸控鍵盤顯示流程
    /// </summary>
    public void EndTouchKeyboard() => Interlocked.Exchange(ref _isShowingTouchKeyboard, 0);

    /// <summary>
    /// 嘗試進入閃爍動畫流程
    /// </summary>
    /// <returns>若成功進入流程則回傳 true。</returns>
    public bool TryBeginFlashing() => Interlocked.CompareExchange(ref _isFlashing, 1, 0) == 0;

    /// <summary>
    /// 結束閃爍動畫流程
    /// </summary>
    public void EndFlashing() => Interlocked.Exchange(ref _isFlashing, 0);

    /// <summary>
    /// 嘗試進入 Activated 事件處理流程
    /// </summary>
    /// <returns>若成功進入流程則回傳 true。</returns>
    public bool TryBeginProcessingActivated() => Interlocked.CompareExchange(ref _isProcessingActivated, 1, 0) == 0;

    /// <summary>
    /// 結束 Activated 事件處理流程
    /// </summary>
    public void EndProcessingActivated() => Interlocked.Exchange(ref _isProcessingActivated, 0);

    /// <summary>
    /// 進入快速鍵擷取模式
    /// </summary>
    public void BeginHotkeyCapture() => Interlocked.Exchange(ref _isCapturingHotkey, 1);

    /// <summary>
    /// 結束快速鍵擷取模式
    /// </summary>
    public void EndHotkeyCapture() => Interlocked.Exchange(ref _isCapturingHotkey, 0);
}