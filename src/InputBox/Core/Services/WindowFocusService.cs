using InputBox.Core.Configuration;
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
    private nint _capturedHwnd;

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
            for (int i = 0; i < AppSettings.WindowSwitchMaxRetries; i++)
            {
                User32.SetForegroundWindow(targetHwnd);

                // 給系統一點時間反應。
                await Task.Delay(AppSettings.WindowSwitchRetryDelayMs, cancellationToken);

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
            LoggerService.LogException(ex, "還原視窗失敗");

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

        // 確保目標不是目前正在互動的前景視窗，否則閃爍不會顯示。
        if (User32.ForegroundWindow == hwnd)
        {
            return;
        }

        // 若系統層級關閉 UI 特效，避免持續閃爍，僅做一次提醒。
        if (!SystemInformation.UIEffectsEnabled)
        {
            User32.FlashWindow(hwnd, true);

            return;
        }

        // 先執行一次簡單版本的 FlashWindow 作為觸發引導。
        User32.FlashWindow(hwnd, true);

        // 使用更詳盡的 FlashWindowEx 進行有限次數閃爍。
        User32.FlashWindowInfo flashInfo = new()
        {
            Hwnd = hwnd,
            Flags = User32.FlashWindowFlags.All,
            Count = AppSettings.TaskbarFlashSafeCount,
            Timeout = 0,
            // 核心修正：手動計算結構大小。
            // 在 64 位元環境下，uint(4) + nint(8) + uint(4) + uint(4) + uint(4) = 24，
            // 但由於對齊（Alignment），結構大小實際為 32 位元組。
            // Marshal.SizeOf 會根據 Runtime 環境動態決定，
            // 在此明確賦值給 Size 欄位後再傳入。
            Size = (uint)Marshal.SizeOf<User32.FlashWindowInfo>()
        };

        if (!User32.FlashWindowEx(in flashInfo))
        {
            Debug.WriteLine("FlashWindowEx 呼叫失敗。");
        }
    }
}