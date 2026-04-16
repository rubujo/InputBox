using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// <see cref="GamepadRepeatSettings"/> 單元測試
/// <para>驗證預設值的合理性，以及 <see cref="GamepadRepeatSettings.Validate"/> 對無效值的防禦行為。</para>
/// </summary>
public class GamepadRepeatSettingsTests
{
    /// <summary>
    /// 預設建構後，InitialDelayFrames 應為 20，IntervalFrames 應為 3，
    /// 確保預設連發參數符合 60fps 手把輸入的使用體驗預期。
    /// </summary>
    [Fact]
    public void DefaultValues_AreValid()
    {
        var settings = new GamepadRepeatSettings();

        Assert.Equal(20, settings.InitialDelayFrames);
        Assert.Equal(3, settings.IntervalFrames);
    }

    /// <summary>
    /// 使用預設值呼叫 Validate() 時，不應拋出任何例外。
    /// </summary>
    [Fact]
    public void Validate_DefaultSettings_DoesNotThrow()
    {
        var settings = new GamepadRepeatSettings();

        var capturedException = Record.Exception(settings.Validate);

        Assert.Null(capturedException);
    }

    /// <summary>
    /// InitialDelayFrames 設為 0 時，Validate() 應拋出 <see cref="InvalidOperationException"/>。
    /// 0 幀延遲會導致第一幀立即觸發，屬無效設定。
    /// </summary>
    [Fact]
    public void Validate_InitialDelayFramesZero_Throws()
    {
        var settings = new GamepadRepeatSettings { InitialDelayFrames = 0 };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    /// <summary>
    /// InitialDelayFrames 設為負值時，Validate() 應拋出 <see cref="InvalidOperationException"/>。
    /// </summary>
    [Fact]
    public void Validate_InitialDelayFramesNegative_Throws()
    {
        var settings = new GamepadRepeatSettings { InitialDelayFrames = -1 };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    /// <summary>
    /// IntervalFrames 設為 0 時，Validate() 應拋出 <see cref="InvalidOperationException"/>。
    /// 0 幀間隔會導致每幀都觸發，屬無效設定。
    /// </summary>
    [Fact]
    public void Validate_IntervalFramesZero_Throws()
    {
        var settings = new GamepadRepeatSettings { IntervalFrames = 0 };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    /// <summary>
    /// IntervalFrames 設為負值時，Validate() 應拋出 <see cref="InvalidOperationException"/>。
    /// </summary>
    [Fact]
    public void Validate_IntervalFramesNegative_Throws()
    {
        var settings = new GamepadRepeatSettings { IntervalFrames = -5 };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    /// <summary>
    /// 兩個欄位皆為正數的最小合法值（1）時，Validate() 不應拋出例外。
    /// </summary>
    [Fact]
    public void Validate_BothPositive_DoesNotThrow()
    {
        var settings = new GamepadRepeatSettings
        {
            InitialDelayFrames = 1,
            IntervalFrames = 1
        };

        var capturedException = Record.Exception(settings.Validate);

        Assert.Null(capturedException);
    }
}