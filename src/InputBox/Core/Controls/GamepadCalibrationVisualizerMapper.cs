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
    public static float ClampNormalized(float value) => Math.Clamp(value, -1.0f, 1.0f);

    /// <summary>
    /// 將死區值換算成相對半徑（0.0 ~ 1.0）。
    /// </summary>
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
    public static PointF MapToCanvas(RectangleF bounds, float normalizedX, float normalizedY)
    {
        float x = ClampNormalized(normalizedX),
            y = ClampNormalized(normalizedY);

        float px = bounds.Left + bounds.Width * ((x + 1f) * 0.5f),
            py = bounds.Top + bounds.Height * (1f - ((y + 1f) * 0.5f));

        return new PointF(px, py);
    }
}
