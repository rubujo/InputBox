using InputBox.Core.Input;
using InputBox.Core.Services;
using System.Diagnostics;

namespace InputBox.Core.Utilities;

/// <summary>
/// Gamescope 下用於重建 WinForms top-level surface 的共用救援流程。
/// </summary>
internal static class GamescopeSurfaceRecovery
{
    /// <summary>
    /// 防止多個 Form 或連續控制器事件同時重建 HWND。
    /// </summary>
    private static int _isRecoveringSurface;

    /// <summary>
    /// 判斷目前控制器狀態是否符合 Gamescope surface recovery 組合鍵。
    /// </summary>
    /// <param name="controller">目前控制器。</param>
    /// <returns>符合 LB + RB 修飾鍵且正在 Gamescope 下執行時為 true。</returns>
    public static bool IsRecoveryChordActive(IGamepadController? controller)
    {
        return SystemHelper.IsRunningOnGamescope() &&
            controller?.IsLeftShoulderHeld == true &&
            controller.IsRightShoulderHeld;
    }

    /// <summary>
    /// 若目前按鍵符合 Gamescope surface recovery 組合鍵，重建指定 Form 的 HWND。
    /// </summary>
    /// <param name="target">要重建 surface 的 top-level Form。</param>
    /// <param name="recreateHandle">由目標 Form 傳入的 <see cref="Control.RecreateHandle"/> 執行委派。</param>
    /// <param name="controller">目前控制器。</param>
    /// <param name="beforeRecover">重建前的選單或 popup 清理流程。</param>
    /// <param name="afterRecover">重建後的視窗樣式或焦點還原流程。</param>
    /// <param name="context">記錄錯誤時使用的情境字串。</param>
    /// <returns>若已處理 recovery chord 則為 true；否則為 false。</returns>
    public static bool TryRecoverFromGamepadChord(
        Form target,
        Action recreateHandle,
        IGamepadController? controller,
        Action? beforeRecover = null,
        Action? afterRecover = null,
        string? context = null)
    {
        if (!IsRecoveryChordActive(controller))
        {
            return false;
        }

        RecoverFormSurface(target, recreateHandle, beforeRecover, afterRecover, context);

        return true;
    }

    /// <summary>
    /// 重建指定 Form 的 HWND，讓 Gamescope 重新取得可合成的 top-level surface。
    /// </summary>
    /// <param name="target">要重建 surface 的 top-level Form。</param>
    /// <param name="recreateHandle">由目標 Form 傳入的 <see cref="Control.RecreateHandle"/> 執行委派。</param>
    /// <param name="beforeRecover">重建前的選單或 popup 清理流程。</param>
    /// <param name="afterRecover">重建後的視窗樣式或焦點還原流程。</param>
    /// <param name="context">記錄錯誤時使用的情境字串。</param>
    public static void RecoverFormSurface(
        Form target,
        Action recreateHandle,
        Action? beforeRecover = null,
        Action? afterRecover = null,
        string? context = null)
    {
        if (!SystemHelper.IsRunningOnGamescope() ||
            target.IsDisposed)
        {
            return;
        }

        // 同一時間只允許一個 top-level Form 重建 HWND，避免 popup/dialog 鏈同時失效。
        if (Interlocked.Exchange(ref _isRecoveringSurface, 1) != 0)
        {
            return;
        }

        // 重建後嘗試恢復原本的焦點控制項；若控制項已釋放或不可聚焦則略過。
        Control? activeControl = target.ActiveControl;

        try
        {
            beforeRecover?.Invoke();

            target.SuspendLayout();

            if (!target.Visible)
            {
                target.Show();
            }

            if (target.IsHandleCreated)
            {
                recreateHandle();
            }

            if (!target.Visible)
            {
                target.Show();
            }

            afterRecover?.Invoke();

            target.BringToFront();
            target.Activate();

            if (activeControl is { IsDisposed: false, CanFocus: true })
            {
                activeControl.Focus();
            }
        }
        catch (Exception ex)
        {
            // context 用於區分是哪一種 Form 觸發 recovery，方便 Steam Deck 實機日誌判讀。
            string message = context ?? "Gamescope surface recovery 失敗";

            LoggerService.LogException(ex, message);
            Debug.WriteLine($"[{nameof(GamescopeSurfaceRecovery)}] {message}：{ex.Message}");
        }
        finally
        {
            target.ResumeLayout(performLayout: true);
            Interlocked.Exchange(ref _isRecoveringSurface, 0);
        }
    }
}
