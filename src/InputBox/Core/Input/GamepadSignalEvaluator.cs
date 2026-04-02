namespace InputBox.Core.Input;

/// <summary>
/// 控制器訊號活動／閒置判定輔助器
/// </summary>
internal static class GamepadSignalEvaluator
{
    /// <summary>
    /// 以整數軸值判斷是否存在明顯活動訊號（按鈕、板機或搖桿）
    /// </summary>
    /// <param name="hasButtons">是否有任一按鈕被按下。</param>
    /// <param name="leftTrigger">左板機值。</param>
    /// <param name="rightTrigger">右板機值。</param>
    /// <param name="leftThumbX">左搖桿 X 軸值。</param>
    /// <param name="leftThumbY">左搖桿 Y 軸值。</param>
    /// <param name="rightThumbX">右搖桿 X 軸值。</param>
    /// <param name="rightThumbY">右搖桿 Y 軸值。</param>
    /// <param name="triggerThreshold">板機活動閾值。</param>
    /// <param name="thumbThreshold">搖桿活動閾值。</param>
    /// <returns>若存在明顯活動訊號則回傳 true。</returns>
    public static bool IsActive(
        bool hasButtons,
        int leftTrigger,
        int rightTrigger,
        int leftThumbX,
        int leftThumbY,
        int rightThumbX,
        int rightThumbY,
        int triggerThreshold,
        int thumbThreshold)
    {
        return hasButtons ||
            leftTrigger > triggerThreshold ||
            rightTrigger > triggerThreshold ||
            Math.Abs(leftThumbX) > thumbThreshold ||
            Math.Abs(leftThumbY) > thumbThreshold ||
            Math.Abs(rightThumbX) > thumbThreshold ||
            Math.Abs(rightThumbY) > thumbThreshold;
    }

    /// <summary>
    /// 以浮點軸值判斷是否存在明顯活動訊號（按鈕、板機或搖桿）
    /// </summary>
    /// <param name="hasButtons">是否有任一按鈕被按下。</param>
    /// <param name="leftTrigger">左板機值。</param>
    /// <param name="rightTrigger">右板機值。</param>
    /// <param name="leftThumbX">左搖桿 X 軸值。</param>
    /// <param name="leftThumbY">左搖桿 Y 軸值。</param>
    /// <param name="rightThumbX">右搖桿 X 軸值。</param>
    /// <param name="rightThumbY">右搖桿 Y 軸值。</param>
    /// <param name="threshold">統一活動閾值。</param>
    /// <returns>若存在明顯活動訊號則回傳 true。</returns>
    public static bool IsActive(
        bool hasButtons,
        float leftTrigger,
        float rightTrigger,
        float leftThumbX,
        float leftThumbY,
        float rightThumbX,
        float rightThumbY,
        float threshold)
    {
        return hasButtons ||
            leftTrigger > threshold ||
            rightTrigger > threshold ||
            Math.Abs(leftThumbX) > threshold ||
            Math.Abs(leftThumbY) > threshold ||
            Math.Abs(rightThumbX) > threshold ||
            Math.Abs(rightThumbY) > threshold;
    }

    /// <summary>
    /// 以浮點軸值判斷是否處於閒置狀態（無按鈕且所有軸值低於閾值）
    /// </summary>
    /// <param name="hasButtons">是否有任一按鈕被按下。</param>
    /// <param name="leftTrigger">左板機值。</param>
    /// <param name="rightTrigger">右板機值。</param>
    /// <param name="leftThumbX">左搖桿 X 軸值。</param>
    /// <param name="leftThumbY">左搖桿 Y 軸值。</param>
    /// <param name="rightThumbX">右搖桿 X 軸值。</param>
    /// <param name="rightThumbY">右搖桿 Y 軸值。</param>
    /// <param name="threshold">閒置閾值。</param>
    /// <returns>若目前狀態可視為閒置則回傳 true。</returns>
    public static bool IsIdle(
        bool hasButtons,
        float leftTrigger,
        float rightTrigger,
        float leftThumbX,
        float leftThumbY,
        float rightThumbX,
        float rightThumbY,
        float threshold)
    {
        return !hasButtons &&
            leftTrigger < threshold &&
            rightTrigger < threshold &&
            Math.Abs(leftThumbX) < threshold &&
            Math.Abs(leftThumbY) < threshold &&
            Math.Abs(rightThumbX) < threshold &&
            Math.Abs(rightThumbY) < threshold;
    }
}