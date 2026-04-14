using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證映射方向幽靈保護可套用到四個方向，並在搖桿真正回到中立後才解除封鎖。
/// </summary>
public sealed class GamepadMappedDirectionGuardTests
{
    /// <summary>
    /// 啟用全方向保護後，只應封鎖指定方向，其他方向仍可依需求個別判斷。
    /// </summary>
    [Fact]
    public void Enable_WhenSpecificDirectionsSuppressed_BlocksOnlyThoseDirections()
    {
        MappedGamepadDirection suppressed = MappedGamepadDirection.None;
        int neutralFrames = 0,
            cooldownFrames = 0;

        GamepadMappedDirectionGuard.Enable(
            ref suppressed,
            ref neutralFrames,
            ref cooldownFrames,
            MappedGamepadDirection.Left | MappedGamepadDirection.Up,
            cooldownFrames: 2);

        Assert.True(GamepadMappedDirectionGuard.IsSuppressed(suppressed, MappedGamepadDirection.Left));
        Assert.True(GamepadMappedDirectionGuard.IsSuppressed(suppressed, MappedGamepadDirection.Up));
        Assert.False(GamepadMappedDirectionGuard.IsSuppressed(suppressed, MappedGamepadDirection.Right));
        Assert.False(GamepadMappedDirectionGuard.IsSuppressed(suppressed, MappedGamepadDirection.Down));
    }

    /// <summary>
    /// 封鎖啟用後，必須先經過冷卻期與連續中立幀，才能解除方向保護，避免剛釋放就被噪聲重入。
    /// </summary>
    [Fact]
    public void Update_WhenStickReturnsNeutral_ReleasesSuppressionAfterRequiredFrames()
    {
        MappedGamepadDirection suppressed = MappedGamepadDirection.None;
        int neutralFrames = 0,
            cooldownFrames = 0;

        GamepadMappedDirectionGuard.Enable(
            ref suppressed,
            ref neutralFrames,
            ref cooldownFrames,
            MappedGamepadDirection.Right,
            cooldownFrames: 2);

        Assert.False(GamepadMappedDirectionGuard.Update(ref suppressed, ref neutralFrames, ref cooldownFrames, isStickNeutral: true, neutralFramesRequired: 2));
        Assert.NotEqual(MappedGamepadDirection.None, suppressed);

        Assert.False(GamepadMappedDirectionGuard.Update(ref suppressed, ref neutralFrames, ref cooldownFrames, isStickNeutral: true, neutralFramesRequired: 2));
        Assert.NotEqual(MappedGamepadDirection.None, suppressed);

        Assert.False(GamepadMappedDirectionGuard.Update(ref suppressed, ref neutralFrames, ref cooldownFrames, isStickNeutral: false, neutralFramesRequired: 2));
        Assert.NotEqual(MappedGamepadDirection.None, suppressed);

        Assert.False(GamepadMappedDirectionGuard.Update(ref suppressed, ref neutralFrames, ref cooldownFrames, isStickNeutral: true, neutralFramesRequired: 2));
        Assert.True(GamepadMappedDirectionGuard.IsSuppressed(suppressed, MappedGamepadDirection.Right));

        Assert.True(GamepadMappedDirectionGuard.Update(ref suppressed, ref neutralFrames, ref cooldownFrames, isStickNeutral: true, neutralFramesRequired: 2));
        Assert.Equal(MappedGamepadDirection.None, suppressed);
    }
}