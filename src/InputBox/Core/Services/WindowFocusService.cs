using InputBox.Core.Configuration;
using InputBox.Core.Interop;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputBox.Core.Services;

/// <summary>
/// 視窗焦點服務
/// </summary>
internal sealed class WindowFocusService
{
    /// <summary>
    /// 保護 _capturedHwnd 讀寫的鎖定物件。
    /// </summary>
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
    /// <returns>若目前前景可作為返回目標則回傳 true。</returns>
    public bool CaptureCurrentWindow()
    {
        nint foregroundHwnd = User32.ForegroundWindow;

        if (foregroundHwnd == IntPtr.Zero ||
            !User32.IsWindow(foregroundHwnd))
        {
            return false;
        }

        _ = User32.GetWindowThreadProcessId(foregroundHwnd, out uint processId);

        // 目前前景若是自己，不應當成返回目標。
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        lock (_lockObj)
        {
            _capturedHwnd = foregroundHwnd;
        }

        return true;
    }

    /// <summary>
    /// 嘗試捕捉指定視窗作為返回目標
    /// </summary>
    /// <param name="hwnd">欲捕捉的視窗控制代碼</param>
    /// <returns>若成功捕捉則回傳 true。</returns>
    public bool TryCaptureWindow(nint hwnd)
    {
        if (hwnd == IntPtr.Zero ||
            !User32.IsWindow(hwnd))
        {
            return false;
        }

        // 取得該視窗所屬的進程 PID。
        _ = User32.GetWindowThreadProcessId(hwnd, out uint processId);

        // 如果該視窗就是「我們自己」，就不要捕捉。
        // 這能防止使用者在 InputBox 已經開啟時再次按下熱鍵，導致捕捉到自己。
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        lock (_lockObj)
        {
            // 小型防呆：避免重複覆寫相同目標，降低高頻切換時的無效更新。
            if (_capturedHwnd == hwnd)
            {
                return false;
            }

            _capturedHwnd = hwnd;
        }

        return true;
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

        return await RestoreWindowAsync(targetHwnd, cancellationToken);
    }

    /// <summary>
    /// 嘗試切換至指定視窗
    /// </summary>
    /// <param name="targetHwnd">目標視窗 Handle</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Task&lt;bool&gt;</returns>
    public static async Task<bool> RestoreWindowAsync(
        nint targetHwnd,
        CancellationToken cancellationToken = default)
    {

        if (targetHwnd == IntPtr.Zero ||
            !User32.IsWindow(targetHwnd))
        {
            return false;
        }

        try
        {
            // 第一段：先用低侵入方式切換，避免不必要的執行緒附加。
            // 僅在視窗確實處於最小化狀態時才呼叫 SW_RESTORE，
            // 避免對全螢幕（Xbox 達陣全螢幕）或最大化視窗呼叫後造成畫面跟版。
            if (User32.IsIconic(targetHwnd))
            {
                _ = User32.ShowWindow(targetHwnd, User32.ShowWindowCommand.Restore);
            }

            _ = User32.BringWindowToTop(targetHwnd);
            _ = User32.SetForegroundWindow(targetHwnd);

            await Task.Delay(AppSettings.WindowSwitchRetryDelayMs, cancellationToken);

            if (User32.ForegroundWindow == targetHwnd)
            {
                return true;
            }

            // 第二段：第一段失敗才啟用執行緒附加強化切換。
            for (int i = 0; i < AppSettings.WindowSwitchMaxRetries; i++)
            {
                uint currentThreadId = User32.GetCurrentThreadId(),
                    targetThreadId = User32.GetWindowThreadProcessId(targetHwnd, out _),
                    foregroundThreadId = User32.GetWindowThreadProcessId(User32.ForegroundWindow, out _);

                bool attachedToTarget = false,
                    attachedToForeground = false;

                try
                {
                    if (targetThreadId != 0 &&
                        targetThreadId != currentThreadId)
                    {
                        attachedToTarget = User32.AttachThreadInput(currentThreadId, targetThreadId, true);
                    }

                    if (foregroundThreadId != 0 &&
                        foregroundThreadId != targetThreadId)
                    {
                        attachedToForeground = User32.AttachThreadInput(foregroundThreadId, targetThreadId, true);
                    }

                    // 先還原並提升 Z-Order，再切前景，提升跨應用視窗切換穩定性。
                    // 同第一段，僅在視窗最小化時才呼叫 SW_RESTORE，防止將全螢幕視窗退出全螢幕。
                    if (User32.IsIconic(targetHwnd))
                    {
                        _ = User32.ShowWindow(targetHwnd, User32.ShowWindowCommand.Restore);
                    }

                    _ = User32.BringWindowToTop(targetHwnd);
                    _ = User32.SetForegroundWindow(targetHwnd);
                    _ = User32.SetFocus(targetHwnd);
                }
                finally
                {
                    if (attachedToForeground)
                    {
                        _ = User32.AttachThreadInput(foregroundThreadId, targetThreadId, false);
                    }

                    if (attachedToTarget)
                    {
                        _ = User32.AttachThreadInput(currentThreadId, targetThreadId, false);
                    }
                }

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