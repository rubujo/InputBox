using InputBox.Core.Configuration;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Utilities;
using System.Media;

namespace InputBox.Core.Services;

/// <summary>
/// 視窗導航服務
/// </summary>
/// <remarks>
/// 負責處理視窗切換的流程控制，包含安全檢查、隨機延遲與執行切換。
/// </remarks>
/// <param name="windowFocusManager">視窗焦點管理器</param>
internal class WindowNavigationService(WindowFocusService windowFocusManager)
{
    /// <summary>
    /// WindowFocusManager
    /// </summary>
    private readonly WindowFocusService _windowFocusManager = windowFocusManager;

    /// <summary>
    /// 檢查目前是否具備有效的返回目標
    /// </summary>
    public bool CanNavigateBack => _windowFocusManager.CapturedHwnd != IntPtr.Zero &&
        User32.IsWindow(_windowFocusManager.CapturedHwnd);

    /// <summary>
    /// 執行返回前一個視窗的流程
    /// </summary>
    /// <param name="controller">目前的控制器實例（用於檢查按鍵狀態）</param>
    /// <param name="announceErrorAction">若切換失敗時的廣播委派（選用）</param>
    /// <returns>Task</returns>
    public async Task NavigateBackAsync(
        IGamepadController? controller,
        Action<string>? announceErrorAction = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 取得先前捕捉的視窗 Handle。
        IntPtr targetHwnd = _windowFocusManager.CapturedHwnd;

        // 2. 預先檢查目標視窗是否仍然有效。
        // 若視窗已被使用者手動關閉，我們應立即停止流程。
        if (targetHwnd == IntPtr.Zero ||
            !User32.IsWindow(targetHwnd))
        {
            // 播放警告音效與失敗震動。
            FeedbackService.PlaySound(SystemSounds.Exclamation);

            _ = FeedbackService.VibrateAsync(controller, VibrationPatterns.ActionFail, cancellationToken);

            announceErrorAction?.Invoke(Resources.Strings.A11y_TargetWindowLost);

            return;
        }

        // 播放音效／震動，給予即時回饋。
        FeedbackService.PlaySound(SystemSounds.Exclamation);

        _ = FeedbackService.VibrateAsync(controller, VibrationPatterns.ReturnStart, cancellationToken);

        using CancellationTokenSource ctsTimeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        ctsTimeout.CancelAfter(AppSettings.KeyReleaseTimeoutMs);

        try
        {
            // 等待所有「危險按鍵」被放開，檢查頻率 10ms ~ 16ms（約 60fps）即可。
            // 增加 IsConnected 檢查，若控制器斷開則停止等待。
            while (controller != null &&
                controller.IsConnected &&
                ShouldWaitForKeyRelease(controller) &&
                !ctsTimeout.Token.IsCancellationRequested)
            {
                await Task.Delay(AppSettings.KeyReleaseCheckIntervalMs, ctsTimeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 捕捉到取消或超時。
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }

        // 捕捉設定快照（包含隨機抖動），確保延遲邏輯的一致性與可重複性。
        // 使用高斯分佈 (Gaussian Distribution) 模擬人類生理反應，降低被 Anti-Cheat 統計學偵測的風險。
        int baseDelay = AppSettings.Current.WindowSwitchBufferBase,
            jitterRange = AppSettings.Current.InputJitterRange,
            totalDelay = HumanoidRandom.NextDelay(baseDelay, jitterRange);

        try
        {
            await Task.Delay(totalDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 執行視窗切換。
        await _windowFocusManager.RestorePreviousWindowAsync(cancellationToken);

        // 切換完成後的震動。
        _ = FeedbackService.VibrateAsync(controller, VibrationPatterns.ReturnSuccess, cancellationToken);
    }

    /// <summary>
    /// 檢查是否需要等待按鍵放開
    /// </summary>
    /// <param name="controller">控制器</param>
    /// <returns>是否需要等待</returns>
    private static bool ShouldWaitForKeyRelease(IGamepadController? controller)
    {
        if (controller == null)
        {
            return false;
        }

        return controller.IsBackHeld ||
            controller.IsLeftShoulderHeld ||
            controller.IsRightShoulderHeld ||
            controller.IsBHeld;
    }
}