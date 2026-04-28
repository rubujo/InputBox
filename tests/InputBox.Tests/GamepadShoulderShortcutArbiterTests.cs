using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證肩鍵捷徑仲裁器在單按、連發與組合鍵情境下的手感判定，避免 LB／RB 的翻頁、單字跳轉與雙肩鍵組合互相干擾。
/// </summary>
public sealed class GamepadShoulderShortcutArbiterTests
{
    /// <summary>
    /// 已武裝的單次肩鍵點按在放開時應輸出一次翻頁動作。
    /// </summary>
    [Fact]
    public void TryConsumeTapOnRelease_WhenSingleTapWasArmed_EmitsSinglePageAction()
    {
        GamepadShoulderShortcutArbiter arbiter = new();

        arbiter.ArmTap(direction: -1);

        Assert.True(arbiter.TryConsumeTapOnRelease(direction: -1, isLeftStillHeld: false, isRightStillHeld: false));
    }

    /// <summary>
    /// 肩鍵長按連發已消費動作後，放開時不應再額外輸出單次翻頁。
    /// </summary>
    [Fact]
    public void TryConsumeTapOnRelease_WhenRepeatAlreadyConsumed_DoesNotEmitExtraTap()
    {
        GamepadShoulderShortcutArbiter arbiter = new();

        arbiter.ArmTap(direction: -1);
        arbiter.MarkRepeatConsumed(direction: -1);

        Assert.False(arbiter.TryConsumeTapOnRelease(direction: -1, isLeftStillHeld: false, isRightStillHeld: false));
    }

    /// <summary>
    /// 肩鍵已被用作修飾鍵時，放開時不應誤觸發單次翻頁。
    /// </summary>
    [Fact]
    public void TryConsumeTapOnRelease_WhenModifierWasUsed_DoesNotEmitTapOnRelease()
    {
        GamepadShoulderShortcutArbiter arbiter = new();

        arbiter.ArmTap(direction: +1);
        arbiter.MarkModifierUsed();

        Assert.False(arbiter.TryConsumeTapOnRelease(direction: +1, isLeftStillHeld: false, isRightStillHeld: false));
    }

    /// <summary>
    /// 雙肩鍵組合保留期間應抑制翻頁，直到兩側肩鍵都放開後才解除抑制。
    /// </summary>
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
