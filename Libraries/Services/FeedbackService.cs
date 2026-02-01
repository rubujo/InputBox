using InputBox.Libraries.Configuration;
using InputBox.Libraries.Feedback;
using InputBox.Libraries.Input;
using System.Media;

namespace InputBox.Libraries.Services;

/// <summary>
/// 回饋服務
/// </summary>
/// <remarks>
/// 負責統一處理應用程式的音效播放與控制器震動邏輯。
/// </remarks>
internal class FeedbackService
{
    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="controller">控制器實例（允許為 null）</param>
    /// <param name="profile">震動設定檔</param>
    /// <returns>Task</returns>
    public Task VibrateAsync(GamepadController? controller, VibrationProfile profile)
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
#pragma warning disable CA1822 // 將成員標記為靜態
    public Task VibrateAsync(GamepadController? controller, ushort strength, int milliseconds)
#pragma warning restore CA1822 // 將成員標記為靜態
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

        // 數值邊界防護（Clamping）。
        ushort finalStrength = (ushort)Math.Clamp(calculatedStrength, 0, ushort.MaxValue);

        // 如果計算後太弱變成 0，也不用開啟了。
        if (finalStrength == 0)
        {
            return Task.CompletedTask;
        }

        // 傳送最終強度給控制器。
        return controller.VibrateAsync(finalStrength, milliseconds);
    }

    /// <summary>
    /// 播放系統音效
    /// </summary>
    /// <param name="sound">SystemSound</param>
#pragma warning disable CA1822 // 將成員標記為靜態
    public void PlaySound(SystemSound sound)
#pragma warning restore CA1822 // 將成員標記為靜態
    {
        sound?.Play();
    }
}