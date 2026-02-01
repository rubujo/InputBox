
using InputBox.Libraries.Interop;

namespace InputBox.Libraries.Manager;

/// <summary>
/// 視窗焦點管理器
/// </summary>
public class WindowFocusManager
{
    /// <summary>
    /// 要返回的視窗 HWND
    /// </summary>
    private nint _previousWindowHwnd;

    /// <summary>
    /// 切換視窗重試間隔
    /// </summary>
    private const int Delay_WindowSwitchRetry = 50;

    /// <summary>
    /// 視窗切換重試次數
    /// </summary>
    private const int Retry_WindowSwitch = 3;

    /// <summary>
    /// 捕捉當前的前景視窗作為返回目標
    /// </summary>
    public void CaptureCurrentWindow()
    {
        _previousWindowHwnd = Win32.GetForegroundWindow();
    }

    /// <summary>
    /// 清除捕捉的視窗記錄
    /// </summary>
    public void ClearCapturedWindow()
    {
        _previousWindowHwnd = IntPtr.Zero;
    }

    /// <summary>
    /// 嘗試切換回捕捉的視窗
    /// </summary>
    /// <returns>Task&lt;bool&gt;</returns>
    public async Task<bool> RestorePreviousWindowAsync()
    {
        if (_previousWindowHwnd == IntPtr.Zero ||
            !Win32.IsWindow(_previousWindowHwnd))
        {
            return false;
        }

        // 嘗試多次切換，直到成功或次數用盡。
        for (int i = 0; i < Retry_WindowSwitch; i++)
        {
            Win32.SetForegroundWindow(_previousWindowHwnd);

            // 給系統一點時間反應。
            await Task.Delay(Delay_WindowSwitchRetry);

            // 如果已經成功切換過去，就結束。
            if (Win32.GetForegroundWindow() == _previousWindowHwnd)
            {
                return true;
            }
        }

        return false;
    }
}