using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using System.Reflection;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證片語管理對話框的肩鍵與板機捷徑能快速切換左側片語清單，避免必須逐筆按方向鍵移動。
/// </summary>
[Collection(PhraseDataTestRequirements.CollectionName)]
public sealed class PhraseManagerDialogGamepadTests : IDisposable
{
    private static readonly string PhrasePath = Path.Combine(
        AppSettings.ConfigDirectory, "phrases.json");

    private static readonly string BackupPath = PhrasePath + ".testbackup";

    public PhraseManagerDialogGamepadTests()
    {
        Directory.CreateDirectory(AppSettings.ConfigDirectory);

        if (File.Exists(PhrasePath))
        {
            File.Copy(PhrasePath, BackupPath, overwrite: true);
        }
    }

    public void Dispose()
    {
        if (File.Exists(BackupPath))
        {
            File.Move(BackupPath, PhrasePath, overwrite: true);
        }
        else if (File.Exists(PhrasePath))
        {
            File.Delete(PhrasePath);
        }
    }

    /// <summary>
    /// 當片語管理對話框開啟時，LB 與 RB 應切換到上一個與下一個片語項目。
    /// </summary>
    [Fact]
    public void ShoulderButtons_SwitchSelectedPhrase()
    {
        PhraseService phraseService = CreatePhraseServiceWithEntries();
        using PhraseManagerDialog dialog = new(phraseService);
        using StubGamepadController controller = new();

        dialog.GamepadController = controller;
        IntPtr handle = dialog.Handle;
        dialog.Show();
        Application.DoEvents();

        ListBox list = GetRequiredPrivateField<ListBox>(dialog, "_lstPhrases");
        list.SelectedIndex = 1;
        list.Focus();
        Application.DoEvents();

        controller.RaiseLeftShoulderPressed();
        Application.DoEvents();
        Assert.Equal(0, list.SelectedIndex);

        controller.RaiseRightShoulderPressed();
        Application.DoEvents();
        Assert.Equal(1, list.SelectedIndex);
    }

    /// <summary>
    /// 當片語管理對話框開啟時，LT 與 RT 應直接跳到第一個與最後一個片語項目。
    /// </summary>
    [Fact]
    public void TriggerButtons_JumpToListBoundaries()
    {
        PhraseService phraseService = CreatePhraseServiceWithEntries();
        using PhraseManagerDialog dialog = new(phraseService);
        using StubGamepadController controller = new();

        dialog.GamepadController = controller;
        IntPtr handle = dialog.Handle;
        dialog.Show();
        Application.DoEvents();

        ListBox list = GetRequiredPrivateField<ListBox>(dialog, "_lstPhrases");
        list.SelectedIndex = 1;
        list.Focus();
        Application.DoEvents();

        controller.RaiseRightTriggerPressed();
        Application.DoEvents();
        Assert.Equal(list.Items.Count - 1, list.SelectedIndex);

        controller.RaiseLeftTriggerPressed();
        Application.DoEvents();
        Assert.Equal(0, list.SelectedIndex);
    }

    /// <summary>
    /// LT 與 RT 的長按連發不應在片語管理對話框中重複觸發邊界跳轉，避免一次到位後仍持續搶走操作節奏。
    /// </summary>
    [Fact]
    public void TriggerRepeat_DoesNotJumpPhraseListBoundariesAgain()
    {
        PhraseService phraseService = CreatePhraseServiceWithEntries();
        using PhraseManagerDialog dialog = new(phraseService);
        using StubGamepadController controller = new();

        dialog.GamepadController = controller;
        IntPtr handle = dialog.Handle;
        dialog.Show();
        Application.DoEvents();

        ListBox list = GetRequiredPrivateField<ListBox>(dialog, "_lstPhrases");
        list.SelectedIndex = 1;
        list.Focus();
        Application.DoEvents();

        controller.RaiseRightTriggerRepeat();
        Application.DoEvents();
        Assert.Equal(1, list.SelectedIndex);

        controller.RaiseLeftTriggerRepeat();
        Application.DoEvents();
        Assert.Equal(1, list.SelectedIndex);
    }

    private static PhraseService CreatePhraseServiceWithEntries()
    {
        PhraseService phraseService = new();
        Assert.True(phraseService.Add("片語一", "內容一"));
        Assert.True(phraseService.Add("片語二", "內容二"));
        Assert.True(phraseService.Add("片語三", "內容三"));

        return phraseService;
    }

    private static T GetRequiredPrivateField<T>(object instance, string fieldName) where T : class
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);

        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private sealed class StubGamepadController : IGamepadController
    {
        public string DeviceName => "Test Controller";
        public string DeviceIdentity => "test-controller";
        public bool IsConnected => true;
        public bool IsLeftShoulderHeld { get; private set; }
        public bool IsRightShoulderHeld { get; private set; }
        public bool IsLeftTriggerHeld { get; private set; }
        public bool IsRightTriggerHeld { get; private set; }
        public bool IsBackHeld => false;
        public bool IsBHeld => false;
        public bool IsXHeld => false;
        public VibrationMotorSupport VibrationMotorSupport => VibrationMotorSupport.None;
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

        public Task VibrateAsync(ushort strength, int milliseconds = 60, VibrationPriority priority = VibrationPriority.Normal, CancellationToken ct = default) => Task.CompletedTask;
        public Task VibrateAsync(VibrationProfile profile, VibrationPriority priority = VibrationPriority.Normal, CancellationToken ct = default) => Task.CompletedTask;
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
            IsLeftShoulderHeld = true;
            LeftShoulderPressed?.Invoke();
        }

        public void RaiseRightShoulderPressed()
        {
            TouchRegisteredEvents();
            IsRightShoulderHeld = true;
            RightShoulderPressed?.Invoke();
        }

        public void RaiseLeftTriggerPressed()
        {
            TouchRegisteredEvents();
            IsLeftTriggerHeld = true;
            LeftTriggerPressed?.Invoke();
        }

        public void RaiseRightTriggerPressed()
        {
            TouchRegisteredEvents();
            IsRightTriggerHeld = true;
            RightTriggerPressed?.Invoke();
        }

        public void RaiseLeftTriggerRepeat()
        {
            TouchRegisteredEvents();
            IsLeftTriggerHeld = true;
            LeftTriggerRepeat?.Invoke();
        }

        public void RaiseRightTriggerRepeat()
        {
            TouchRegisteredEvents();
            IsRightTriggerHeld = true;
            RightTriggerRepeat?.Invoke();
        }
    }
}
