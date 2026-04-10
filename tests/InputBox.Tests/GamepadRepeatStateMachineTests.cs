using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// <see cref="GamepadRepeatStateMachine"/> 單元測試
/// <para>涵蓋方向連發（AdvanceDirectionRepeat）與持續按住連發（AdvanceHeldRepeat）兩組方法的狀態轉換。</para>
/// </summary>
public class GamepadRepeatStateMachineTests
{
    // ── AdvanceDirectionRepeat ──────────────────────────────────────────

    /// <summary>
    /// 傳入 null 方向時，應回傳 false，並將追蹤方向、計數器與間隔全部重置為初始值。
    /// </summary>
    [Fact]
    public void AdvanceDirectionRepeat_NullDirection_ReturnsFalseAndResetsState()
    {
        int? tracked = 1;
        int counter = 5;
        int interval = 10;

        bool result = GamepadRepeatStateMachine.AdvanceDirectionRepeat<int>(
            currentDirection: null,
            trackedDirection: ref tracked,
            repeatCounter: ref counter,
            currentRepeatInterval: ref interval,
            initialDelayFrames: 20,
            intervalFrames: 3);

        Assert.False(result);
        Assert.Null(tracked);
        Assert.Equal(0, counter);
        Assert.Equal(0, interval);
    }

    /// <summary>
    /// 首次出現方向（previously null）時，應回傳 false，並將追蹤方向設為新方向、間隔設為初始延遲。
    /// </summary>
    [Fact]
    public void AdvanceDirectionRepeat_NewDirection_ReturnsFalseAndSetsInitialDelay()
    {
        int? tracked = null;
        int counter = 0;
        int interval = 0;

        bool result = GamepadRepeatStateMachine.AdvanceDirectionRepeat(
            currentDirection: (int?)1,
            trackedDirection: ref tracked,
            repeatCounter: ref counter,
            currentRepeatInterval: ref interval,
            initialDelayFrames: 20,
            intervalFrames: 3);

        Assert.False(result);
        Assert.Equal(1, tracked);
        Assert.Equal(0, counter);
        Assert.Equal(20, interval);
    }

    /// <summary>
    /// 方向在序列中途改變時，應重置計數器並以新方向重新開始初始延遲，且本幀回傳 false。
    /// </summary>
    [Fact]
    public void AdvanceDirectionRepeat_DirectionChanged_ResetsStateReturnssFalse()
    {
        int? tracked = 1;
        int counter = 10;
        int interval = 20;

        bool result = GamepadRepeatStateMachine.AdvanceDirectionRepeat(
            currentDirection: (int?)2,
            trackedDirection: ref tracked,
            repeatCounter: ref counter,
            currentRepeatInterval: ref interval,
            initialDelayFrames: 20,
            intervalFrames: 3);

        Assert.False(result);
        Assert.Equal(2, tracked);
        Assert.Equal(0, counter);
        Assert.Equal(20, interval);
    }

    /// <summary>
    /// 相同方向持續 19 幀（initialDelayFrames=20）時，應始終回傳 false（尚未達到觸發閾值）。
    /// </summary>
    [Fact]
    public void AdvanceDirectionRepeat_SameDirection_BeforeThreshold_ReturnsFalse()
    {
        int? tracked = 1;
        int counter = 0;
        int interval = 20;

        bool fired = false;

        for (int i = 0; i < 19; i++)
        {
            fired |= GamepadRepeatStateMachine.AdvanceDirectionRepeat(
                currentDirection: (int?)1,
                trackedDirection: ref tracked,
                repeatCounter: ref counter,
                currentRepeatInterval: ref interval,
                initialDelayFrames: 20,
                intervalFrames: 3);
        }

        Assert.False(fired);
    }

    /// <summary>
    /// 相同方向剛好達到 initialDelayFrames（20 幀）時，最後一幀應回傳 true，
    /// 且計數器重置為 0、間隔切換為 intervalFrames。
    /// </summary>
    [Fact]
    public void AdvanceDirectionRepeat_SameDirection_AtThreshold_ReturnsTrue()
    {
        int? tracked = 1;
        int counter = 0;
        int interval = 20;

        bool fired = false;

        for (int i = 0; i < 20; i++)
        {
            fired = GamepadRepeatStateMachine.AdvanceDirectionRepeat(
                currentDirection: (int?)1,
                trackedDirection: ref tracked,
                repeatCounter: ref counter,
                currentRepeatInterval: ref interval,
                initialDelayFrames: 20,
                intervalFrames: 3);
        }

        Assert.True(fired);
        Assert.Equal(0, counter);
        Assert.Equal(3, interval);
    }

    /// <summary>
    /// 初始延遲觸發後，再持續 intervalFrames（3 幀）應再次觸發連發事件。
    /// 驗證初始延遲只作用一次，後續以較快的 intervalFrames 頻率重複。
    /// </summary>
    [Fact]
    public void AdvanceDirectionRepeat_AfterFirstFire_RepeatFiresAtIntervalFrames()
    {
        int? tracked = 1;
        int counter = 0;
        int interval = 20;

        for (int i = 0; i < 20; i++)
        {
            GamepadRepeatStateMachine.AdvanceDirectionRepeat(
                currentDirection: (int?)1,
                trackedDirection: ref tracked,
                repeatCounter: ref counter,
                currentRepeatInterval: ref interval,
                initialDelayFrames: 20,
                intervalFrames: 3);
        }

        bool fired = false;

        for (int i = 0; i < 3; i++)
        {
            fired = GamepadRepeatStateMachine.AdvanceDirectionRepeat(
                currentDirection: (int?)1,
                trackedDirection: ref tracked,
                repeatCounter: ref counter,
                currentRepeatInterval: ref interval,
                initialDelayFrames: 20,
                intervalFrames: 3);
        }

        Assert.True(fired);
    }

    // ── AdvanceHeldRepeat ────────────────────────────────────────────────

    /// <summary>
    /// 未按住（isHeld=false）時，應回傳 false，並將計數器與間隔重置為 0。
    /// </summary>
    [Fact]
    public void AdvanceHeldRepeat_NotHeld_ReturnsFalseAndResetsState()
    {
        int counter = 5;
        int interval = 10;

        bool result = GamepadRepeatStateMachine.AdvanceHeldRepeat(
            isHeld: false,
            repeatCounter: ref counter,
            currentRepeatInterval: ref interval,
            initialDelayFrames: 20,
            intervalFrames: 3);

        Assert.False(result);
        Assert.Equal(0, counter);
        Assert.Equal(0, interval);
    }

    /// <summary>
    /// 第一幀按住（counter=0, interval=0）時，應初始化 interval=initialDelayFrames，
    /// 計數器遞增為 1，且回傳 false（尚未達到閾值）。
    /// </summary>
    [Fact]
    public void AdvanceHeldRepeat_FirstFrameHeld_SetsInitialDelayAndReturnsFalse()
    {
        int counter = 0;
        int interval = 0;

        bool result = GamepadRepeatStateMachine.AdvanceHeldRepeat(
            isHeld: true,
            repeatCounter: ref counter,
            currentRepeatInterval: ref interval,
            initialDelayFrames: 20,
            intervalFrames: 3);

        Assert.False(result);
        Assert.Equal(20, interval);
        Assert.Equal(1, counter);
    }

    /// <summary>
    /// 持續按住達 initialDelayFrames（20 幀）時，最後一幀應回傳 true，
    /// 計數器重置為 0，間隔切換為 intervalFrames。
    /// </summary>
    [Fact]
    public void AdvanceHeldRepeat_HeldForInitialDelay_FiresOnThresholdFrame()
    {
        int counter = 0;
        int interval = 0;
        bool fired = false;

        for (int i = 0; i < 20; i++)
        {
            fired = GamepadRepeatStateMachine.AdvanceHeldRepeat(
                isHeld: true,
                repeatCounter: ref counter,
                currentRepeatInterval: ref interval,
                initialDelayFrames: 20,
                intervalFrames: 3);
        }

        Assert.True(fired);
        Assert.Equal(0, counter);
        Assert.Equal(3, interval);
    }

    /// <summary>
    /// 按住中途放開時，計數器與間隔應立即重置為 0，確保下次按住重新開始初始延遲。
    /// </summary>
    [Fact]
    public void AdvanceHeldRepeat_AfterRelease_StateResets()
    {
        int counter = 10;
        int interval = 20;

        GamepadRepeatStateMachine.AdvanceHeldRepeat(
            isHeld: false,
            repeatCounter: ref counter,
            currentRepeatInterval: ref interval,
            initialDelayFrames: 20,
            intervalFrames: 3);

        Assert.Equal(0, counter);
        Assert.Equal(0, interval);
    }

    /// <summary>
    /// 持續按住 19 幀（initialDelayFrames=20）時，應始終回傳 false（尚未達到觸發閾值）。
    /// </summary>
    [Fact]
    public void AdvanceHeldRepeat_HeldBeforeThreshold_DoesNotFire()
    {
        int counter = 0;
        int interval = 0;
        bool fired = false;

        for (int i = 0; i < 19; i++)
        {
            fired |= GamepadRepeatStateMachine.AdvanceHeldRepeat(
                isHeld: true,
                repeatCounter: ref counter,
                currentRepeatInterval: ref interval,
                initialDelayFrames: 20,
                intervalFrames: 3);
        }

        Assert.False(fired);
    }
}
