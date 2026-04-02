namespace InputBox.Core.Input;

/// <summary>
/// 搖桿死區遲滯計算輔助器：集中處理 Enter／Exit 閾值方向判定
/// </summary>
internal static class GamepadDeadzoneHysteresis
{
    /// <summary>
    /// 依據整數軸值與前一幀方向，回傳方向（-1／0／1）
    /// </summary>
    /// <param name="axisValue">目前軸值。</param>
    /// <param name="wasNegative">前一幀是否為負向。</param>
    /// <param name="wasPositive">前一幀是否為正向。</param>
    /// <param name="enterThreshold">進入閾值。</param>
    /// <param name="exitThreshold">退出閾值。</param>
    /// <returns>負向為 -1、無方向為 0、正向為 1。</returns>
    public static int ResolveDirection(
        int axisValue,
        bool wasNegative,
        bool wasPositive,
        int enterThreshold,
        int exitThreshold)
    {
        int thresholdNegative = wasNegative ?
            exitThreshold :
            enterThreshold;

        int thresholdPositive = wasPositive ?
            exitThreshold :
            enterThreshold;

        if (axisValue < -thresholdNegative)
        {
            return -1;
        }

        if (axisValue > thresholdPositive)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// 依據浮點軸值與前一幀方向，回傳方向（-1／0／1）
    /// </summary>
    /// <param name="axisValue">目前軸值。</param>
    /// <param name="wasNegative">前一幀是否為負向。</param>
    /// <param name="wasPositive">前一幀是否為正向。</param>
    /// <param name="enterThreshold">進入閾值。</param>
    /// <param name="exitThreshold">退出閾值。</param>
    /// <returns>負向為 -1、無方向為 0、正向為 1。</returns>
    public static int ResolveDirection(
        float axisValue,
        bool wasNegative,
        bool wasPositive,
        float enterThreshold,
        float exitThreshold)
    {
        float thresholdNegative = wasNegative ?
            exitThreshold :
            enterThreshold;

        float thresholdPositive = wasPositive ?
            exitThreshold :
            enterThreshold;

        if (axisValue < -thresholdNegative)
        {
            return -1;
        }

        if (axisValue > thresholdPositive)
        {
            return 1;
        }

        return 0;
    }
}