using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using System.Media;

namespace InputBox.Core.Services;

/// <summary>
/// 視窗導航服務
/// </summary>
/// <remarks>
/// 負責處理視窗切換的流程控制，包含安全檢查、隨機延遲與執行切換。
/// </remarks>
/// <param name="windowFocusManager">視窗焦點管理器</param>
internal sealed class WindowNavigationService(WindowFocusService windowFocusManager)
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
    /// <param name="controller">目前的控制器實例（用於檢查按鍵狀態）。</param>
    /// <param name="announceErrorAction">若切換失敗時的廣播委派（選用）。</param>
    /// <param name="cancellationToken">取消權杖，視窗關閉時應一併傳入以中止等待。</param>
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

            FeedbackService.VibrateAsync(controller, VibrationPatterns.ActionFail, cancellationToken)
                .SafeFireAndForget();

            announceErrorAction?.Invoke(Resources.Strings.A11y_TargetWindowLost);

            return;
        }

        // 播放音效／震動，給予即時回饋。
        FeedbackService.PlaySound(SystemSounds.Exclamation);

        FeedbackService.VibrateAsync(controller, VibrationPatterns.ReturnStart, cancellationToken)
            .SafeFireAndForget();

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

        // 套用視窗切換前的固定緩衝，確保前一個視窗有足夠時間恢復焦點。
        int totalDelay = AppSettings.Current.WindowSwitchBufferBase;

        try
        {
            await Task.Delay(totalDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 執行視窗切換。
        // 使用本次流程一開始的 target 快照，避免背景追蹤在等待期間覆寫目標。
        bool restored = await WindowFocusService.RestoreWindowAsync(targetHwnd, cancellationToken);

        if (!restored)
        {
            // 若實際切換失敗，回報與前置檢查一致的失敗訊息，避免「播報返回中但未切走」的假成功體驗。
            FeedbackService.PlaySound(SystemSounds.Exclamation);

            FeedbackService.VibrateAsync(controller, VibrationPatterns.ActionFail, cancellationToken)
                .SafeFireAndForget();

            announceErrorAction?.Invoke(Resources.Strings.A11y_TargetWindowLost);

            return;
        }

        // 切換完成後的震動。
        FeedbackService.VibrateAsync(controller, VibrationPatterns.ReturnSuccess, cancellationToken)
            .SafeFireAndForget();
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