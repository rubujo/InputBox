using GameInputDotNet.Interop.Enums;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using System.Reflection;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證控制器在暫停輪詢時，會清掉暫態輸入與連發狀態，避免原生對話框返回後出現方向卡住。
/// </summary>
public sealed class GamepadControllerPauseTests
{
    /// <summary>
    /// 提供測試使用的最小輸入情境，固定回報可接受輸入。
    /// </summary>
    private sealed class StubInputContext : IInputContext
    {
        /// <summary>
        /// 取得目前是否允許控制器輸入。
        /// </summary>
        public bool IsInputActive => true;

        /// <summary>
        /// 測試替身不持有任何外部資源，因此不需實際處置。
        /// </summary>
        public void Dispose()
        {

        }
    }

    /// <summary>
    /// XInput 控制器在 Pause 後，應重置上一幀快照與各種連發計數器，避免恢復時把舊方向視為仍然按住。
    /// </summary>
    [Fact]
    public void Pause_XInputController_ClearsTransientRuntimeState()
    {
        using var controller = new XInputGamepadController(new StubInputContext());

        SetPrivateField(controller, "_repeatCounter", 5);
        SetPrivateField(controller, "_currentRepeatInterval", 3);
        SetPrivateField(controller, "_rsRepeatCounter", 4);
        SetPrivateField(controller, "_currentRSRepeatInterval", 2);
        SetPrivateField(controller, "_ltRepeatCounter", 7);
        SetPrivateField(controller, "_currentLTRepeatInterval", 1);
        SetPrivateField(controller, "_rtRepeatCounter", 6);
        SetPrivateField(controller, "_currentRTRepeatInterval", 1);
        SetPrivateField(controller, "_rsRepeatDirection", 1);
        SetPrivateField(controller, "_repeatDirection", XInput.GamepadButton.DpadRight);
        SetPrivateField(controller, "_hasPreviousState", true);

        controller.Pause();

        Assert.False(GetPrivateField<bool>(controller, "_hasPreviousState"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_repeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRSRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_ltRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentLTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rtRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatDirection"));
        Assert.Null(GetPrivateField<XInput.GamepadButton?>(controller, "_repeatDirection"));
    }

    /// <summary>
    /// GameInput 控制器在 Pause 後，應丟棄暫態按鍵快照與加工後方向狀態，避免原生檔案對話框關閉後重播舊輸入。
    /// </summary>
    [Fact]
    public void Pause_GameInputController_ClearsTransientRuntimeState()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        SetPrivateField(controller, "_repeatCounter", 5);
        SetPrivateField(controller, "_currentRepeatInterval", 3);
        SetPrivateField(controller, "_rsRepeatCounter", 4);
        SetPrivateField(controller, "_currentRSRepeatInterval", 2);
        SetPrivateField(controller, "_ltRepeatCounter", 7);
        SetPrivateField(controller, "_currentLTRepeatInterval", 1);
        SetPrivateField(controller, "_rtRepeatCounter", 6);
        SetPrivateField(controller, "_currentRTRepeatInterval", 1);
        SetPrivateField(controller, "_rsRepeatDirection", 1);
        SetPrivateField(controller, "_repeatDirection", GameInputGamepadButtons.DPadRight);
        SetPrivateField(controller, "_previousProcessedButtons", GameInputGamepadButtons.DPadRight);
        SetPrivateField(controller, "_hasPreviousState", true);

        controller.Pause();

        Assert.False(GetPrivateField<bool>(controller, "_hasPreviousState"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_repeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRSRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_ltRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentLTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rtRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatDirection"));
        Assert.Null(GetPrivateField<GameInputGamepadButtons?>(controller, "_repeatDirection"));
        Assert.Equal((GameInputGamepadButtons)0, GetPrivateField<GameInputGamepadButtons>(controller, "_previousProcessedButtons"));
    }

    /// <summary>
    /// 透過反射寫入私有欄位，模擬控制器在暫停前已存在的執行期狀態。
    /// </summary>
    /// <typeparam name="T">欄位值型別。</typeparam>
    /// <param name="target">要修改欄位的目標物件。</param>
    /// <param name="name">私有欄位名稱。</param>
    /// <param name="value">要寫入的欄位值。</param>
    private static void SetPrivateField<T>(object target, string name, T value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到欄位：{name}");

        field.SetValue(target, value);
    }

    /// <summary>
    /// 透過反射讀取私有欄位，驗證 Pause 後暫態狀態是否已正確清除。
    /// </summary>
    /// <typeparam name="T">欄位值型別。</typeparam>
    /// <param name="target">要讀取欄位的目標物件。</param>
    /// <param name="name">私有欄位名稱。</param>
    /// <returns>欄位目前的值；若為空值則回傳對應型別的預設值。</returns>
    private static T GetPrivateField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到欄位：{name}");

        object? value = field.GetValue(target);

        if (value is null)
        {
            return default!;
        }

        return (T)value;
    }
}