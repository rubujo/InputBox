using InputBox.Core.Feedback;
using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證 MainForm 使用的控制器事件綁定表，確保肩鍵與觸發鍵捷徑不會在綁定層遺失。
/// </summary>
public sealed class GamepadEventBinderTests
{
    /// <summary>
    /// 片語子選單的快捷翻頁依賴 LB、RB、LT、RT 事件；綁定器必須完整轉接這些事件。
    /// </summary>
    [Fact]
    public void Bind_WhenShoulderAndTriggerEventsRaised_InvokesMappedHandlers()
    {
        StubGamepadController controller = new();

        int leftShoulderCount = 0;
        int rightShoulderCount = 0;
        int leftTriggerCount = 0;
        int rightTriggerCount = 0;

        GamepadEventBinder.Bind(
            controller,
            new GamepadEventBinder.BindingMap(
                OnConnectionChanged: _ => { },
                OnBackPressed: () => { },
                OnBackReleased: () => { },
                OnUpPressed: () => { },
                OnDownPressed: () => { },
                OnUpRepeat: () => { },
                OnDownRepeat: () => { },
                OnLeftPressed: () => { },
                OnLeftRepeat: () => { },
                OnRightPressed: () => { },
                OnRightRepeat: () => { },
                OnLeftShoulderPressed: () => leftShoulderCount++,
                OnLeftShoulderReleased: () => leftShoulderCount++,
                OnLeftShoulderRepeat: () => leftShoulderCount++,
                OnRightShoulderPressed: () => rightShoulderCount++,
                OnRightShoulderReleased: () => rightShoulderCount++,
                OnRightShoulderRepeat: () => rightShoulderCount++,
                OnLeftTriggerPressed: () => leftTriggerCount++,
                OnLeftTriggerRepeat: () => leftTriggerCount++,
                OnRightTriggerPressed: () => rightTriggerCount++,
                OnRightTriggerRepeat: () => rightTriggerCount++,
                OnStartPressed: () => { },
                OnAPressed: () => { },
                OnBPressed: () => { },
                OnYPressed: () => { },
                OnRSLeftPressed: () => { },
                OnRSLeftRepeat: () => { },
                OnRSRightPressed: () => { },
                OnRSRightRepeat: () => { },
                OnXPressed: () => { }));

        controller.RaiseLeftShoulderPressed();
        controller.RaiseLeftShoulderReleased();
        controller.RaiseLeftShoulderRepeat();
        controller.RaiseRightShoulderPressed();
        controller.RaiseRightShoulderReleased();
        controller.RaiseRightShoulderRepeat();
        controller.RaiseLeftTriggerPressed();
        controller.RaiseLeftTriggerRepeat();
        controller.RaiseRightTriggerPressed();
        controller.RaiseRightTriggerRepeat();

        Assert.Equal(3, leftShoulderCount);
        Assert.Equal(3, rightShoulderCount);
        Assert.Equal(2, leftTriggerCount);
        Assert.Equal(2, rightTriggerCount);
    }

    /// <summary>
    /// 測試替身控制器，專門用來驗證事件綁定是否完整轉接。
    /// </summary>
    private sealed class StubGamepadController : IGamepadController
    {
        public string DeviceName => "Test Controller";
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
        public event Action? LeftTriggerPressed;
        public event Action? RightTriggerPressed;
        public event Action? LeftTriggerRepeat;
        public event Action? RightTriggerRepeat;

        public Task VibrateAsync(ushort strength, int milliseconds = 60, VibrationPriority priority = VibrationPriority.Normal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task VibrateAsync(VibrationProfile profile, VibrationPriority priority = VibrationPriority.Normal, CancellationToken ct = default)
            => Task.CompletedTask;

        public void StopVibration() { }
        public void Pause() { }
        public void Resume() { }
        public void ResetCalibration() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// 讓測試替身顯式讀取所有事件欄位，避免未使用事件警告並保留完整介面覆蓋。
        /// </summary>
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
            _ = LeftTriggerPressed;
            _ = RightTriggerPressed;
            _ = LeftTriggerRepeat;
            _ = RightTriggerRepeat;
        }

        public void RaiseLeftShoulderPressed()
        {
            TouchRegisteredEvents();
            LeftShoulderPressed?.Invoke();
        }

        public void RaiseLeftShoulderReleased()
        {
            TouchRegisteredEvents();
            LeftShoulderReleased?.Invoke();
        }

        public void RaiseLeftShoulderRepeat()
        {
            TouchRegisteredEvents();
            LeftShoulderRepeat?.Invoke();
        }

        public void RaiseRightShoulderPressed()
        {
            TouchRegisteredEvents();
            RightShoulderPressed?.Invoke();
        }

        public void RaiseRightShoulderReleased()
        {
            TouchRegisteredEvents();
            RightShoulderReleased?.Invoke();
        }

        public void RaiseRightShoulderRepeat()
        {
            TouchRegisteredEvents();
            RightShoulderRepeat?.Invoke();
        }

        public void RaiseLeftTriggerPressed()
        {
            TouchRegisteredEvents();
            LeftTriggerPressed?.Invoke();
        }

        public void RaiseLeftTriggerRepeat()
        {
            TouchRegisteredEvents();
            LeftTriggerRepeat?.Invoke();
        }

        public void RaiseRightTriggerPressed()
        {
            TouchRegisteredEvents();
            RightTriggerPressed?.Invoke();
        }

        public void RaiseRightTriggerRepeat()
        {
            TouchRegisteredEvents();
            RightTriggerRepeat?.Invoke();
        }
    }
}