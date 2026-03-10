using InputBox.Core.Interop;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputBox.Core.Services;

/// <summary>
/// 視窗焦點服務
/// </summary>
public class WindowFocusService
{
    private readonly Lock _lockObj = new();

    /// <summary>
    /// 目前捕捉到的視窗控制代碼
    /// </summary>
    private volatile nint _capturedHwnd;

    /// <summary>
    /// 取得目前捕捉到的視窗控制代碼
    /// </summary>
    public nint CapturedHwnd
    {
        get
        {
            lock (_lockObj)
            {
                return _capturedHwnd;
            }
        }
    }

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
        nint hwnd = User32.ForegroundWindow;

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 取得該視窗所屬的進程 PID。
        _ = User32.GetWindowThreadProcessId(hwnd, out uint processId);

        // 如果該視窗就是「我們自己」，就不要捕捉。
        // 這能防止使用者在 InputBox 已經開啟時再次按下熱鍵，導致捕捉到自己。
        if (processId == Environment.ProcessId)
        {
            return;
        }

        lock (_lockObj)
        {
            _capturedHwnd = hwnd;
        }
    }

    /// <summary>
    /// 清除捕捉的視窗記錄
    /// </summary>
    public void ClearCapturedWindow()
    {
        lock (_lockObj)
        {
            _capturedHwnd = IntPtr.Zero;
        }
    }

    /// <summary>
    /// 嘗試切換回捕捉的視窗
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Task&lt;bool&gt;</returns>
    public async Task<bool> RestorePreviousWindowAsync(CancellationToken cancellationToken = default)
    {
        nint targetHwnd;

        lock (_lockObj)
        {
            targetHwnd = _capturedHwnd;
        }

        if (targetHwnd == IntPtr.Zero ||
            !User32.IsWindow(targetHwnd))
        {
            return false;
        }

        try
        {
            // 嘗試多次切換，直到成功或次數用盡。
            for (int i = 0; i < Retry_WindowSwitch; i++)
            {
                User32.SetForegroundWindow(targetHwnd);

                // 給系統一點時間反應。
                await Task.Delay(Delay_WindowSwitchRetry, cancellationToken);

                // 如果已經成功切換過去，就結束。
                if (User32.ForegroundWindow == targetHwnd)
                {
                    return true;
                }
            }

            if (User32.ForegroundWindow != targetHwnd)
            {
                // 如果切換失敗，至少讓目標視窗在工作列閃爍，提示使用者。
                FlashWindow(targetHwnd);
            }
        }
        catch (OperationCanceledException)
        {
            // 任務取消。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"還原視窗時發生錯誤：{ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// 讓指定的視窗在工作列閃爍
    /// </summary>
    /// <param name="hwnd">視窗 Handle</param>
    public static void FlashWindow(nint hwnd)
    {
        if (hwnd == IntPtr.Zero ||
            !User32.IsWindow(hwnd))
        {
            return;
        }

        User32.FlashWindowInfo flashInfo = new()
        {
            Hwnd = hwnd,
            Flags = User32.FlashWindowFlags.All |
                User32.FlashWindowFlags.TimerNoForeground,
            Count = uint.MaxValue,
            Timeout = 0
        };

        flashInfo.Size = (uint)Marshal.SizeOf(flashInfo);

        User32.FlashWindowEx(in flashInfo);
    }
}