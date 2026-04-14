using InputBox.Core.Configuration;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Media;

namespace InputBox.Core.Services;

/// <summary>
/// 回饋服務
/// </summary>
/// <remarks>
/// 負責統一處理應用程式的音效播放與控制器震動邏輯。
/// </remarks>
internal static class FeedbackService
{
    /// <summary>
    /// 追蹤所有活躍的控制器實例（用於緊急停止）
    /// </summary>
    private static readonly ConcurrentDictionary<IGamepadController, byte> ActiveControllers = new();

    /// <summary>
    /// 註冊控制器實例以供追蹤
    /// </summary>
    /// <param name="controller">IGamepadController</param>
    public static void RegisterController(IGamepadController controller)
    {
        if (controller == null)
        {
            return;
        }

        ActiveControllers.TryAdd(controller, 0);
    }

    /// <summary>
    /// 取消註冊控制器實例
    /// </summary>
    /// <param name="controller">IGamepadController</param>
    public static void UnregisterController(IGamepadController controller)
    {
        if (controller == null)
        {
            return;
        }

        ActiveControllers.TryRemove(controller, out _);
    }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="controller">控制器實例（允許為 null）</param>
    /// <param name="profile">震動設定檔</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task</returns>
    public static Task VibrateAsync(
        IGamepadController? controller,
        VibrationProfile profile,
        CancellationToken ct = default)
    {
        VibrationPriority priority = ResolvePriority(profile);

        return VibrateAsync(
            controller,
            profile.Strength,
            profile.Duration,
            priority,
            ct);
    }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="controller">控制器實例（允許為 null）</param>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">毫秒</param>
    /// <param name="priority">震動優先級</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task</returns>
    public static async Task VibrateAsync(
        IGamepadController? controller,
        ushort strength,
        int milliseconds,
        VibrationPriority priority = VibrationPriority.Normal,
        CancellationToken ct = default)
    {
#if DEBUG
        bool enableDebugDiagnostics =
            AppSettings.Current.EnableVibration &&
            AppSettings.Current.VibrationIntensity > 0f;
#endif

        // 讀取設定檔開關。
        if (!AppSettings.Current.EnableVibration)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=disabled reqStrength={strength} reqMs={milliseconds} priority={priority}");
            }
#endif
            return;
        }

        // 若控制器未連接或未初始化，直接結束。
        if (controller == null)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=no-controller reqStrength={strength} reqMs={milliseconds} priority={priority}");
            }
#endif
            return;
        }

        // 應用全域倍率。
        float multiplier = VibrationPatterns.GlobalIntensityMultiplier;

        // 如果倍率是 0，直接視為不震動。
        if (multiplier <= 0f)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=zero-intensity reqStrength={strength} reqMs={milliseconds} priority={priority} multiplier={multiplier:F2}");
            }
#endif
            return;
        }

        // 計算最終強度：原始強度 * 倍率。
        float calculatedStrength = strength * multiplier;

        // 數值邊界防護。
        ushort finalStrength = (ushort)Math.Clamp(calculatedStrength, ushort.MinValue, ushort.MaxValue);

        // 如果計算後太弱變成 0，也不用開啟了。
        if (finalStrength == 0)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=clamped-zero reqStrength={strength} reqMs={milliseconds} priority={priority} multiplier={multiplier:F2}");
            }
#endif
            return;
        }

#if DEBUG
        if (enableDebugDiagnostics)
        {
            LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=dispatch controller={controller.GetType().Name} reqStrength={strength} reqMs={milliseconds} finalStrength={finalStrength} priority={priority} multiplier={multiplier:F2}");
        }
#endif

        try
        {
            // 傳送震動指令。
            // 由於控制器實作（如 XInputGamepadController）內部已經具備完備的世代管理（Token）與自動停止邏輯，
            // Service 層級不應再介入細節控制，以免在多控制器環境下產生競態干擾。
            await controller.VibrateAsync(finalStrength, milliseconds, priority, ct);
        }
        catch (OperationCanceledException)
        {
            // 正常取消。
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=cancelled controller={controller.GetType().Name} priority={priority}");
            }
#endif
        }
        catch (Exception ex)
        {
            // 忽略個別控制器的震動失敗。
            Debug.WriteLine($"[震動] 控制器震動失敗（已忽略）：{ex.Message}");
        }
    }

    /// <summary>
    /// 強制停止控制器的所有震動
    /// </summary>
    /// <param name="controller">控制器實例</param>
    /// <returns>Task</returns>
    public static Task StopAllVibrationsAsync(IGamepadController? controller)
    {
        if (controller == null)
        {
            return Task.CompletedTask;
        }

        // 直接發送停止指令。
        return controller.VibrateAsync(0, 0, VibrationPriority.Critical);
    }

    /// <summary>
    /// 依震動設定檔映射優先級，讓高頻提示先被節流，關鍵 A11y 事件優先保留。
    /// </summary>
    /// <param name="profile">震動設定檔。</param>
    /// <returns>對應的震動優先級。</returns>
    private static VibrationPriority ResolvePriority(VibrationProfile profile)
    {
        if (profile == VibrationPatterns.CursorMove)
        {
            return VibrationPriority.Ambient;
        }

        if (profile == VibrationPatterns.ActionFail ||
            profile == VibrationPatterns.ControllerConnected)
        {
            return VibrationPriority.Critical;
        }

        return VibrationPriority.Normal;
    }

    /// <summary>
    /// 播放系統音效
    /// </summary>
    /// <param name="sound">SystemSound</param>
    public static void PlaySound(SystemSound sound)
    {
        sound?.Play();
    }

    /// <summary>
    /// 緊急嘗試停止所有可能的控制器震動（用於崩潰處理情境）
    /// </summary>
    public static void EmergencyStopAllActiveControllers()
    {
        try
        {
            // 1. 嘗試清理 XInput（最常見的震動殘留來源）。
            for (uint i = 0; i < AppSettings.XInputMaxControllers; i++)
            {
                XInput.XInputVibration stopVibration = default;

                XInput.XInputSetState(i, in stopVibration);
            }

            // 2. 遍歷目前所有活躍的控制器實例，強制發送停止震動指令。
            foreach (IGamepadController controller in ActiveControllers.Keys)
            {
                try
                {
                    // 使用同步方式發送震動停止指令，確保在應用程式關閉前完成。
                    // 注意：這裡不能使用非同步 Task，因為程式可能正在關閉中。
                    // XInput 已由上方處理，此處主要針對 GameInput 與其原生 COM。
                    controller.StopVibration();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"控制器停止震動失敗，已忽略：{ex.Message}");
                }
            }

            ActiveControllers.Clear();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"緊急清理發生錯誤，已忽略：{ex.Message}");
        }
    }
}