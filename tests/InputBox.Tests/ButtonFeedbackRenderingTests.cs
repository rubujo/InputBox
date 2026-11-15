using InputBox.Core.Extensions;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Xunit;

namespace InputBox.Tests;

/// <summary>Standard／Latest 的原生表面、必要提示及完整互動交接像素驗證。</summary>
[Collection(UiSmokeTestRequirements.CollectionName)]
[Trait("Category", "UI")]
public sealed class ButtonFeedbackRenderingTests
{
    static ButtonFeedbackRenderingTests()
    {
        Application.SetHighDpiMode(Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_TEST_96DPI") == "1" ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetColorMode(Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_TEST_DARK") == "1" ? SystemColorMode.Dark : SystemColorMode.Classic);
        Application.SetDefaultVisualStylesMode(VisualStylesMode.Latest);
    }

    /// <summary>驗證預設與一般按鈕各互動狀態的實際文字、提示及按壓對比。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, "focus")][InlineData(false, "focus")]
    [InlineData(true, "hover")][InlineData(false, "hover")]
    [InlineData(true, "pressed")][InlineData(false, "pressed")]
    [InlineData(true, "focus-hover")][InlineData(false, "focus-hover")]
    [InlineData(true, "keyboard-pressed")][InlineData(false, "keyboard-pressed")]
    [InlineData(true, "keyboard-release")][InlineData(false, "keyboard-release")]
    [InlineData(true, "keyboard-cancel")][InlineData(false, "keyboard-cancel")]
    public void Render_InteractiveState_PreservesContrast(bool isDefault, string state) => RunUi(isDefault, async scene =>
    {
        ProbeButton button = scene.Button;
        bool hover = state is "hover" or "focus-hover";
        bool pressed = state is "pressed" or "keyboard-pressed";
        if (state != "hover") { Assert.True(button.Focus()); }
        if (hover) { button.EnterHover(); }
        if (state == "pressed") { button.PressMouse(); }
        if (state.StartsWith("keyboard", StringComparison.Ordinal))
        {
            button.PressSpace();
            if (state == "keyboard-release") { button.ReleaseSpace(); }
            if (state == "keyboard-cancel") { scene.Input.Focus(); button.Focus(); }
        }
        button.RefreshSystemColors();
        await Task.Delay(50);
        using Bitmap image = Render(button);
        Color actualBack = image.GetPixel(25, 20);
        AssertText(button, 4.5);
        AssertAmbient(button);
        Assert.Equal(!hover, button.Font.Bold);
        if (!hover)
        {
            bool dark = button.IsDarkModeActive();
            Color expectedBack = SystemInformation.HighContrast ? (pressed ? SystemColors.HighlightText : SystemColors.Highlight) :
                pressed ? (dark ? Color.FromArgb(255, 200, 120) : Color.FromArgb(28, 28, 28)) : dark ? Color.White : Color.Black;
            Assert.Equal(expectedBack.ToArgb(), actualBack.ToArgb());
            double cue = Contrast(image.GetPixel(2, 20), image.GetPixel(0, 20));
            double minimum = SystemInformation.HighContrast || pressed ? 3 : dark ? 11.16 : 16.75;
            Assert.True(cue >= 3 && cue + .005 >= minimum);
            if (pressed)
            {
                double inner = Enumerable.Range(5, 6).Max(x => Contrast(actualBack, image.GetPixel(x, 20)));
                Assert.True(inner >= 3, "按壓缺少可見內框，包括高對比。");
                if (dark && !SystemInformation.HighContrast) { Assert.True(100 - TritanLightness(actualBack) >= 13); }
            }
        }
        else
        {
            double cue = Contrast(image.GetPixel(2, 20), image.GetPixel(0, 20));
            double minimum = SystemInformation.HighContrast ? 3 : button.IsDarkModeActive() ? 7.2 : 8.14;
            Assert.True(cue >= 3 && cue + .005 >= minimum);
        }
        Save(image, $"{isDefault}-{state}");
    });

    /// <summary>驗證中性與停用狀態保留原生表面和可辨識文字。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, true)][InlineData(false, true)][InlineData(true, false)][InlineData(false, false)]
    public void Render_NeutralAndDisabled_KeepNativeSurface(bool isDefault, bool enabled) => RunUi(isDefault, async scene =>
    {
        scene.Button.Enabled = enabled;
        await Task.Delay(450);
        AssertAmbient(scene.Button);
        AssertText(scene.Button, enabled ? 4.5 : 1.5);
    });

    /// <summary>驗證預設動作提示與實際輸入焦點的情境色一致。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true)][InlineData(false)]
    public void Render_DefaultAction_FollowsInputContext(bool strong) => RunUi(true, async scene =>
    {
        bool dark = scene.Button.IsDarkModeActive();
        scene.Input.BackColor = strong ? (dark ? Color.White : Color.Black) : Color.Empty;
        scene.Button.Invalidate();
        await Task.Delay(30);
        using Bitmap image = Render(scene.Button);
        (Color border, Color backing) = ControlExtensions.GetContextualFocusColors(scene.Input.BackColor, dark);
        Assert.Equal(border.ToArgb(), image.GetPixel(2, 20).ToArgb());
        Assert.Equal(backing.ToArgb(), image.GetPixel(0, 20).ToArgb());
        Assert.True(Contrast(image.GetPixel(2, 20), image.GetPixel(0, 20)) >= 3);
        AssertAmbient(scene.Button);
    });

    /// <summary>驗證部分與完整 Dwell 皆保留可見襯底及足夠相鄰對比。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, false)][InlineData(false, false)][InlineData(true, true)][InlineData(false, true)]
    public void Render_Dwell_HasVisibleBackingAtPartialAndFullProgress(bool isDefault, bool complete) => RunUi(isDefault, async scene =>
    {
        ProbeButton button = scene.Button;
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        button.EnterHover();
        await WaitProgress(button, complete ? 1 : .4f);
        using Bitmap image = Render(button);
        float scale = button.DeviceDpi / 96f;
        int inset = (int)Math.Ceiling(7 * scale);
        int backing = (int)Math.Max(1, Math.Ceiling(scale));
        int height = (int)Math.Ceiling(6 * scale) + 2 * backing;
        int top = button.Height - inset - height;
        Color track = image.GetPixel(inset, top);
        bool dark = button.IsDarkModeActive();
        Color fill = SystemInformation.HighContrast ? SystemColors.HighlightText : dark ? Color.LimeGreen : Color.Green;
        Color expectedTrack = SystemInformation.HighContrast ? SystemColors.Highlight : dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);
        Assert.Equal(expectedTrack.ToArgb(), track.ToArgb());
        Assert.Equal(track.ToArgb(), image.GetPixel(inset, top + height - 1).ToArgb());
        Assert.Equal(track.ToArgb(), image.GetPixel(button.Width - inset - 1, top + height / 2).ToArgb());
        Assert.Equal(track.ToArgb(), image.GetPixel(inset, top + height / 2).ToArgb());
        int ink = 0;
        for (int y = top + backing; y < top + height - backing; y++)
        {
            for (int x = inset + backing; x < inset + backing + 40; x++)
            {
                if (image.GetPixel(x, y).ToArgb() == fill.ToArgb()) { ink++; }
            }
        }
        Assert.True(ink > 20);
        double ratio = Contrast(fill, track);
        double minimum = SystemInformation.HighContrast ? 3 : dark ? 5.21 : 3.75;
        Assert.True(ratio >= 3 && ratio + .005 >= minimum);
        Assert.Equal(0, clicks);
        AssertAmbient(button);
        AssertText(button, 4.5);
        Save(image, $"dwell-{isDefault}-{complete}");
    });

    /// <summary>驗證鍵盤與滑鼠交錯、Capture 取消及生命週期清理不殘留回饋。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, "space-leave")][InlineData(false, "space-leave")]
    [InlineData(true, "pointer-space-release")][InlineData(false, "pointer-space-release")]
    [InlineData(true, "capture")][InlineData(false, "capture")]
    [InlineData(true, "disable")][InlineData(false, "disable")]
    [InlineData(true, "cancel")][InlineData(false, "cancel")]
    [InlineData(true, "reconfigure")][InlineData(false, "reconfigure")]
    [InlineData(true, "lifetime")][InlineData(false, "lifetime")]
    public void Interaction_InterleavingAndLifecycle_DoNotLeaveFeedback(bool isDefault, string action) => RunUi(isDefault, async scene =>
    {
        ProbeButton button = scene.Button;
        if (action is "space-leave" or "pointer-space-release")
        {
            button.Focus(); button.PressSpace(); button.EnterHover();
            if (action == "space-leave") { button.ExitHover(); }
            else { button.Capture = true; button.PressMouse(); button.ReleaseMouse(); }
            Assert.True(button.Pressed, "滑鼠狀態不應清除仍按住的 Space。");
            button.ReleaseSpace();
            Assert.False(button.Pressed);
        }
        else if (action == "capture")
        {
            button.EnterHover(); button.Capture = true;
            Assert.True(button.Capture);
            button.PressMouse(); Assert.True(button.Pressed);
            button.Capture = false;
            Assert.False(button.Pressed);
            button.ReleaseMouse();
        }
        else
        {
            button.EnterHover(); await WaitProgress(button, .2f);
            if (action == "disable")
            {
                button.Enabled = false; Assert.Equal(0, button.Progress); Assert.False(button.Pressed);
                button.Enabled = true; Assert.Equal(0, button.Progress);
            }
            else if (action == "cancel")
            {
                Task pending = button.CompleteClickAndReGazeAsync();
                button.CancelDwellAnimation(); await pending; await Task.Delay(60);
                Assert.Equal(0, button.Progress);
            }
            else if (action == "lifetime")
            {
                scene.Lifetime.Cancel(); await Task.Delay(60);
                Assert.Equal(0, button.Progress);
            }
            else
            {
                float progress = button.Progress;
                button.AttachEyeTrackerFeedback("Updated", scene.Regular, scene.Bold, scene.Lifetime.Token);
                button.UpdateEyeTrackerFonts(scene.Regular, scene.Bold);
                button.RefreshSystemColors(); await Task.Delay(25);
                Assert.True(button.Progress >= progress, "更新字型或系統色不應重新開始凝視。");
                Assert.Contains("Updated", button.AccessibleDescription);
            }
        }
        AssertAmbient(button);
        AssertText(button, 4.5);
    });

    /// <summary>逐幀驗證視窗有無焦點時懸停過渡的對比與版面不變。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, true)][InlineData(true, false)][InlineData(false, true)][InlineData(false, false)]
    public void Render_Transition_EverySamplePreservesContrastAndGeometry(bool isDefault, bool active) => RunUi(isDefault, async scene =>
    {
        ProbeButton button = scene.Button;
        using Form other = new() { StartPosition = FormStartPosition.Manual, Location = new Point(850, 550), Size = new Size(100, 80) };
        if (!active) { other.Show(); other.Activate(); }
        Form target = active ? scene.Form : other;
        _ = await WindowFocusService.RestoreWindowAsync(target.Handle, TestContext.Current.CancellationToken);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            _ = User32.ShowWindow(target.Handle, User32.ShowWindowCommand.Restore);
            _ = User32.SetForegroundWindow(target.Handle);
            target.Activate();
            await Task.Delay(60);
            if ((Form.ActiveForm == scene.Form) == active) { break; }
        }
        Assert.Equal(active, Form.ActiveForm == scene.Form);
        Size size = button.Size; Padding padding = button.Padding; Padding margin = button.Margin; nint handle = button.Handle;
        for (int phase = 0; phase < 4; phase++)
        {
            if (phase is 0 or 2) { button.EnterHover(); } else { button.ExitHover(); }
            for (int i = 0; i < 25; i++)
            {
                AssertText(button, 4.5);
                AssertAmbient(button);
                Assert.Equal(size, button.Size); Assert.Equal(padding, button.Padding);
                Assert.Equal(margin, button.Margin); Assert.Equal(handle, button.Handle);
                if (isDefault || phase is 0 or 2)
                {
                    using Bitmap image = Render(button);
                    Assert.True(Contrast(image.GetPixel(2, 20), image.GetPixel(0, 20)) >= 3);
                }
                await Task.Delay(16);
            }
        }
        other.Close();
    });

    /// <summary>驗證文字量測會拒絕空標題，避免僅量到裝飾的假陽性。</summary>
    [Fact(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    public void Render_EmptyCaption_DoesNotPassTextCheck() => RunUi(false, scene =>
    {
        scene.Button.Enabled = false; scene.Button.Text = string.Empty;
        Assert.Throws<Xunit.Sdk.TrueException>(() => AssertText(scene.Button, 1.5));
        return Task.CompletedTask;
    });

    /// <summary>驗證七語系及左右文字方向下的實際文字對比與內距。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData("zh-Hant")][InlineData("en")][InlineData("de")][InlineData("fr")][InlineData("ja")][InlineData("ko")][InlineData("zh-Hans")]
    public void Render_LocalizedAndRtlCaption_PreservesTextAndPadding(string culture) => RunUi(true, async scene =>
    {
        scene.Button.Text = Strings.ResourceManager.GetString("Btn_CopyDefault", new CultureInfo(culture))!;
        Assert.False(string.IsNullOrWhiteSpace(scene.Button.Text));
        Padding original = scene.Button.Padding;
        foreach (RightToLeft rtl in new[] { RightToLeft.No, RightToLeft.Yes })
        {
            scene.Button.RightToLeft = rtl; scene.Button.Focus();
            await Task.Delay(30); AssertText(scene.Button, 4.5);
            Assert.Equal(original, scene.Button.Padding); AssertAmbient(scene.Button);
            scene.Input.Focus();
        }
    });

    /// <summary>驗證舊點擊的非同步重新接合不能覆寫新懸停狀態。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true)][InlineData(false)]
    public void Interaction_StaleClickRegaze_DoesNotReplaceNewHover(bool isDefault) => RunUi(isDefault, async scene =>
    {
        List<bool> notifications = [];
        scene.Button.AttachEyeTrackerFeedback("Button", scene.Regular, scene.Bold, scene.Lifetime.Token, notifications.Add);
        Task oldClick = scene.Button.CompleteClickAndReGazeAsync();
        scene.Button.EnterHover();
        await oldClick;
        await WaitProgress(scene.Button, .1f);
        Assert.True(notifications[^1]);
        scene.Button.ExitHover();
        Assert.False(notifications[^1]);
        Assert.Equal(0, scene.Button.Progress);
        AssertAmbient(scene.Button);
    });
    /// <summary>滿格或停用 UI 特效後，取消仍須清除提示，換權杖及處置也不得殘留註冊。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, "cancel")][InlineData(false, "cancel")]
    [InlineData(true, "rebind")][InlineData(false, "rebind")]
    [InlineData(true, "dispose")][InlineData(false, "dispose")]
    public void Interaction_CompletedDwellLifetime_ClearsWithoutTimer(bool isDefault, string action) => RunUi(isDefault, async scene =>
    {
        ProbeButton button = scene.Button;
        button.EnterHover();
        await WaitProgress(button, 1);
        using CancellationTokenSource replacement = new();
        CancellationTokenSource cancellation = scene.Lifetime;
        if (action == "rebind")
        {
            button.AttachEyeTrackerFeedback("Replacement", scene.Regular, scene.Bold, replacement.Token);
            await Task.Run(scene.Lifetime.Cancel, TestContext.Current.CancellationToken);
            await Task.Delay(60);
            Assert.Equal(1, button.Progress);
            cancellation = replacement;
        }
        if (action == "dispose") { button.Dispose(); }
        await Task.Run(cancellation.Cancel, TestContext.Current.CancellationToken);
        for (int i = 0; i < 100 && button.Progress > 0; i++) { await Task.Delay(16); }
        Assert.Equal(0, button.Progress);
        if (!button.IsDisposed) { AssertAmbient(button); AssertText(button, 4.5); }
    });

    /// <summary>以訊息框的 AutoSize 與最小尺寸規則驗證七語系較矮按鈕，完整 Dwell 不得遮字或抖動。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, "zh-Hant")][InlineData(false, "zh-Hant")]
    [InlineData(true, "en")][InlineData(false, "en")]
    [InlineData(true, "de")][InlineData(false, "de")]
    [InlineData(true, "fr")][InlineData(false, "fr")]
    [InlineData(true, "ja")][InlineData(false, "ja")]
    [InlineData(true, "ko")][InlineData(false, "ko")]
    [InlineData(true, "zh-Hans")][InlineData(false, "zh-Hans")]
    public void Render_MessageBoxSizedDwell_PreservesEveryCaptionPixel(bool isDefault, string culture) => RunUi(isDefault, async scene =>
    {
        ProbeButton button = scene.Button;
        button.Text = Strings.ResourceManager.GetString("Btn_CopyDefault", new CultureInfo(culture))!;
        button.Padding = Padding.Empty;
        button.AutoSize = true;
        float scale = button.DeviceDpi / 96f;
        InputBox.Core.Utilities.DialogLayoutHelper.UpdateButtonMinimumSize(button, scene.Bold, scale, 88, 0, 24, 8);
        button.Size = button.GetPreferredSize(Size.Empty);
        button.AutoSize = false;
        Size size = button.Size; Padding padding = button.Padding; Padding margin = button.Margin; nint handle = button.Handle;
        button.EnterHover();
        HashSet<Point> caption = GetCaptionPixels(button);
        Assert.True(caption.Count > 10);
        int trackTop = button.Height - (int)Math.Ceiling(7 * scale) - (int)Math.Ceiling(6 * scale) - 2 * (int)Math.Max(1, Math.Ceiling(scale));
        Assert.True(caption.Max(point => point.Y) < trackTop, "原生標題與 Dwell 區域重疊。");
        for (int phase = 0; phase < 2; phase++)
        {
            float progress = phase == 0 ? .4f : 1f;
            await WaitProgress(button, progress);
            Assert.True(caption.IsSubsetOf(GetCaptionPixels(button)), "Dwell 遮住部分標題像素。");
            AssertText(button, 4.5); AssertAmbient(button);
            Assert.Equal(size, button.Size); Assert.Equal(padding, button.Padding);
            Assert.Equal(margin, button.Margin); Assert.Equal(handle, button.Handle);
        }
        using Bitmap image = Render(button);
        Save(image, $"compact-{isDefault}-{culture}");
    });

    private static HashSet<Point> GetCaptionPixels(Button button)
    {
        string caption = button.Text;
        button.Text = string.Empty; using Bitmap before = Render(button);
        button.Text = caption; using Bitmap image = Render(button);
        button.Text = string.Empty; using Bitmap after = Render(button);
        button.Text = caption;
        HashSet<Point> pixels = [];
        for (int y = 0; y < button.Height; y++)
        {
            for (int x = 0; x < button.Width; x++)
            {
                int background = before.GetPixel(x, y).ToArgb();
                if (background == after.GetPixel(x, y).ToArgb() && background != image.GetPixel(x, y).ToArgb())
                {
                    pixels.Add(new Point(x, y));
                }
            }
        }
        return pixels;
    }

    private static void AssertAmbient(ProbeButton button)
    {
        Assert.Equal(FlatStyle.Standard, button.FlatStyle);
        Assert.False(TypeDescriptor.GetProperties(button)[nameof(button.BackColor)]!.ShouldSerializeValue(button));
        Assert.False(TypeDescriptor.GetProperties(button)[nameof(button.ForeColor)]!.ShouldSerializeValue(button));
        Assert.Equal(SystemInformation.HighContrast ? VisualStylesMode.Classic : VisualStylesMode.Latest, button.ResolvedMode);
    }

    private static Bitmap Render(Button button)
    {
        Bitmap image = new(button.Width, button.Height);
        button.DrawToBitmap(image, button.ClientRectangle);
        return image;
    }

    private static void AssertText(Button button, double minimum)
    {
        string caption = button.Text;
        button.Text = string.Empty; using Bitmap before = Render(button);
        button.Text = caption; using Bitmap image = Render(button);
        button.Text = string.Empty; using Bitmap after = Render(button);
        button.Text = caption;
        Dictionary<int, int> colors = [];
        for (int y = button.Height / 4; y < button.Height * 3 / 4; y++)
        {
            for (int x = 25; x < button.Width - 25; x++)
            {
                int pixel = image.GetPixel(x, y).ToArgb();
                if (before.GetPixel(x, y).ToArgb() != after.GetPixel(x, y).ToArgb() || pixel == before.GetPixel(x, y).ToArgb()) { continue; }
                colors.TryGetValue(pixel, out int count); colors[pixel] = count + 1;
            }
        }
        Assert.True(colors.Values.Sum() > 10, "沒有找到實際文字墨色。");
        Color ink = Color.FromArgb(colors.MaxBy(pair => pair.Value).Key);
        double ratio = Contrast(image.GetPixel(25, 20), ink);
        Assert.True(ratio >= minimum, $"文字對比 {ratio:F3} < {minimum}");
    }

    private static async Task WaitProgress(ProbeButton button, float minimum)
    {
        for (int i = 0; i < 200 && button.Progress < minimum; i++) { await Task.Delay(16); }
        Assert.True(button.Progress >= minimum, "Dwell 未到達驗證進度。");
    }

    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) { double s = value / 255.0; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        static double Lum(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        double first = Lum(a); double second = Lum(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static double TritanLightness(Color c)
    {
        static double Linear(byte v) { double s = v / 255.0; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        return 116 * Math.Cbrt((.2126 * Linear(c.R) + .7152 * Linear(c.G)) / .9278) - 16;
    }

    private static void Save(Bitmap image, string name)
    {
        string? path = Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_ARTIFACTS");
        if (path is null) { return; }
        Directory.CreateDirectory(path); image.Save(Path.Combine(path, name + ".png"));
    }

    private static void RunUi(bool isDefault, Func<Scene, Task> action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using Scene scene = new(isDefault);
                scene.Form.Shown += async (_, _) =>
                {
                    try
                    {
                        _ = User32.ShowWindow(scene.Form.Handle, User32.ShowWindowCommand.Restore);
                        _ = User32.SetForegroundWindow(scene.Form.Handle);
                        _ = await WindowFocusService.RestoreWindowAsync(scene.Form.Handle, TestContext.Current.CancellationToken);
                        scene.Form.Activate(); scene.Input.Focus(); await Task.Delay(40);
                        await action(scene);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { scene.Form.Close(); }
                };
                Application.Run(scene.Form);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private sealed class Scene : IDisposable
    {
        internal Form Form { get; } = new() { ClientSize = new Size(410, 250), StartPosition = FormStartPosition.Manual, Location = new Point(10, 10), TopMost = true };
        internal TextBox Input { get; } = new() { Bounds = new Rectangle(10, 195, 280, 30) };
        internal ProbeButton Button { get; } = new() { Text = "Button text", Bounds = new Rectangle(10, 50, 370, 120), Padding = new Padding(7) };
        internal Font Regular { get; } = new("Segoe UI", 18);
        internal Font Bold { get; } = new("Segoe UI", 18, FontStyle.Bold);
        internal CancellationTokenSource Lifetime { get; } = new();
        internal Scene(bool isDefault)
        {
            Button.Font = Regular; Form.Controls.AddRange([Input, Button]);
            if (Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_TEST_96DPI") == "1") { Assert.Equal(96, Button.DeviceDpi); }
            Button.AttachEyeTrackerFeedback("Button", Regular, Bold, Lifetime.Token);
            if (isDefault) { Form.AcceptButton = Button; }
            if (Form.Bounds.Contains(Cursor.Position)) { Form.Location = new Point(30, 450); }
        }
        public void Dispose() { Lifetime.Cancel(); Form.Dispose(); Lifetime.Dispose(); Regular.Dispose(); Bold.Dispose(); }
    }

    private sealed class ProbeButton : InputBox.Core.Controls.FeedbackButton
    {
        internal VisualStylesMode ResolvedMode => EffectiveVisualStylesMode;
        internal void EnterHover() => OnMouseEnter(EventArgs.Empty);
        internal void ExitHover() => OnMouseLeave(EventArgs.Empty);
        internal void PressMouse() => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 30, 30, 0));
        internal void ReleaseMouse() => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 30, 30, 0));
        internal void PressSpace() => OnKeyDown(new KeyEventArgs(Keys.Space));
        internal void ReleaseSpace() => OnKeyUp(new KeyEventArgs(Keys.Space));
        internal void RefreshSystemColors() => OnSystemColorsChanged(EventArgs.Empty);
    }
}
