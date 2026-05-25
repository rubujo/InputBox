using InputBox.Core.Configuration;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證控制器後端建立策略，確保 GameInput 是選用後端且失敗時會退避至 XInput。
/// </summary>
public sealed class GamepadControllerFactoryTests
{
    /// <summary>
    /// 使用者設定為 XInput 時，應直接建立 XInput 控制器，不觸發 GameInput 初始化。
    /// </summary>
    [Fact]
    public async Task CreateAsync_XInputProvider_UsesXInputFactory()
    {
        using var context = new StubInputContext();
        using var xInputController = new StubGamepadController("XInput");

        GamepadControllerCreationResult result = await GamepadControllerFactory.CreateAsync(
            AppSettings.GamepadProvider.XInput,
            context,
            new GamepadRepeatSettings(),
            static (_, _) => throw new InvalidOperationException("GameInput factory should not be called."),
            (_, _) => xInputController);

        Assert.Same(xInputController, result.Controller);
        Assert.False(result.FellBackToXInput);
        Assert.Null(result.GameInputFailure);
    }

    /// <summary>
    /// 使用者設定為 GameInput 且初始化成功時，應保留 GameInput 控制器，不退避。
    /// </summary>
    [Fact]
    public async Task CreateAsync_GameInputProviderAndFactorySucceeds_UsesGameInputController()
    {
        using var context = new StubInputContext();
        using var gameInputController = new StubGamepadController("GameInput");
        using var xInputController = new StubGamepadController("XInput");

        GamepadControllerCreationResult result = await GamepadControllerFactory.CreateAsync(
            AppSettings.GamepadProvider.GameInput,
            context,
            new GamepadRepeatSettings(),
            (_, _) => Task.FromResult<IGamepadController>(gameInputController),
            (_, _) => xInputController);

        Assert.Same(gameInputController, result.Controller);
        Assert.False(result.FellBackToXInput);
        Assert.Null(result.GameInputFailure);
    }

    /// <summary>
    /// 使用者設定為 GameInput 但 runtime 初始化失敗時，應退避為 XInput 並保留原始例外。
    /// </summary>
    [Fact]
    public async Task CreateAsync_GameInputFactoryThrows_ReturnsXInputFallback()
    {
        using var context = new StubInputContext();
        using var xInputController = new StubGamepadController("XInput");
        InvalidOperationException failure = new("GameInput runtime unavailable.");

        GamepadControllerCreationResult result = await GamepadControllerFactory.CreateAsync(
            AppSettings.GamepadProvider.GameInput,
            context,
            new GamepadRepeatSettings(),
            (_, _) => Task.FromException<IGamepadController>(failure),
            (_, _) => xInputController);

        Assert.Same(xInputController, result.Controller);
        Assert.True(result.FellBackToXInput);
        Assert.Same(failure, result.GameInputFailure);
    }

    /// <summary>
    /// 測試用輸入狀態內容。
    /// </summary>
    private sealed class StubInputContext : IInputContext
    {
        /// <summary>
        /// 測試中固定允許輸入。
        /// </summary>
        public bool IsInputActive => true;

        /// <summary>
        /// 測試替身不持有外部資源。
        /// </summary>
        public void Dispose()
        {

        }
    }

    /// <summary>
    /// 測試用控制器替身。
    /// </summary>
    private sealed class StubGamepadController : IGamepadController
    {
        public StubGamepadController(string deviceName)
        {
            DeviceName = deviceName;
        }

        public string DeviceName { get; }

        public string DeviceIdentity => DeviceName;

        public bool IsConnected => true;

        public GamepadCalibrationSnapshot CurrentCalibrationSnapshot => GamepadCalibrationSnapshot.Empty;

        public bool IsLeftShoulderHeld => false;

        public bool IsRightShoulderHeld => false;

        public bool IsLeftTriggerHeld => false;

        public bool IsRightTriggerHeld => false;

        public bool IsBackHeld => false;

        public bool IsBHeld => false;

        public bool IsXHeld => false;

        public VibrationMotorSupport VibrationMotorSupport => VibrationMotorSupport.DualMain;

        public GamepadRepeatSettings RepeatSettings { get; set; } = new();

        public int ThumbDeadzoneEnter { get; set; }

        public int ThumbDeadzoneExit { get; set; }

        public event Action<bool>? ConnectionChanged;
        public event Action? UpPressed;
        public event Action? DownPressed;
        public event Action? LeftPressed;
        public event Action? RightPressed;
        public event Action? LeftShoulderPressed;
        public event Action? LeftShoulderReleased;
        public event Action? LeftShoulderRepeat;
        public event Action? RightShoulderPressed;
        public event Action? RightShoulderReleased;
        public event Action? RightShoulderRepeat;
        public event Action? StartPressed;
        public event Action? BackPressed;
        public event Action? BackReleased;
        public event Action? APressed;
        public event Action? BPressed;
        public event Action? XPressed;
        public event Action? YPressed;
        public event Action? UpRepeat;
        public event Action? DownRepeat;
        public event Action? LeftRepeat;
        public event Action? RightRepeat;
        public event Action? RSLeftPressed;
        public event Action? RSRightPressed;
        public event Action? RSLeftRepeat;
        public event Action? RSRightRepeat;
        public event Action? LSClickPressed;
        public event Action? RSClickPressed;
        public event Action? LeftTriggerPressed;
        public event Action? RightTriggerPressed;
        public event Action? LeftTriggerRepeat;
        public event Action? RightTriggerRepeat;

        public Task VibrateAsync(
            ushort strength,
            int milliseconds = 60,
            VibrationPriority priority = VibrationPriority.Normal,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task VibrateAsync(
            VibrationProfile profile,
            VibrationPriority priority = VibrationPriority.Normal,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public void StopVibration()
        {

        }

        public void Pause()
        {

        }

        public void Resume()
        {

        }

        public void ResetCalibration()
        {

        }

        public void Dispose()
        {
            TouchRegisteredEvents();
        }

        public ValueTask DisposeAsync()
        {
            TouchRegisteredEvents();

            return ValueTask.CompletedTask;
        }

        private void TouchRegisteredEvents()
        {
            _ = ConnectionChanged;
            _ = UpPressed;
            _ = DownPressed;
            _ = LeftPressed;
            _ = RightPressed;
            _ = LeftShoulderPressed;
            _ = LeftShoulderReleased;
            _ = LeftShoulderRepeat;
            _ = RightShoulderPressed;
            _ = RightShoulderReleased;
            _ = RightShoulderRepeat;
            _ = StartPressed;
            _ = BackPressed;
            _ = BackReleased;
            _ = APressed;
            _ = BPressed;
            _ = XPressed;
            _ = YPressed;
            _ = UpRepeat;
            _ = DownRepeat;
            _ = LeftRepeat;
            _ = RightRepeat;
            _ = RSLeftPressed;
            _ = RSRightPressed;
            _ = RSLeftRepeat;
            _ = RSRightRepeat;
            _ = LSClickPressed;
            _ = RSClickPressed;
            _ = LeftTriggerPressed;
            _ = RightTriggerPressed;
            _ = LeftTriggerRepeat;
            _ = RightTriggerRepeat;
        }
    }
}
