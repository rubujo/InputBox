namespace InputBox.Core.Controls;

/// <summary>
/// 校準視覺化的座標與比例換算輔助器。
/// </summary>
internal static class GamepadCalibrationVisualizerMapper
{
    /// <summary>
    /// XInput 與死區設定的全尺度值。
    /// </summary>
    internal const float FullScale = 32768f;

    /// <summary>
    /// 將正規化座標限制在可視範圍內。
    /// </summary>
    /// <param name="value">原始正規化值。</param>
    /// <returns>限制在 -1.0 ~ 1.0 範圍內的值。</returns>
    public static float ClampNormalized(float value) => Math.Clamp(value, -1.0f, 1.0f);

    /// <summary>
    /// 將死區值換算成相對半徑（0.0 ~ 1.0）。
    /// </summary>
    /// <param name="deadzone">死區原始值。</param>
    /// <param name="fullScale">全尺度值，預設為 <see cref="FullScale"/>。</param>
    /// <returns>正規化後的死區半徑（0.0 ~ 1.0）。</returns>
    public static float CalculateDeadzoneRadius(int deadzone, float fullScale = FullScale)
    {
        if (fullScale <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(deadzone / fullScale, 0f, 1f);
    }

    /// <summary>
    /// 將正規化座標映射到畫布矩形上的像素位置。
    /// </summary>
    /// <param name="bounds">畫布的像素邊界矩形。</param>
    /// <param name="normalizedX">正規化 X 座標（-1.0 ~ 1.0）。</param>
    /// <param name="normalizedY">正規化 Y 座標（-1.0 ~ 1.0）。</param>
    /// <returns>對應的畫布像素座標。</returns>
    public static PointF MapToCanvas(RectangleF bounds, float normalizedX, float normalizedY)
    {
        float x = ClampNormalized(normalizedX),
            y = ClampNormalized(normalizedY);

        float px = bounds.Left + bounds.Width * ((x + 1f) * 0.5f),
            py = bounds.Top + bounds.Height * (1f - ((y + 1f) * 0.5f));

        return new PointF(px, py);
    }
}