using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using System.Diagnostics;

namespace InputBox.Core.Feedback;

/// <summary>
/// 輸入邊界與驗證失敗時共用的視覺閃爍警示：警示色、每一幀的前景／背景配色，以及光敏安全的動畫節奏
/// </summary>
/// <remarks>
/// <para>主視窗、數值輸入與片語編輯三處共用此實作，確保 1Hz 頻率上限、動畫偏好退回與 WCAG 文字對比規則只維護一份。</para>
/// <para>套用目標控制項、狀態旗標與結束後的視覺還原仍由各呼叫端負責。</para>
/// </remarks>
internal static class FlashAlertAnimator
{
    /// <summary>
    /// 關閉動畫時以單次長脈衝維持警示的時間（毫秒），讓低視能使用者有足夠時間感知狀態。
    /// </summary>
    private const int StaticPulseDurationMs = 800;

    /// <summary>
    /// WCAG 相對亮度切換閾值：背景亮度高於此值時改用黑色文字，否則使用白色文字。
    /// </summary>
    /// <remarks>
    /// 取黑白文字對比相等的交叉點（L≈0.1791），避免以 YUV≈128 近似在切換帶（intensity≈0.75）
    /// 讓文字對比跌破 AA；精確切換後全程 ≥4.64:1，14f 粗體大型文字全程 ≥4.5:1。
    /// </remarks>
    internal const float ForegroundLuminanceThreshold = 0.1791f;

    /// <summary>
    /// 取得警示色
    /// </summary>
    /// <remarks>
    /// 選色對齊反轉後的控制項背景：淺色模式（黑底）使用 DarkOrange（8.3:1）；深色模式（白底）使用 Firebrick（5.8:1）；
    /// 高對比模式一律使用系統醒目提示色。
    /// </remarks>
    /// <param name="isDark">目標控制項目前是否為深色模式。</param>
    /// <param name="highContrast">系統是否啟用高對比模式。</param>
    /// <returns>警示色。</returns>
    public static Color GetAlertColor(bool isDark, bool highContrast)
    {
        return highContrast ?
            SystemColors.Highlight :
            (isDark ? Color.Firebrick : Color.DarkOrange);
    }

    /// <summary>
    /// 依閃爍強度計算單一幀的背景與前景色
    /// </summary>
    /// <remarks>
    /// 一般模式由純淨底色（深色用白、淺色用黑）線性插值至警示色，避免與高飽和焦點色插值產生髒濁色；
    /// 前景色依 sRGB 線性化後的相對亮度切換黑白。高對比模式不插值，強度過半即切換為警示配色。
    /// </remarks>
    /// <param name="intensity">閃爍強度（0 到 1）。</param>
    /// <param name="isDark">目標控制項目前是否為深色模式。</param>
    /// <param name="alertColor">由 <see cref="GetAlertColor"/> 取得的警示色。</param>
    /// <param name="highContrast">系統是否啟用高對比模式。</param>
    /// <returns>本幀的背景色與前景色。</returns>
    public static (Color Back, Color Fore) ComputeFrameColors(
        float intensity,
        bool isDark,
        Color alertColor,
        bool highContrast)
    {
        if (highContrast)
        {
            bool isAlert = intensity > 0.5f;

            return isAlert ?
                (alertColor, SystemColors.HighlightText) :
                (SystemColors.Window, SystemColors.WindowText);
        }

        Color pureBase = isDark ?
            Color.White :
            Color.Black;

        int r = (int)(pureBase.R + (alertColor.R - pureBase.R) * intensity),
            g = (int)(pureBase.G + (alertColor.G - pureBase.G) * intensity),
            b = (int)(pureBase.B + (alertColor.B - pureBase.B) * intensity);

        Color back = Color.FromArgb(255, r, g, b);

        Color fore = GetRelativeLuminance(back) > ForegroundLuminanceThreshold ?
            Color.Black :
            Color.White;

        return (back, fore);
    }

    /// <summary>
    /// 播放閃爍動畫：預設為 <see cref="AppSettings.PhotoSafeFrequencyMs"/> 週期的單次正弦脈衝；
    /// 系統關閉動畫效果或使用者停用動畫警示時，改為單次長脈衝
    /// </summary>
    /// <param name="owner">用於在 UI 執行緒套用每一幀的控制項。</param>
    /// <param name="applyFrame">套用指定強度（0 到 1）的委派，會在 UI 執行緒上呼叫。</param>
    /// <param name="cancellationToken">取消權杖；取消時擲出 <see cref="OperationCanceledException"/>。</param>
    /// <returns>動畫完成或取消時結束的工作。</returns>
    public static async Task RunAsync(
        Control owner,
        Action<float> applyFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(applyFrame);

        // 嚴格遵守光敏性癲癇防護與使用者偏好：
        // 系統層級關閉動畫效果（UIEffectsEnabled 為 false）或停用動畫警示時，不進行循環閃爍。
        if (!SystemInformation.UIEffectsEnabled ||
            !AppSettings.Current.EnableAnimatedVisualAlerts)
        {
            await owner.SafeInvokeAsync(() => applyFrame(1.0f));

            await Task.Delay(StaticPulseDurationMs, cancellationToken);

            return;
        }

        int totalDuration = AppSettings.PhotoSafeFrequencyMs;

        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(AppSettings.TargetFrameTimeMs));

        long startTime = Stopwatch.GetTimestamp();

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            double elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;

            if (elapsedMs >= totalDuration)
            {
                break;
            }

            float intensity = ComputeIntensity(elapsedMs, totalDuration);

            await owner.SafeInvokeAsync(() => applyFrame(intensity));
        }
    }

    /// <summary>
    /// 依經過時間計算正弦脈衝強度：從 0 開始、在週期中點達到 1，再回到 0
    /// </summary>
    /// <param name="elapsedMs">動畫開始後的經過時間（毫秒）。</param>
    /// <param name="periodMs">脈衝週期（毫秒）。</param>
    /// <returns>閃爍強度（0 到 1）。</returns>
    internal static float ComputeIntensity(double elapsedMs, double periodMs)
    {
        double angle = elapsedMs / periodMs * 2.0 * Math.PI - (Math.PI / 2.0);

        return (float)((Math.Sin(angle) + 1.0) / 2.0);
    }

    /// <summary>
    /// 計算色彩的 WCAG 相對亮度
    /// </summary>
    /// <param name="color">要計算的色彩。</param>
    /// <returns>相對亮度（0 到 1）。</returns>
    internal static float GetRelativeLuminance(Color color)
    {
        return 0.2126f * Linearize(color.R) +
            0.7152f * Linearize(color.G) +
            0.0722f * Linearize(color.B);

        static float Linearize(int channel)
        {
            float f = channel / 255f;

            return f <= 0.04045f ?
                f / 12.92f :
                MathF.Pow((f + 0.055f) / 1.055f, 2.4f);
        }
    }
}
