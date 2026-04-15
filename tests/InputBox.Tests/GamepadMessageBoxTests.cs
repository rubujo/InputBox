using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
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
#pragma warning disable CS0067
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
        public bool IsLeftShoulderHeld => false;
        public bool IsRightShoulderHeld => false;
        public bool IsLeftTriggerHeld => false;
        public bool IsRightTriggerHeld => false;
        public bool IsBackHeld => false;
        public bool IsBHeld => false;
        public GamepadRepeatSettings RepeatSettings { get; set; } = new();
        public int ThumbDeadzoneEnter { get; set; }
        public int ThumbDeadzoneExit { get; set; }

        public event Action<bool>? ConnectionChanged;
        public event Action? UpPressed;
        public event Action? DownPressed;
        public event Action? LeftPressed;
        public event Action? RightPressed;
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

        public void StopVibration() { }
        public void Pause() { }
        public void Resume() { }
        public void ResetCalibration() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// 手動觸發連線狀態變更，供對話框測試驗證提示列與事件綁定流程。
        /// </summary>
        /// <param name="connected">是否模擬為已連線狀態。</param>
        public void RaiseConnectionChanged(bool connected)
        {
            IsConnected = connected;
            ConnectionChanged?.Invoke(connected);
        }

        public void RaiseAPressed() => APressed?.Invoke();

        public void RaiseBPressed() => BPressed?.Invoke();
    }
#pragma warning restore CS0067

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
    /// 當對話框一開始就綁定到已連線的控制器時，提示列應立即可見，避免不同 API 後端出現一邊有提示、一邊沒有提示的不一致行為。
    /// </summary>
    [Fact]
    public void GamepadControllerSetter_WhenControllerAlreadyConnected_ShowsHintImmediately()
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

        FieldInfo hintField = typeof(GamepadMessageBox).GetField(
            "_lblHint",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GamepadMessageBox._lblHint 欄位。");

        Label hintLabel = Assert.IsType<Label>(hintField.GetValue(dialog));

        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        Assert.True(hintLabel.Visible);
        Assert.Equal(
            string.Format(
                Resources.Strings.GmBox_A11y_Hint,
                profile.FormatPrimaryActionHintText(Resources.Strings.Btn_Yes),
                profile.FormatCancelActionHintText(Resources.Strings.Btn_No)),
            hintLabel.Text);
    }

    /// <summary>
    /// PlayStation 傳統配置下，提示列必須改用實際生效的 B / A 對應，避免仍固定顯示成 Xbox 風格的 A / B。
    /// </summary>
    [Fact]
    public void GamepadControllerSetter_PlayStationTraditional_UsesSwappedHintLabels()
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

            FieldInfo hintField = typeof(GamepadMessageBox).GetField(
                "_lblHint",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("找不到 GamepadMessageBox._lblHint 欄位。");

            Label hintLabel = Assert.IsType<Label>(hintField.GetValue(dialog));
            GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

            Assert.Contains(ControlExtensions.GetActionHintText('B', $"○ {Resources.Strings.Btn_Yes}"), hintLabel.Text);
            Assert.Contains(ControlExtensions.GetActionHintText('A', $"× {Resources.Strings.Btn_No}"), hintLabel.Text);
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