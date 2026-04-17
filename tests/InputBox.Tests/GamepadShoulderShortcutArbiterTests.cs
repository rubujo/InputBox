using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證肩鍵捷徑仲裁器在單按、連發與組合鍵情境下的手感判定，避免 LB／RB 的翻頁、單字跳轉與雙肩鍵組合互相干擾。
/// </summary>
public sealed class GamepadShoulderShortcutArbiterTests
{
    [Fact]
    public void TryConsumeTapOnRelease_WhenSingleTapWasArmed_EmitsSinglePageAction()
    {
        GamepadShoulderShortcutArbiter arbiter = new();

        arbiter.ArmTap(direction: -1);

        Assert.True(arbiter.TryConsumeTapOnRelease(direction: -1, isLeftStillHeld: false, isRightStillHeld: false));
    }

    [Fact]
    public void TryConsumeTapOnRelease_WhenRepeatAlreadyConsumed_DoesNotEmitExtraTap()
    {
        GamepadShoulderShortcutArbiter arbiter = new();

        arbiter.ArmTap(direction: -1);
        arbiter.MarkRepeatConsumed(direction: -1);

        Assert.False(arbiter.TryConsumeTapOnRelease(direction: -1, isLeftStillHeld: false, isRightStillHeld: false));
    }

    [Fact]
    public void TryConsumeTapOnRelease_WhenModifierWasUsed_DoesNotEmitTapOnRelease()
    {
        GamepadShoulderShortcutArbiter arbiter = new();

        arbiter.ArmTap(direction: +1);
        arbiter.MarkModifierUsed();

        Assert.False(arbiter.TryConsumeTapOnRelease(direction: +1, isLeftStillHeld: false, isRightStillHeld: false));
    }

    [Fact]
    public void ReserveDualShoulderCombo_SuppressesPagingUntilBothShouldersAreReleased()
    {
        GamepadShoulderShortcutArbiter arbiter = new();

        arbiter.ArmTap(direction: -1);
        arbiter.ArmTap(direction: +1);
        arbiter.ReserveDualShoulderCombo();

        Assert.True(arbiter.ShouldSuppressPaging(isLeftHeld: true, isRightHeld: true));
        Assert.False(arbiter.TryConsumeTapOnRelease(direction: -1, isLeftStillHeld: false, isRightStillHeld: true));
        Assert.False(arbiter.TryConsumeTapOnRelease(direction: +1, isLeftStillHeld: false, isRightStillHeld: false));
        Assert.False(arbiter.ShouldSuppressPaging(isLeftHeld: false, isRightHeld: false));
    }
}
