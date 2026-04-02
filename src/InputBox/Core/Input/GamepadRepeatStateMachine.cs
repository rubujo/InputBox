namespace InputBox.Core.Input;

/// <summary>
/// 控制器連發狀態機輔助：集中管理方向連發與持續按住連發的計時轉換
/// </summary>
internal static class GamepadRepeatStateMachine
{
    /// <summary>
    /// 推進方向鍵連發狀態；回傳 true 代表本幀應觸發 Repeat 事件
    /// </summary>
    /// <typeparam name="TDirection">方向型別（通常為 Enum）。</typeparam>
    /// <param name="currentDirection">本幀方向。</param>
    /// <param name="trackedDirection">追蹤中的方向狀態。</param>
    /// <param name="repeatCounter">目前連發計數器。</param>
    /// <param name="currentRepeatInterval">目前連發間隔。</param>
    /// <param name="initialDelayFrames">初始延遲幀數。</param>
    /// <param name="intervalFrames">重複間隔幀數。</param>
    /// <returns>若本幀應觸發 Repeat 事件則回傳 true。</returns>
    public static bool AdvanceDirectionRepeat<TDirection>(
        TDirection? currentDirection,
        ref TDirection? trackedDirection,
        ref int repeatCounter,
        ref int currentRepeatInterval,
        int initialDelayFrames,
        int intervalFrames)
        where TDirection : struct
    {
        if (currentDirection is null)
        {
            repeatCounter = 0;
            trackedDirection = null;
            currentRepeatInterval = 0;

            return false;
        }

        if (!trackedDirection.HasValue ||
            !trackedDirection.Value.Equals(currentDirection.Value))
        {
            repeatCounter = 0;
            trackedDirection = currentDirection;
            currentRepeatInterval = initialDelayFrames;

            return false;
        }

        repeatCounter++;

        if (repeatCounter < currentRepeatInterval)
        {
            return false;
        }

        repeatCounter = 0;
        currentRepeatInterval = intervalFrames;

        return true;
    }

    /// <summary>
    /// 推進持續按住連發狀態；回傳 true 代表本幀應觸發 Repeat 事件
    /// </summary>
    /// <param name="isHeld">目前是否處於按住狀態。</param>
    /// <param name="repeatCounter">目前連發計數器。</param>
    /// <param name="currentRepeatInterval">目前連發間隔。</param>
    /// <param name="initialDelayFrames">初始延遲幀數。</param>
    /// <param name="intervalFrames">重複間隔幀數。</param>
    /// <returns>若本幀應觸發 Repeat 事件則回傳 true。</returns>
    public static bool AdvanceHeldRepeat(
        bool isHeld,
        ref int repeatCounter,
        ref int currentRepeatInterval,
        int initialDelayFrames,
        int intervalFrames)
    {
        if (!isHeld)
        {
            repeatCounter = 0;
            currentRepeatInterval = 0;

            return false;
        }

        if (repeatCounter == 0 &&
            currentRepeatInterval == 0)
        {
            currentRepeatInterval = initialDelayFrames;
        }

        repeatCounter++;

        if (repeatCounter < currentRepeatInterval)
        {
            return false;
        }

        repeatCounter = 0;
        currentRepeatInterval = intervalFrames;

        return true;
    }
}