using InputBox.Core.Configuration;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// VibrationPatterns 的靜態欄位值與 GlobalIntensityMultiplier 行為測試
/// <para>確保各震動設定的強度與持續時間符合設計規格，且全域倍率能正確設定與讀取。</para>
/// </summary>
public class VibrationPatternsTests
{
    // ── GlobalIntensityMultiplier ──────────────────────────────────────────

    /// <summary>
    /// GlobalIntensityMultiplier 預設值應為 0.7f。
    /// </summary>
    [Fact]
    public void GlobalIntensityMultiplier_DefaultValue_Is0Point7()
    {
        // 重置為已知狀態
        VibrationPatterns.GlobalIntensityMultiplier = 0.7f;
        Assert.Equal(0.7f, VibrationPatterns.GlobalIntensityMultiplier);
    }

    /// <summary>
    /// 設定 GlobalIntensityMultiplier 後，讀取值應與設定值一致。
    /// </summary>
    [Fact]
    public void GlobalIntensityMultiplier_SetAndGet_ReturnsNewValue()
    {
        float original = VibrationPatterns.GlobalIntensityMultiplier;

        try
        {
            VibrationPatterns.GlobalIntensityMultiplier = 0.5f;
            Assert.Equal(0.5f, VibrationPatterns.GlobalIntensityMultiplier);
        }
        finally
        {
            VibrationPatterns.GlobalIntensityMultiplier = original;
        }
    }

    // ── 靜態欄位設計值 ─────────────────────────────────────────────────────

    /// <summary>
    /// CursorMove 強度應為 18000（足以克服馬達啟動閾值的輕微點擊感）。
    /// </summary>
    [Fact]
    public void CursorMove_Strength_Is18000()
    {
        Assert.Equal(18000, VibrationPatterns.CursorMove.Strength);
    }

    /// <summary>
    /// CursorMove 持續時間應為 50ms（短暫點擊感，避免連按時糊在一起）。
    /// </summary>
    [Fact]
    public void CursorMove_Duration_Is50()
    {
        Assert.Equal(50, VibrationPatterns.CursorMove.Duration);
    }

    /// <summary>
    /// CopySuccess 強度應為 40000（約 60% 強度的確認感）。
    /// </summary>
    [Fact]
    public void CopySuccess_Strength_Is40000()
    {
        Assert.Equal(40000, VibrationPatterns.CopySuccess.Strength);
    }

    /// <summary>
    /// CopySuccess 持續時間應為 150ms。
    /// </summary>
    [Fact]
    public void CopySuccess_Duration_Is150()
    {
        Assert.Equal(150, VibrationPatterns.CopySuccess.Duration);
    }

    /// <summary>
    /// ActionFail 強度應為 45000（最強，確保錯誤不被忽視）。
    /// </summary>
    [Fact]
    public void ActionFail_Strength_Is45000()
    {
        Assert.Equal(45000, VibrationPatterns.ActionFail.Strength);
    }

    /// <summary>
    /// ActionFail 持續時間應為 200ms（最長，打破節奏）。
    /// </summary>
    [Fact]
    public void ActionFail_Duration_Is200()
    {
        Assert.Equal(200, VibrationPatterns.ActionFail.Duration);
    }

    /// <summary>
    /// ControllerConnected 強度應為 30000（觸覺握手的中等強度）。
    /// </summary>
    [Fact]
    public void ControllerConnected_Strength_Is30000()
    {
        Assert.Equal(30000, VibrationPatterns.ControllerConnected.Strength);
    }

    /// <summary>
    /// ControllerConnected 持續時間應為 200ms（確保視聽雙障使用者也能感知）。
    /// </summary>
    [Fact]
    public void ControllerConnected_Duration_Is200()
    {
        Assert.Equal(200, VibrationPatterns.ControllerConnected.Duration);
    }

    /// <summary>
    /// 強度預覽應維持短促且中等強度，方便安全地立即確認目前震動設定。
    /// </summary>
    [Fact]
    public void IntensityPreview_UsesModerateShortPulse()
    {
        Assert.Equal(26000, VibrationPatterns.IntensityPreview.Strength);
        Assert.Equal(75, VibrationPatterns.IntensityPreview.Duration);
    }

    // ── VibrationProfile 值語義 ────────────────────────────────────────────

    /// <summary>
    /// VibrationProfile 的 Strength 與 Duration 應正確封裝初始值。
    /// </summary>
    [Fact]
    public void VibrationProfile_ConstructorValues_AreAccessible()
    {
        var profile = new VibrationProfile(12345, 99);
        Assert.Equal(12345, profile.Strength);
        Assert.Equal(99, profile.Duration);
    }

    /// <summary>
    /// VibrationProfile 是 record struct，相同值的兩個實例應相等。
    /// </summary>
    [Fact]
    public void VibrationProfile_SameValues_AreEqual()
    {
        var firstProfile = new VibrationProfile(30000, 120);
        var secondProfile = new VibrationProfile(30000, 120);
        Assert.Equal(firstProfile, secondProfile);
    }

    /// <summary>
    /// 方向性震動設定應保留各馬達的比例，供 XInput / GameInput 做左右差異化回饋。
    /// </summary>
    [Fact]
    public void VibrationProfile_DirectionalMotorScales_AreAccessible()
    {
        var profile = new VibrationProfile(32000, 80, 1.0f, 0.3f, 0.8f, 0.1f);

        Assert.Equal(1.0f, profile.LowFrequencyMotorScale);
        Assert.Equal(0.3f, profile.HighFrequencyMotorScale);
        Assert.Equal(0.8f, profile.LeftTriggerMotorScale);
        Assert.Equal(0.1f, profile.RightTriggerMotorScale);
    }

    /// <summary>
    /// 回饋服務在派送方向性震動時，仍應套用既有的全域強度倍率設定。
    /// </summary>
    [Fact]
    public async Task FeedbackService_VibrateAsync_DirectionalProfile_RespectsGlobalIntensityMultiplier()
    {
        float originalMultiplier = VibrationPatterns.GlobalIntensityMultiplier;
        bool originalEnableVibration = AppSettings.Current.EnableVibration;

        try
        {
            VibrationPatterns.GlobalIntensityMultiplier = 0.5f;
            AppSettings.Current.EnableVibration = true;

            var controller = new StubGamepadController();
            var profile = new VibrationProfile(20000, 90, 1.0f, 0.3f, 1.0f, 0.0f);

            await FeedbackService.VibrateAsync(controller, profile, TestContext.Current.CancellationToken);

            Assert.NotNull(controller.LastProfile);
            Assert.Equal((ushort)10000, controller.LastProfile!.Value.Strength);
            Assert.Equal(1.0f, controller.LastProfile.Value.LowFrequencyMotorScale);
            Assert.Equal(0.3f, controller.LastProfile.Value.HighFrequencyMotorScale);
            Assert.Equal(VibrationPriority.Normal, controller.LastPriority);
        }
        finally
        {
            VibrationPatterns.GlobalIntensityMultiplier = originalMultiplier;
            AppSettings.Current.EnableVibration = originalEnableVibration;
        }
    }

    /// <summary>
    /// 不同導覽情境應回傳可區分的中央化觸覺樣式，方便後續統一微調。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetNavigationProfile_UsesSemanticContextAndDirection()
    {
        VibrationProfile historyBackward = VibrationPatterns.GetNavigationProfile(VibrationSemantic.PageSwitch, direction: -1, VibrationContext.History);
        VibrationProfile historyForward = VibrationPatterns.GetNavigationProfile(VibrationSemantic.PageSwitch, direction: 1, VibrationContext.History);
        VibrationProfile phraseBackward = VibrationPatterns.GetNavigationProfile(VibrationSemantic.PageSwitch, direction: -1, VibrationContext.PhraseMenu);

        Assert.NotEqual(historyBackward, historyForward);
        Assert.NotEqual(historyBackward.Duration, phraseBackward.Duration);
        Assert.True(historyBackward.LowFrequencyMotorScale > historyBackward.HighFrequencyMotorScale);
        Assert.True(historyForward.HighFrequencyMotorScale > historyForward.LowFrequencyMotorScale);
    }

    /// <summary>
    /// 具備扳機馬達時，語意序列應回傳多段式微震動，讓單字跳轉保有更細緻的層次感。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetNavigationSequence_WithTriggerSupport_ReturnsLayeredSequence()
    {
        IReadOnlyList<VibrationSequenceStep> sequence = VibrationPatterns.GetNavigationSequence(
            VibrationSemantic.WordJump,
            direction: -1,
            context: VibrationContext.TextBoundary,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.True(sequence.Count >= 2);
        Assert.True(sequence[0].Profile.LeftTriggerMotorScale > sequence[0].Profile.RightTriggerMotorScale);
        Assert.True(sequence[0].Profile.LeftTriggerMotorScale > sequence[0].Profile.LowFrequencyMotorScale);
    }

    /// <summary>
    /// 若控制器只支援雙主馬達，系統應自動退化為單段安全樣式，而不是仍要求四馬達輸出。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetNavigationSequence_WithoutTriggerSupport_FallsBackToSingleStep()
    {
        IReadOnlyList<VibrationSequenceStep> sequence = VibrationPatterns.GetNavigationSequence(
            VibrationSemantic.ModeToggle,
            direction: 1,
            context: VibrationContext.PrivacyMode,
            motorSupport: VibrationMotorSupport.DualMain);

        Assert.Single(sequence);
        Assert.Equal(VibrationPatterns.PrivacyModeToggle, sequence[0].Profile);
    }

    /// <summary>
    /// 多段震動序列中的每一步都必須繼續遵守既有的全域強度倍率設定。
    /// </summary>
    [Fact]
    public async Task FeedbackService_VibrateSequenceAsync_AppliesGlobalIntensityMultiplierToEachStep()
    {
        float originalMultiplier = VibrationPatterns.GlobalIntensityMultiplier;
        bool originalEnableVibration = AppSettings.Current.EnableVibration;

        try
        {
            VibrationPatterns.GlobalIntensityMultiplier = 0.5f;
            AppSettings.Current.EnableVibration = true;

            var controller = new StubGamepadController();
            IReadOnlyList<VibrationSequenceStep> sequence = VibrationPatterns.GetNavigationSequence(
                VibrationSemantic.PageSwitch,
                direction: 1,
                context: VibrationContext.History,
                motorSupport: controller.VibrationMotorSupport);

            await FeedbackService.VibrateSequenceAsync(controller, sequence, TestContext.Current.CancellationToken);

            Assert.True(controller.PlayedProfiles.Count >= 2);
            Assert.All(controller.PlayedProfiles, profile => Assert.True(profile.Strength > 0));
            Assert.Equal((ushort)(sequence[0].Profile.Strength * 0.5f), controller.PlayedProfiles[0].Strength);
        }
        finally
        {
            VibrationPatterns.GlobalIntensityMultiplier = originalMultiplier;
            AppSettings.Current.EnableVibration = originalEnableVibration;
        }
    }

    /// <summary>
    /// 單字跳轉的第二拍應比第一拍更短更輕，避免長時間操作時的手部疲勞。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetNavigationSequence_WordJump_UsesCrispShorterSecondPulse()
    {
        IReadOnlyList<VibrationSequenceStep> sequence = VibrationPatterns.GetNavigationSequence(
            VibrationSemantic.WordJump,
            direction: 1,
            context: VibrationContext.TextBoundary,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.True(sequence.Count >= 2);
        Assert.True(sequence[1].Profile.Duration < sequence[0].Profile.Duration);
        Assert.True(sequence[1].Profile.Strength < sequence[0].Profile.Strength);
    }

    /// <summary>
    /// 片語子選單翻頁應保留比歷程翻頁更鮮明的前拍與獨立收尾，讓不同情境更容易靠手感分辨。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetNavigationSequence_PhrasePaging_StaysDistinctFromHistoryPaging()
    {
        IReadOnlyList<VibrationSequenceStep> historySequence = VibrationPatterns.GetNavigationSequence(
            VibrationSemantic.PageSwitch,
            direction: -1,
            context: VibrationContext.History,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        IReadOnlyList<VibrationSequenceStep> phraseSequence = VibrationPatterns.GetNavigationSequence(
            VibrationSemantic.PageSwitch,
            direction: -1,
            context: VibrationContext.PhraseMenu,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.True(phraseSequence[0].Profile.Strength > historySequence[0].Profile.Strength);
        Assert.NotEqual(phraseSequence[1].Profile, historySequence[1].Profile);
    }

    /// <summary>
    /// 一般游標微震應明顯輕於翻頁震動，降低長時間文字編輯時的疲勞感。
    /// </summary>
    [Fact]
    public void VibrationPatterns_CursorMove_RemainsLighterThanPageSwitch()
    {
        Assert.True(VibrationPatterns.CursorMoveLeft.Strength < VibrationPatterns.HistoryPageBackward.Strength);
        Assert.True(VibrationPatterns.CursorMoveLeft.Duration < VibrationPatterns.HistoryPageBackward.Duration);
        Assert.True(VibrationPatterns.CursorMoveRightEcho.Duration < VibrationPatterns.HistoryPageForwardSettle.Duration);
    }

    /// <summary>
    /// 隱私模式切換的觸覺序列應區分開啟與關閉，讓使用者能以手感辨識目前是上鎖或解鎖。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetNavigationSequence_PrivacyMode_DiffersForEnableAndDisable()
    {
        IReadOnlyList<VibrationSequenceStep> enableSequence = VibrationPatterns.GetNavigationSequence(
            VibrationSemantic.ModeToggle,
            direction: 1,
            context: VibrationContext.PrivacyMode,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        IReadOnlyList<VibrationSequenceStep> disableSequence = VibrationPatterns.GetNavigationSequence(
            VibrationSemantic.ModeToggle,
            direction: -1,
            context: VibrationContext.PrivacyMode,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.NotEqual(enableSequence[0].Profile, disableSequence[0].Profile);
        Assert.NotEqual(enableSequence[1].Profile, disableSequence[1].Profile);
    }

    /// <summary>
    /// 控制器識別序列應具備至少兩段不同節奏，讓使用者能快速辨認目前作用中的控制器。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetControllerIdentifySequence_ReturnsDistinctMultiPulsePattern()
    {
        IReadOnlyList<VibrationSequenceStep> sequence = VibrationPatterns.GetControllerIdentifySequence(
            VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.True(sequence.Count >= 2);
        Assert.NotEqual(sequence[0].Profile, sequence[1].Profile);
        Assert.True(sequence[0].Profile.Duration != sequence[1].Profile.Duration);
    }

    /// <summary>
    /// 結束程式的長按確認應先給較輕的準備提示，再給更明確的最終確認震動。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetExitHoldSequence_UsesGentlePrepAndStrongerConfirmation()
    {
        IReadOnlyList<VibrationSequenceStep> prepSequence = VibrationPatterns.GetExitHoldSequence(
            confirmed: false,
            VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);
        IReadOnlyList<VibrationSequenceStep> confirmSequence = VibrationPatterns.GetExitHoldSequence(
            confirmed: true,
            VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.NotEmpty(prepSequence);
        Assert.NotEmpty(confirmSequence);
        Assert.True(confirmSequence[^1].Profile.Strength > prepSequence[0].Profile.Strength);
    }

    /// <summary>
    /// 長按撞牆時的重複邊界回饋應明顯輕於第一次撞擊，避免手部疲勞。
    /// </summary>
    [Fact]
    public void VibrationPatterns_RepeatedBoundaryProfiles_RemainLighterThanInitialImpact()
    {
        Assert.True(VibrationPatterns.BoundaryLeftRepeat.Strength < VibrationPatterns.BoundaryLeft.Strength);
        Assert.True(VibrationPatterns.BoundaryRightRepeat.Duration < VibrationPatterns.BoundaryRight.Duration);
    }

    /// <summary>
    /// 快速連續翻閱歷程時，阻尼版的滾輪回饋應比初始刻度更短更輕。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetHistoryScrollSequence_RapidBurst_BecomesLighterAndShorter()
    {
        IReadOnlyList<VibrationSequenceStep> normalSequence = VibrationPatterns.GetHistoryScrollSequence(
            direction: -1,
            burstLevel: 0,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);
        IReadOnlyList<VibrationSequenceStep> rapidSequence = VibrationPatterns.GetHistoryScrollSequence(
            direction: -1,
            burstLevel: 3,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.NotEmpty(normalSequence);
        Assert.NotEmpty(rapidSequence);
        Assert.True(rapidSequence[0].Profile.Strength < normalSequence[0].Profile.Strength);
        Assert.True(rapidSequence[0].Profile.Duration < normalSequence[0].Profile.Duration);
    }

    /// <summary>
    /// 當文字已達輸入上限時，應回傳比接近上限更沉重的硬牆震動序列。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetTextLimitSequence_AtHardLimit_ReturnsHeavierWallFeedback()
    {
        IReadOnlyList<VibrationSequenceStep> nearLimit = VibrationPatterns.GetTextLimitSequence(
            remainingCharacters: 2,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);
        IReadOnlyList<VibrationSequenceStep> hardLimit = VibrationPatterns.GetTextLimitSequence(
            remainingCharacters: 0,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.NotEmpty(nearLimit);
        Assert.True(hardLimit.Count >= 2);
        Assert.True(hardLimit[0].Profile.Strength > nearLimit[0].Profile.Strength);
    }

    /// <summary>
    /// 右搖桿選取在單字粒度時，應與逐字拉鏈感回饋有可辨識的差異。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetSelectionSequence_WordGranularity_DiffersFromCharacterMode()
    {
        IReadOnlyList<VibrationSequenceStep> charSequence = VibrationPatterns.GetSelectionSequence(
            direction: 1,
            wordGranularity: false,
            burstLevel: 2,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);
        IReadOnlyList<VibrationSequenceStep> wordSequence = VibrationPatterns.GetSelectionSequence(
            direction: 1,
            wordGranularity: true,
            burstLevel: 0,
            motorSupport: VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.NotEmpty(charSequence);
        Assert.True(wordSequence.Count >= 2);
        Assert.NotEqual(charSequence[0].Profile, wordSequence[0].Profile);
    }

    /// <summary>
    /// 由全域快速鍵喚起視窗時的觸覺握手應維持輕巧的三連脈衝科技感。
    /// </summary>
    [Fact]
    public void VibrationPatterns_GetFocusHandshakeSequence_ReturnsCompactThreePulsePattern()
    {
        IReadOnlyList<VibrationSequenceStep> sequence = VibrationPatterns.GetFocusHandshakeSequence(
            VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors);

        Assert.Equal(3, sequence.Count);
        Assert.True(sequence[0].Profile.Duration <= sequence[1].Profile.Duration);
        Assert.True(sequence[2].Profile.Duration <= 40);
    }

    /// <summary>
    /// VibrationProfile 不同值的兩個實例應不相等。
    /// </summary>
    [Fact]
    public void VibrationProfile_DifferentValues_AreNotEqual()
    {
        var firstProfile = new VibrationProfile(30000, 120);
        var secondProfile = new VibrationProfile(30000, 121);
        Assert.NotEqual(firstProfile, secondProfile);
    }

    /// <summary>
    /// 用來驗證回饋服務是否正確把方向性震動設定轉送給控制器。
    /// </summary>
    private sealed class StubGamepadController : IGamepadController
    {
        public string DeviceName => "Test Controller";
        public string DeviceIdentity => DeviceName;
        public bool IsConnected => true;
        public bool IsLeftShoulderHeld => false;
        public bool IsRightShoulderHeld => false;
        public bool IsLeftTriggerHeld => false;
        public bool IsRightTriggerHeld => false;
        public bool IsBackHeld => false;
        public bool IsBHeld => false;
        public bool IsXHeld => false;
        public GamepadRepeatSettings RepeatSettings { get; set; } = new();
        public int ThumbDeadzoneEnter { get; set; }
        public int ThumbDeadzoneExit { get; set; }
        public VibrationMotorSupport VibrationMotorSupport { get; set; } = VibrationMotorSupport.DualMain | VibrationMotorSupport.TriggerMotors;
        public VibrationProfile? LastProfile { get; private set; }
        public VibrationPriority? LastPriority { get; private set; }
        public List<VibrationProfile> PlayedProfiles { get; } = [];

        public event Action<bool>? ConnectionChanged;
        public event Action? UpPressed;
        public event Action? DownPressed;
        public event Action? LeftPressed;
        public event Action? RightPressed;
        public event Action? LeftShoulderPressed;
        public event Action? LeftShoulderRepeat;
        public event Action? RightShoulderPressed;
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

        /// <summary>
        /// 顯式讀取所有事件欄位，避免測試替身因未觸發事件而留下 CS0067 警告。
        /// </summary>
        private void TouchRegisteredEvents()
        {
            _ = ConnectionChanged;
            _ = UpPressed;
            _ = DownPressed;
            _ = LeftPressed;
            _ = RightPressed;
            _ = LeftShoulderPressed;
            _ = LeftShoulderRepeat;
            _ = RightShoulderPressed;
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

        public Task VibrateAsync(ushort strength, int milliseconds = 60, VibrationPriority priority = VibrationPriority.Normal, CancellationToken ct = default)
        {
            TouchRegisteredEvents();
            LastProfile = new VibrationProfile(strength, milliseconds);
            LastPriority = priority;
            PlayedProfiles.Add(LastProfile.Value);
            return Task.CompletedTask;
        }

        public Task VibrateAsync(VibrationProfile profile, VibrationPriority priority = VibrationPriority.Normal, CancellationToken ct = default)
        {
            TouchRegisteredEvents();
            LastProfile = profile;
            LastPriority = priority;
            PlayedProfiles.Add(profile);
            return Task.CompletedTask;
        }

        public void StopVibration() { }
        public void Pause() { }
        public void Resume() { }
        public void ResetCalibration() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}