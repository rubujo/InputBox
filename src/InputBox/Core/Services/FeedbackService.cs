using System.Collections.Concurrent;
using System.Media;
using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;

namespace InputBox.Core.Services;

/// <summary>
/// 回饋服務
/// </summary>
/// <remarks>
/// 負責統一處理應用程式的音效播放與控制器震動邏輯。
/// </remarks>
internal class FeedbackService
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
    /// <returns>Task</returns>
    public static Task VibrateAsync(IGamepadController? controller, VibrationProfile profile)
    {
        return VibrateAsync(controller, profile.Strength, profile.Duration);
    }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="controller">控制器實例（允許為 null）</param>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">毫秒</param>
    /// <returns>Task</returns>
    public static Task VibrateAsync(IGamepadController? controller, ushort strength, int milliseconds)
    {
        // 讀取設定檔開關。
        if (!AppSettings.Current.EnableVibration)
        {
            return Task.CompletedTask;
        }

        // 若控制器未連接或未初始化，直接結束。
        if (controller == null)
        {
            return Task.CompletedTask;
        }

        // 應用全域倍率。
        float multiplier = VibrationPatterns.GlobalIntensityMultiplier;

        // 如果倍率是 0，直接視為不震動。
        if (multiplier <= 0f)
        {
            return Task.CompletedTask;
        }

        // 計算最終強度：原始強度 * 倍率。
        float calculatedStrength = strength * multiplier;

        // 數值邊界防護。
        ushort finalStrength = (ushort)Math.Clamp(calculatedStrength, ushort.MinValue, ushort.MaxValue);

        // 如果計算後太弱變成 0，也不用開啟了。
        if (finalStrength == 0)
        {
            return Task.CompletedTask;
        }

        // 傳送最終強度給控制器。
        return controller.VibrateAsync(finalStrength, milliseconds);
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

        // 強制傳送強度為 0 且持續時間極短的指令。
        return controller.VibrateAsync(0, 10);
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
            for (uint i = 0; i < 4; i++)
            {
                XInput.XInputVibration stopVibration = default;

                XInput.XInputSetState(i, in stopVibration);
            }

            // 2. 遍歷目前所有活躍的控制器實例，強制發送停止震動指令。
            foreach (IGamepadController controller in ActiveControllers.Keys)
            {
                try
                {
                    // 使用同步方式發送震動 0 的指令（如果該實作支援）。
                    // 注意：這裡不能使用非同步 Task，因為程式可能正在關閉中。
                    // XInput 已由上方處理，此處主要針對 GameInput 與其原生 COM。
                    controller.VibrateAsync(0, 0).SafeFireAndForget();
                }
                catch
                {
                    // 忽略個別控制器的操作失敗。
                }
            }

            ActiveControllers.Clear();
        }
        catch
        {
            // 忽略緊急清理時的任何錯誤。
        }
    }
}