using InputBox.Core.Configuration;

namespace InputBox.Core.Input;

/// <summary>
/// 建立遊戲控制器後端的結果。
/// </summary>
/// <param name="Controller">已建立的控制器。</param>
/// <param name="FellBackToXInput">是否因 GameInput 初始化失敗而退避至 XInput。</param>
/// <param name="GameInputFailure">GameInput 初始化失敗例外；未退避時為 null。</param>
internal readonly record struct GamepadControllerCreationResult(
    IGamepadController Controller,
    bool FellBackToXInput,
    Exception? GameInputFailure);

/// <summary>
/// 根據設定建立遊戲控制器後端，並集中處理 GameInput 退避至 XInput 的策略。
/// </summary>
internal static class GamepadControllerFactory
{
    /// <summary>
    /// 建立目前設定指定的控制器後端。
    /// </summary>
    /// <param name="provider">使用者設定的控制器後端。</param>
    /// <param name="context">輸入狀態內容。</param>
    /// <param name="repeatSettings">連發設定。</param>
    /// <returns>控制器建立結果。</returns>
    public static Task<GamepadControllerCreationResult> CreateAsync(
        AppSettings.GamepadProvider provider,
        IInputContext context,
        GamepadRepeatSettings repeatSettings)
        => CreateAsync(
            provider,
            context,
            repeatSettings,
            CreateGameInputControllerAsync,
            CreateXInputController);

    /// <summary>
    /// 建立目前設定指定的控制器後端，供測試注入後端建立函式。
    /// </summary>
    /// <param name="provider">使用者設定的控制器後端。</param>
    /// <param name="context">輸入狀態內容。</param>
    /// <param name="repeatSettings">連發設定。</param>
    /// <param name="gameInputFactory">GameInput 建立函式。</param>
    /// <param name="xInputFactory">XInput 建立函式。</param>
    /// <returns>控制器建立結果。</returns>
    internal static async Task<GamepadControllerCreationResult> CreateAsync(
        AppSettings.GamepadProvider provider,
        IInputContext context,
        GamepadRepeatSettings repeatSettings,
        Func<IInputContext, GamepadRepeatSettings, Task<IGamepadController>> gameInputFactory,
        Func<IInputContext, GamepadRepeatSettings, IGamepadController> xInputFactory)
    {
        if (provider != AppSettings.GamepadProvider.GameInput)
        {
            return new GamepadControllerCreationResult(
                xInputFactory(context, repeatSettings),
                FellBackToXInput: false,
                GameInputFailure: null);
        }

        try
        {
            return new GamepadControllerCreationResult(
                await gameInputFactory(context, repeatSettings).ConfigureAwait(false),
                FellBackToXInput: false,
                GameInputFailure: null);
        }
        catch (Exception ex)
        {
            return new GamepadControllerCreationResult(
                xInputFactory(context, repeatSettings),
                FellBackToXInput: true,
                GameInputFailure: ex);
        }
    }

    private static async Task<IGamepadController> CreateGameInputControllerAsync(
        IInputContext context,
        GamepadRepeatSettings repeatSettings)
        => await GameInputGamepadController.CreateAsync(context, repeatSettings).ConfigureAwait(false);

    private static IGamepadController CreateXInputController(
        IInputContext context,
        GamepadRepeatSettings repeatSettings)
    {
        uint activeUserIndex = XInputGamepadController.GetFirstConnectedUserIndex();

        return new XInputGamepadController(context, activeUserIndex, repeatSettings);
    }
}