namespace InputBox.Core.Input;

/// <summary>
/// 可重用的方向映射保護工具：在 anti-stuck 或暫態重置後，
/// 暫時封鎖指定的左搖桿映射方向，直到觀察到足夠的中立幀再解除。
/// </summary>
[Flags]
internal enum MappedGamepadDirection
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Up = 1 << 2,
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
    public static bool IsSuppressed(
        MappedGamepadDirection suppressedDirections,
        MappedGamepadDirection direction)
    {
        return direction != MappedGamepadDirection.None &&
            (suppressedDirections & direction) != 0;
    }
}