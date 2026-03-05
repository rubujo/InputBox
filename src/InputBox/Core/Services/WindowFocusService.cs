using InputBox.Core.Interop;

namespace InputBox.Core.Services;

/// <summary>
/// 視窗焦點管理器
/// </summary>
public class WindowFocusService
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
    /// 捕捉目前的前景視窗作為返回目標
    /// </summary>
    public void CaptureCurrentWindow()
    {
        nint hwnd = Win32.GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 取得該視窗所屬的進程 PID。
        _ = Win32.GetWindowThreadProcessId(hwnd, out uint processId);

        // 如果該視窗就是「我們自己」，就不要捕捉。
        // 這能防止使用者在 InputBox 已經開啟時再次按下熱鍵，導致捕捉到自己。
        if (processId == Environment.ProcessId)
        {
            return;
        }

        _previousWindowHwnd = hwnd;
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