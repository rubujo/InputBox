using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using System.Reflection;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// GamepadMessageBox 關閉生命週期回歸測試。
/// <para>驗證當 FormClosing 被取消時，對話框不應提早釋放執行期資源，避免畫面仍開啟但內部狀態已被清空。</para>
/// </summary>
public sealed class GamepadMessageBoxTests
{
    /// <summary>
    /// 測試替身控制器：可指定連線狀態，供對話框初始化提示列與事件綁定驗證使用。
    /// </summary>
    private sealed class StubGamepadController : IGamepadController
    {
        /// <summary>
        /// 提供對話框顯示用的測試裝置名稱。
        /// </summary>
        public string DeviceName => "Test Controller";

        /// <summary>
        /// 提供 Auto 判斷路徑使用的穩定識別資訊；測試替身直接沿用顯示名稱即可。
        /// </summary>
        public string DeviceIdentity => DeviceName;

        public bool IsConnected { get; set; }
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
        /// 讓測試替身讀取所有事件欄位，避免以 pragma 抑制未使用事件警告。
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
            _ = LSClickPressed;
            _ = RSClickPressed;
            _ = LeftTriggerPressed;
            _ = RightTriggerPressed;
            _ = LeftTriggerRepeat;
            _ = RightTriggerRepeat;
        }

        /// <summary>
        /// 手動觸發連線狀態變更，供對話框測試驗證提示列與事件綁定流程。
        /// </summary>
        /// <param name="connected">是否模擬為已連線狀態。</param>
        public void RaiseConnectionChanged(bool connected)
        {
            TouchRegisteredEvents();
            IsConnected = connected;
            ConnectionChanged?.Invoke(connected);
        }

        /// <summary>
        /// 模擬按下確認鍵。
        /// </summary>
        public void RaiseAPressed()
        {
            TouchRegisteredEvents();
            APressed?.Invoke();
        }

        /// <summary>
        /// 模擬按下返回／取消鍵。
        /// </summary>
        public void RaiseBPressed()
        {
            TouchRegisteredEvents();
            BPressed?.Invoke();
        }
    }

    /// <summary>
    /// 若 FormClosing 被取消，對話框應保留取消權杖等執行期資源，避免仍顯示於畫面上卻失去互動能力。
    /// </summary>
    [Fact]
    public void OnFormClosing_WhenCancelled_DoesNotReleaseRuntimeResources()
    {
        using GamepadMessageBox dialog = new(
            "test message",
            "test caption",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        dialog.FormClosing += (_, e) => e.Cancel = true;

        MethodInfo onFormClosing = typeof(GamepadMessageBox).GetMethod(
            "OnFormClosing",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GamepadMessageBox.OnFormClosing。");

        FieldInfo ctsField = typeof(GamepadMessageBox).GetField(
            "_cts",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GamepadMessageBox._cts 欄位。");

        FormClosingEventArgs args = new(CloseReason.UserClosing, cancel: false);

        onFormClosing.Invoke(dialog, [args]);

        Assert.True(args.Cancel);
        Assert.False(dialog.IsDisposed);
        Assert.NotNull(ctsField.GetValue(dialog));
    }

    /// <summary>
    /// 當對話框一開始就綁定到已連線的控制器時，提示文字與可見狀態都應立即同步，避免不同 API 後端出現初始狀態不一致。
    /// </summary>
    [Fact]
    public void GamepadControllerSetter_WhenControllerAlreadyConnected_SynchronizesHintTextAndVisibilityImmediately()
    {
        using GamepadMessageBox dialog = new(
            "test message",
            "test caption",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        StubGamepadController controller = new()
        {
            IsConnected = true
        };

        dialog.GamepadController = controller;
        dialog.Show();
        Application.DoEvents();

        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        Assert.Empty(dialog.Controls.Find("_lblHint", true));
        Assert.Contains(
            string.Format(
                Resources.Strings.GmBox_A11y_Hint,
                profile.FormatPrimaryActionHintText(Resources.Strings.Btn_Yes),
                profile.FormatCancelActionHintText(Resources.Strings.Btn_No)),
            dialog.AccessibleDescription);
    }

    /// <summary>
    /// 非 PlayStation 配置下，畫面不應額外顯示重複的提示列；提示文字仍應保留供無障礙描述使用。
    /// </summary>
    [Fact]
    public void GamepadControllerSetter_NintendoLayout_HidesVisualHintRow()
    {
        AppSettings.GamepadFaceButtonMode previousMode = AppSettings.Current.GamepadFaceButtonModeType;

        try
        {
            AppSettings.Current.GamepadFaceButtonModeType = AppSettings.GamepadFaceButtonMode.Nintendo;

            using GamepadMessageBox dialog = new(
                "test message",
                "test caption",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            StubGamepadController controller = new()
            {
                IsConnected = true
            };

            dialog.GamepadController = controller;
            dialog.Show();
            Application.DoEvents();

            Assert.Empty(dialog.Controls.Find("_lblHint", true));
            Assert.Contains(ControlExtensions.GetActionHintText('A', Resources.Strings.Btn_Yes), dialog.AccessibleDescription);
            Assert.Contains(ControlExtensions.GetActionHintText('B', Resources.Strings.Btn_No), dialog.AccessibleDescription);
        }
        finally
        {
            AppSettings.Current.GamepadFaceButtonModeType = previousMode;
        }
    }

    /// <summary>
    /// PlayStation 傳統配置下，畫面不再顯示額外提示列，但無障礙描述仍須保留實際生效的 B / A 對應。
    /// </summary>
    [Fact]
    public void GamepadControllerSetter_PlayStationTraditional_KeepsMappedHintsOnlyInAccessibilityDescription()
    {
        AppSettings.GamepadFaceButtonMode previousMode = AppSettings.Current.GamepadFaceButtonModeType;

        try
        {
            AppSettings.Current.GamepadFaceButtonModeType = AppSettings.GamepadFaceButtonMode.PlayStationTraditional;

            using GamepadMessageBox dialog = new(
                "test message",
                "test caption",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            StubGamepadController controller = new()
            {
                IsConnected = true
            };

            dialog.GamepadController = controller;
            dialog.Show();
            Application.DoEvents();

            Assert.Empty(dialog.Controls.Find("_lblHint", true));
            Assert.Contains(ControlExtensions.GetActionHintText('B', $"○ {Resources.Strings.Btn_Yes}"), dialog.AccessibleDescription);
            Assert.Contains(ControlExtensions.GetActionHintText('A', $"× {Resources.Strings.Btn_No}"), dialog.AccessibleDescription);
        }
        finally
        {
            AppSettings.Current.GamepadFaceButtonModeType = previousMode;
        }
    }

    /// <summary>
    /// Nintendo 配置下，實體下方鍵應執行取消、實體右側鍵應執行確認，避免仍沿用 Xbox 的南鍵確認邏輯。
    /// </summary>
    [Fact]
    public void GamepadButtons_NintendoMode_SwapsPhysicalConfirmAndCancelBehavior()
    {
        AppSettings.GamepadFaceButtonMode previousMode = AppSettings.Current.GamepadFaceButtonModeType;

        try
        {
            AppSettings.Current.GamepadFaceButtonModeType = AppSettings.GamepadFaceButtonMode.Nintendo;

            using GamepadMessageBox dialog = new(
                "test message",
                "test caption",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            StubGamepadController controller = new()
            {
                IsConnected = true
            };

            dialog.GamepadController = controller;
            dialog.Show();
            Application.DoEvents();

            controller.RaiseAPressed();
            Application.DoEvents();

            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
        }
        finally
        {
            AppSettings.Current.GamepadFaceButtonModeType = previousMode;
        }
    }
}