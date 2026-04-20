namespace InputBox.Core.Input;

/// <summary>
/// 可重用的方向映射保護工具：在 anti-stuck 或暫態重置後，
/// 暫時封鎖指定的左搖桿映射方向，直到觀察到足夠的中立幀再解除。
/// </summary>
[Flags]
internal enum MappedGamepadDirection
{
    /// <summary>
    /// 無方向封鎖。
    /// </summary>
    None = 0,
    /// <summary>
    /// 左方向封鎖。
    /// </summary>
    Left = 1 << 0,
    /// <summary>
    /// 右方向封鎖。
    /// </summary>
    Right = 1 << 1,
    /// <summary>
    /// 上方向封鎖。
    /// </summary>
    Up = 1 << 2,
    /// <summary>
    /// 下方向封鎖。
    /// </summary>
    Down = 1 << 3,
}

/// <summary>
/// 控制映射方向的短暫封鎖與解除節奏，避免幽靈方向在放開後立即重入。
/// </summary>
internal static class GamepadMappedDirectionGuard
{
    /// <summary>
    /// 啟用指定方向的保護封鎖，並重設中立/冷卻計數。
    /// </summary>
    /// <param name="suppressedDirections">目前封鎖方向旗標（以 ref 傳入並更新）。</param>
    /// <param name="neutralFrameCount">中立幀計數器（以 ref 傳入並重設為 0）。</param>
    /// <param name="remainingCooldownFrames">剩餘冷卻幀數（以 ref 傳入並設為 cooldownFrames）。</param>
    /// <param name="direction">要封鎖的方向旗標。</param>
    /// <param name="cooldownFrames">初始冷卻幀數；預設 12 幀。</param>
    public static void Enable(
        ref MappedGamepadDirection suppressedDirections,
        ref int neutralFrameCount,
        ref int remainingCooldownFrames,
        MappedGamepadDirection direction,
        int cooldownFrames = 12)
    {
        if (direction == MappedGamepadDirection.None)
        {
            return;
        }

        suppressedDirections |= direction;
        neutralFrameCount = 0;
        remainingCooldownFrames = Math.Max(0, cooldownFrames);
    }

    /// <summary>
    /// 更新保護狀態；回傳 true 代表本次已解除全部封鎖。
    /// </summary>
    /// <param name="suppressedDirections">目前封鎖方向旗標（以 ref 傳入並可能清空）。</param>
    /// <param name="neutralFrameCount">中立幀累積計數器（以 ref 傳入並更新）。</param>
    /// <param name="remainingCooldownFrames">剩餘冷卻幀數（以 ref 傳入並遞減）。</param>
    /// <param name="isStickNeutral">本幀搖桿是否位於中立區。</param>
    /// <param name="neutralFramesRequired">解除封鎖所需的最低連續中立幀數；預設 6 幀。</param>
    /// <returns>若本次呼叫解除了全部封鎖方向則為 true；封鎖仍持續中則為 false。</returns>
    public static bool Update(
        ref MappedGamepadDirection suppressedDirections,
        ref int neutralFrameCount,
        ref int remainingCooldownFrames,
        bool isStickNeutral,
        int neutralFramesRequired = 6)
    {
        if (suppressedDirections == MappedGamepadDirection.None)
        {
            neutralFrameCount = 0;
            remainingCooldownFrames = 0;

            return false;
        }

        if (remainingCooldownFrames > 0)
        {
            remainingCooldownFrames--;

            return false;
        }

        neutralFrameCount = isStickNeutral ?
            neutralFrameCount + 1 :
            0;

        if (neutralFrameCount < Math.Max(1, neutralFramesRequired))
        {
            return false;
        }

        suppressedDirections = MappedGamepadDirection.None;
        neutralFrameCount = 0;
        remainingCooldownFrames = 0;

        return true;
    }

    /// <summary>
    /// 檢查指定方向目前是否處於封鎖中。
    /// </summary>
    /// <param name="suppressedDirections">目前封鎖方向旗標。</param>
    /// <param name="direction">要查詢的方向旗標。</param>
    /// <returns>若指定方向在封鎖旗標中則為 true，否則為 false。</returns>
    public static bool IsSuppressed(
        MappedGamepadDirection suppressedDirections,
        MappedGamepadDirection direction)
    {
        return direction != MappedGamepadDirection.None &&
            (suppressedDirections & direction) != 0;
    }
}