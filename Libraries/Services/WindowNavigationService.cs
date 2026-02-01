using InputBox.Libraries.Configuration;
using InputBox.Libraries.Feedback;
using InputBox.Libraries.Input;
using InputBox.Libraries.Manager;
using System.Media;

namespace InputBox.Libraries.Services;

/// <summary>
/// 視窗導航服務
/// </summary>
/// <remarks>
/// 負責處理視窗切換的流程控制，包含安全檢查、隨機延遲與執行切換。
/// </remarks>
/// <param name="windowFocusManager">視窗焦點管理器</param>
/// <param name="feedbackService">回饋服務</param>
internal class WindowNavigationService(
    WindowFocusManager windowFocusManager,
    FeedbackService feedbackService)
{
    /// <summary>
    /// WindowFocusManager
    /// </summary>
    private readonly WindowFocusManager _windowFocusManager = windowFocusManager;

    /// <summary>
    /// FeedbackService
    /// </summary>
    private readonly FeedbackService _feedbackService = feedbackService;

    /// <summary>
    /// 用於產生隨機延遲，使操作更自然
    /// </summary>
    private readonly Random _random = new();

    /// <summary>
    /// 按鍵放開檢查頻率
    /// </summary>
    private const int Delay_KeyReleaseCheck = 15;

    /// <summary>
    /// 等待按鍵放開的超時上限
    /// </summary>
    private const int Timeout_KeyRelease = 2000;

    /// <summary>
    /// 執行返回前一個視窗的流程
    /// </summary>
    /// <param name="controller">目前的控制器實例（用於檢查按鍵狀態）</param>
    /// <returns>Task</returns>
    public async Task NavigateBackAsync(GamepadController? controller)
    {
        // 播放音效／震動，給予即時回饋，讓使用者知道「收到了」。
        _feedbackService.PlaySound(SystemSounds.Exclamation);

        // 這裡震動可以短一點，因為還沒真正切換。
        _ = _feedbackService.VibrateAsync(controller, VibrationPatterns.ReturnStart);

        using CancellationTokenSource ctsTimeout = new(Timeout_KeyRelease);

        // 等待所有「危險按鍵」被放開，檢查頻率 10ms ~ 16ms（約 60fps）即可。
        // 將判斷邏輯封裝在此，避免 UI 層過度依賴控制器細節。
        while (ShouldWaitForKeyRelease(controller) &&
            !ctsTimeout.IsCancellationRequested)
        {
            await Task.Delay(Delay_KeyReleaseCheck);
        }

        // 讀取設定 + 隨機抖動。
        int baseDelay = AppSettings.Current.WindowSwitchBufferBase,
            jitter = _random.Next(0, AppSettings.Current.InputJitterRange);

        await Task.Delay(baseDelay + jitter);

        // 執行視窗切換。
        await _windowFocusManager.RestorePreviousWindowAsync();

        // 切換完成後的震動。
        _ = _feedbackService.VibrateAsync(controller, VibrationPatterns.ReturnSuccess);
    }

    /// <summary>
    /// 檢查是否需要等待按鍵放開
    /// </summary>
    /// <param name="controller">控制器</param>
    /// <returns>是否需要等待</returns>
    private static bool ShouldWaitForKeyRelease(GamepadController? controller)
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