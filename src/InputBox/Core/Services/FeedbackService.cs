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

        return VibrateAsync(controller, profile, priority, ct);
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
    public static Task VibrateAsync(
        IGamepadController? controller,
        ushort strength,
        int milliseconds,
        VibrationPriority priority = VibrationPriority.Normal,
        CancellationToken ct = default)
    {
        return VibrateAsync(
            controller,
            new VibrationProfile(strength, milliseconds),
            priority,
            ct);
    }

    /// <summary>
    /// 讓控制器依指定的多馬達震動設定震動，並套用既有全域強度倍率。
    /// </summary>
    /// <param name="controller">控制器實例（允許為 null）。</param>
    /// <param name="profile">震動設定檔。</param>
    /// <param name="priority">震動優先級。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>Task。</returns>
    public static async Task VibrateAsync(
        IGamepadController? controller,
        VibrationProfile profile,
        VibrationPriority priority,
        CancellationToken ct = default)
    {
#if DEBUG
        bool enableDebugDiagnostics =
            AppSettings.Current.EnableVibration &&
            AppSettings.Current.VibrationIntensity > 0f;
#endif

        if (!AppSettings.Current.EnableVibration)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=disabled reqStrength={profile.Strength} reqMs={profile.Duration} priority={priority}");
            }
#endif
            return;
        }

        if (controller == null)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=no-controller reqStrength={profile.Strength} reqMs={profile.Duration} priority={priority}");
            }
#endif
            return;
        }

        float multiplier = VibrationPatterns.GlobalIntensityMultiplier;

        if (multiplier <= 0f)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=zero-intensity reqStrength={profile.Strength} reqMs={profile.Duration} priority={priority} multiplier={multiplier:F2}");
            }
#endif
            return;
        }

        VibrationProfile adjustedProfile = profile.ApplyIntensityMultiplier(multiplier);

        if (adjustedProfile.GetPeakMotorStrength() == 0)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=skip reason=clamped-zero reqStrength={profile.Strength} reqMs={profile.Duration} priority={priority} multiplier={multiplier:F2}");
            }
#endif
            return;
        }

#if DEBUG
        if (enableDebugDiagnostics)
        {
            LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=dispatch controller={controller.GetType().Name} reqStrength={profile.Strength} reqMs={profile.Duration} finalStrength={adjustedProfile.Strength} priority={priority} multiplier={multiplier:F2} low={adjustedProfile.LowFrequencyMotorScale:F2} high={adjustedProfile.HighFrequencyMotorScale:F2} lt={adjustedProfile.LeftTriggerMotorScale:F2} rt={adjustedProfile.RightTriggerMotorScale:F2}");
        }
#endif

        try
        {
            await controller.VibrateAsync(adjustedProfile, priority, ct);
        }
        catch (OperationCanceledException)
        {
#if DEBUG
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo($"VibrationDiag source=FeedbackService stage=cancelled controller={controller.GetType().Name} priority={priority}");
            }
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[震動] 控制器震動失敗（已忽略）：{ex.Message}");
        }
    }

    /// <summary>
    /// 依序播放多段式震動序列，供具備四馬達能力的控制器呈現更細緻的觸覺層次。
    /// </summary>
    /// <param name="controller">控制器實例（允許為 null）。</param>
    /// <param name="sequence">要播放的震動步驟集合。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>Task。</returns>
    public static async Task VibrateSequenceAsync(
        IGamepadController? controller,
        IReadOnlyList<VibrationSequenceStep> sequence,
        CancellationToken ct = default)
    {
        if (sequence == null ||
            sequence.Count == 0)
        {
            return;
        }

        try
        {
            foreach (VibrationSequenceStep step in sequence)
            {
                ct.ThrowIfCancellationRequested();
                await VibrateAsync(controller, step.Profile, ResolvePriority(step.Profile), ct).ConfigureAwait(false);

                if (step.PauseAfterMs > 0)
                {
                    await Task.Delay(step.PauseAfterMs, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 視窗關閉或輸入情境改變時允許無聲取消。
        }
    }

    /// <summary>
    /// 依語意、方向與控制器能力播放最佳化的導覽觸覺序列。
    /// </summary>
    /// <param name="controller">控制器實例（允許為 null）。</param>
    /// <param name="semantic">觸覺語意。</param>
    /// <param name="direction">方向：負值代表向左／向前，非負值代表向右／向後。</param>
    /// <param name="context">目前 UI 情境。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>Task。</returns>
    public static Task VibrateNavigationAsync(
        IGamepadController? controller,
        VibrationSemantic semantic,
        int direction,
        VibrationContext context = VibrationContext.General,
        CancellationToken ct = default)
    {
        VibrationMotorSupport motorSupport = controller?.VibrationMotorSupport ?? VibrationMotorSupport.None;
        IReadOnlyList<VibrationSequenceStep> sequence = VibrationPatterns.GetNavigationSequence(semantic, direction, context, motorSupport);

        return sequence.Count == 1 ?
            VibrateAsync(controller, sequence[0].Profile, ResolvePriority(sequence[0].Profile), ct) :
            VibrateSequenceAsync(controller, sequence, ct);
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
        if (profile == VibrationPatterns.CursorMove ||
            (profile.Strength <= 20000 && profile.Duration <= 60))
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
    /// 為右搖桿文字選取播放輕量同步音效，只使用 Windows 內建系統音避免外部素材與額外刺激。
    /// </summary>
    /// <param name="wordGranularity">是否為單字粒度選取。</param>
    /// <param name="burstLevel">快速連續選取的 burst 等級。</param>
    public static void PlaySelectionCue(bool wordGranularity, int burstLevel)
    {
        try
        {
            SystemSound sound = wordGranularity || burstLevel <= 0 ?
                SystemSounds.Asterisk :
                SystemSounds.Beep;

            PlaySound(sound);
        }
        catch
        {
            // 若系統音效不可用則靜默略過，不影響主要操作流程。
        }
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