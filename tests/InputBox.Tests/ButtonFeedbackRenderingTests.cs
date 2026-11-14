using InputBox.Core.Extensions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證 Latest 預設按鈕的實際像素與自訂互動配色一致，避免強調色覆寫背景。
/// </summary>
[Collection(UiSmokeTestRequirements.CollectionName)]
[Trait("Category", "UI")]
public sealed class ButtonFeedbackRenderingTests
{
    /// <summary>
    /// 在獨立測試程序中選擇淺色或深色，讓同一組像素檢查覆蓋兩種渲染路徑。
    /// </summary>
    static ButtonFeedbackRenderingTests()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetColorMode(Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_TEST_DARK") == "1" ?
            SystemColorMode.Dark : SystemColorMode.Classic);
    }

    /// <summary>
    /// 預設與一般按鈕的焦點、懸停、按下背景及文字應成對呈現，且互動不改變尺寸或內距。
    /// </summary>
    [Theory(Skip = "Requires explicit UI test opt-in.",
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, "focus")]
    [InlineData(true, "hover")]
    [InlineData(true, "pressed")]
    [InlineData(false, "focus")]
    [InlineData(false, "hover")]
    [InlineData(false, "pressed")]
    public void Render_InteractiveState_PreservesCustomContrast(bool isDefault, string state)
    {
        RunSta(() =>
        {
            using var form = new Form();
            using var input = new TextBox();
            using var bold = new Font(form.Font, FontStyle.Bold);
            using var button = new FeedbackButton
            {
                Text = "Button text",
                Bounds = new Rectangle(10, 50, 240, 90),
                FlatStyle = FlatStyle.Flat,
                VisualStylesMode = VisualStylesMode.Latest,
                Padding = new Padding(7)
            };
            using var lifetime = new CancellationTokenSource();
            form.Controls.AddRange([input, button]);
            button.AttachEyeTrackerFeedback("Button", button.Font, bold, lifetime.Token);
            if (isDefault)
            {
                form.AcceptButton = button;
            }

            form.Show();
            form.Activate();
            Application.DoEvents();
            input.Focus();
            if (state == "focus")
            {
                Assert.True(button.Focus());
            }
            else if (state == "hover")
            {
                button.Hover();
                Assert.NotEqual(form.BackColor, button.BackColor);
            }
            else
            {
                Assert.True(button.Focus());
                button.Press();
            }

            button.BackColor = Color.Magenta;
            button.ForeColor = Color.Magenta;
            button.RefreshSystemColors();
            Assert.NotEqual(Color.Magenta, button.BackColor);

            using var bitmap = new Bitmap(button.Width, button.Height);
            button.DrawToBitmap(bitmap, button.ClientRectangle);
            string? artifacts = Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_ARTIFACTS");
            if (artifacts != null)
            {
                Directory.CreateDirectory(artifacts);
                bitmap.Save(Path.Combine(artifacts, $"{isDefault}-{state}.png"));
            }
            Assert.Equal(button.BackColor.ToArgb(), bitmap.GetPixel(25, 20).ToArgb());
            int textPixels = 0;
            for (int y = 25; y < 65; y++)
            {
                for (int x = 35; x < 205; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == button.ForeColor.ToArgb())
                    {
                        textPixels++;
                    }
                }
            }

            Color border = button.GetButtonInteractiveBorderColor(state != "hover", button.IsDarkModeActive());
            Assert.Equal(border.ToArgb(), bitmap.GetPixel(2, 20).ToArgb());
            double borderMinimum = SystemInformation.HighContrast || state == "pressed" ? 3 :
                state == "focus" ? (button.IsDarkModeActive() ? 11.16 : 16.75) :
                (button.IsDarkModeActive() ? 7.2 : 8.14);
            double borderContrast = Contrast(bitmap.GetPixel(2, 20), bitmap.GetPixel(0, 20));
            Assert.True(borderContrast >= 3 && borderContrast + 0.005 >= borderMinimum);
            Assert.Equal(state != "hover", button.Font.Bold);
            if (state == "pressed" && button.IsDarkModeActive() && !SystemInformation.HighContrast)
            {
                double red = LinearComponent(button.BackColor.R);
                double green = LinearComponent(button.BackColor.G);
                double tritanLuminance = (red * 0.2126 + green * 0.7152) / 0.9278;
                double lightness = 116 * Math.Cbrt(tritanLuminance) - 16;
                Assert.True(100 - lightness >= 13);
            }
            Assert.True(Contrast(button.BackColor, button.ForeColor) >= 4.5);
            Assert.True(textPixels > 10, "自訂文字色沒有實際繪製到按鈕。");
            Assert.Equal(new Padding(7), button.Padding);
            Assert.Equal(new Size(240, 90), button.Size);
            Assert.Equal(SystemInformation.HighContrast ? VisualStylesMode.Classic : VisualStylesMode.Latest, button.ResolvedMode);
            lifetime.Cancel();
        });
    }

    /// <summary>
    /// 回饋結束後必須還原環境配色，不得留下硬編碼顏色或破壞主題渲染。
    /// </summary>
    [Theory(Skip = "Requires explicit UI test opt-in.",
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_AfterMouseLeave_RestoresAmbientColors(bool isDefault)
    {
        RunSta(() =>
        {
            using var form = new Form();
            using var input = new TextBox();
            using var button = new FeedbackButton
            {
                Text = "Button text", Bounds = new Rectangle(10, 50, 240, 90),
                FlatStyle = FlatStyle.Flat, VisualStylesMode = VisualStylesMode.Latest
            };
            using var lifetime = new CancellationTokenSource();
            form.Controls.AddRange([input, button]);
            button.AttachEyeTrackerFeedback("Button", null, null, lifetime.Token);
            if (isDefault)
            {
                form.AcceptButton = button;
            }

            form.Show();
            form.Activate();
            Application.DoEvents();
            input.Focus();
            button.Press();
            button.ExitHover();
            Assert.Equal(form.BackColor, button.BackColor);
            Assert.Equal(form.ForeColor, button.ForeColor);
            CheckNeutralRendering(button, isDefault);
            button.Enabled = false;
            CheckNeutralRendering(button, isDefault);
            lifetime.Cancel();
        });
    }

    /// <summary>
    /// 預設動作邊框須跟隨輸入框反轉／中性背景，並在實際相鄰襯底達到專案對比目標。
    /// </summary>
    [Theory(Skip = "Requires explicit UI test opt-in.",
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_DefaultAction_MatchesInputContextWithContrast(bool strong)
    {
        RunSta(() =>
        {
            bool dark = Application.IsDarkModeEnabled;
            Color contextColor = strong ? (dark ? Color.White : Color.Black) :
                (dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220));
            using var form = new Form();
            using var input = new TextBox { BackColor = contextColor };
            using var button = new FeedbackButton
            {
                Text = "Button text", Bounds = new Rectangle(10, 50, 240, 90),
                FlatStyle = FlatStyle.Flat, VisualStylesMode = VisualStylesMode.Latest
            };
            using var lifetime = new CancellationTokenSource();
            form.Controls.AddRange([input, button]);
            form.AcceptButton = button;
            button.AttachEyeTrackerFeedback("Button", null, null, lifetime.Token);
            form.Show();
            Assert.True(input.Focus());
            Assert.Same(input, form.ActiveControl);
            Color expected = SystemInformation.HighContrast ? SystemColors.Highlight :
                strong ? (dark ? Color.MediumBlue : Color.Cyan) :
                (dark ? Color.LightBlue : Color.MediumBlue);
            using var bitmap = new Bitmap(button.Width, button.Height);
            button.DrawToBitmap(bitmap, button.ClientRectangle);
            Assert.Equal(expected.ToArgb(), bitmap.GetPixel(2, 20).ToArgb());
            double minimum = SystemInformation.HighContrast ? 3 :
                strong ? (dark ? 11.16 : 16.75) : (dark ? 7.2 : 8.14);
            double contrast = Contrast(bitmap.GetPixel(2, 20), bitmap.GetPixel(0, 20));
            Assert.True(contrast >= 3 && contrast + 0.005 >= minimum, $"預設動作邊框對比不足：{contrast:F3} < {minimum}");
            Assert.Equal(new Size(240, 90), button.Size);
            string? artifacts = Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_ARTIFACTS");
            if (artifacts != null)
            {
                Directory.CreateDirectory(artifacts);
                bitmap.Save(Path.Combine(artifacts, $"default-context-{strong}.png"));
                File.AppendAllText(Path.Combine(artifacts, "contrast.txt"), $"default strong={strong}, dpi={button.DeviceDpi}: {contrast:F3}:1{Environment.NewLine}");
            }
            lifetime.Cancel();
        });
    }

    /// <summary>
    /// 凝視進度條須依實際底色選用 Green／LimeGreen，保留色覺紋理並達至少 3:1。
    /// </summary>
    [Theory(Skip = "Requires explicit UI test opt-in.",
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Render_Dwell_UsesActualBackgroundWithNonTextContrast(int palette)
    {
        RunSta(() =>
        {
            Color[] backgrounds = [Color.White, Color.Black, Color.FromArgb(220, 220, 220), Color.FromArgb(60, 60, 60)];
            using var form = new Form();
            using var input = new TextBox();
            using var button = new FeedbackButton
            {
                Text = "Button text", Bounds = new Rectangle(10, 50, 240, 90),
                FlatStyle = FlatStyle.Flat, VisualStylesMode = VisualStylesMode.Latest
            };
            using var lifetime = new CancellationTokenSource();
            form.Controls.AddRange([input, button]);
            button.AttachEyeTrackerFeedback("Button", null, null, lifetime.Token);
            form.Show();
            input.Focus();
            button.Hover();
            button.CancelDwellAnimation();
            button.BackColor = SystemInformation.HighContrast ? SystemColors.Highlight : backgrounds[palette];
            button.SetProgress(0.5f);
            using var bitmap = new Bitmap(button.Width, button.Height);
            button.DrawToBitmap(bitmap, button.ClientRectangle);
            Color fill = SystemInformation.HighContrast ? SystemColors.HighlightText :
                palette is 0 or 2 ? Color.Green : Color.LimeGreen;
            Color texture = palette is 0 or 2 ? Color.PaleGreen : Color.DarkGreen;
            int fillPixels = 0;
            int texturePixels = 0;
            for (int y = button.Height - 5; y < button.Height; y++)
            {
                for (int x = 10; x < 110; x++)
                {
                    Color actual = bitmap.GetPixel(x, y);
                    int pixel = actual.ToArgb();
                    if (pixel == fill.ToArgb())
                    {
                        fillPixels++;
                    }
                    if (pixel != fill.ToArgb() && IsTextureBlend(actual, fill, texture))
                    {
                        texturePixels++;
                    }
                }
            }
            string? debugArtifacts = Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_ARTIFACTS");
            if (debugArtifacts != null)
            {
                Directory.CreateDirectory(debugArtifacts);
                bitmap.Save(Path.Combine(debugArtifacts, $"dwell-{palette}.png"));
            }
            Assert.True(fillPixels > 100);
            Assert.True(SystemInformation.HighContrast || texturePixels > 20);
            double contrast = Contrast(fill, bitmap.GetPixel(25, 20));
            double[] targets = [5.14, 9.91, 3.75, 5.21];
            Assert.True(contrast >= 3 && contrast + 0.005 >= (SystemInformation.HighContrast ? 3 : targets[palette]), $"進度條對比不足：{contrast:F3}");
            string? artifacts = Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_ARTIFACTS");
            if (artifacts != null)
            {
                Directory.CreateDirectory(artifacts);
                bitmap.Save(Path.Combine(artifacts, $"dwell-{palette}.png"));
                File.AppendAllText(Path.Combine(artifacts, "contrast.txt"), $"dwell palette={palette}: {contrast:F3}:1{Environment.NewLine}");
            }
            lifetime.Cancel();
        });
    }
    /// <summary>
    /// 確認一般及停用狀態仍由 Latest 繪製可辨識文字，並保留實際像素供人工檢視。
    /// </summary>
    private static void CheckNeutralRendering(Button button, bool isDefault)
    {
        using var bitmap = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(bitmap, button.ClientRectangle);
        Color background = bitmap.GetPixel(25, 20);
        double contrast = 1;
        for (int y = 30; y < 60; y++)
        {
            for (int x = 35; x < 205; x++)
            {
                contrast = Math.Max(contrast, Contrast(background, bitmap.GetPixel(x, y)));
            }
        }

        Assert.True(contrast >= (button.Enabled ? 4.5 : 1.5), $"按鈕文字無法辨識：contrast={contrast}");
        string? artifacts = Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_ARTIFACTS");
        if (artifacts != null)
        {
            Directory.CreateDirectory(artifacts);
            bitmap.Save(Path.Combine(artifacts, $"{isDefault}-enabled-{button.Enabled}.png"));
        }
    }
    /// <summary>
    /// HatchBrush 在 DPI 縮放時會混合紋理與填色，檢查實際像素仍落於指定色對的範圍。
    /// </summary>
    private static bool IsTextureBlend(Color pixel, Color fill, Color texture)
    {
        static bool Between(byte value, byte first, byte second) =>
            value >= Math.Min(first, second) && value <= Math.Max(first, second);
        return Between(pixel.R, fill.R, texture.R) &&
            Between(pixel.G, fill.G, texture.G) && Between(pixel.B, fill.B, texture.B);
    }
    /// <summary>
    /// 取得 sRGB 分量的線性值，供色覺友善的按下亮度差驗證使用。
    /// </summary>
    private static double LinearComponent(byte component)
    {
        double value = component / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
    /// <summary>
    /// 以 WCAG 相對亮度檢查實際文字與背景的對比，不以色彩名稱推測可讀性。
    /// </summary>
    private static double Contrast(Color background, Color foreground)
    {
        static double Linear(byte component)
        {
            double value = component / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) =>
            0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double back = Luminance(background);
        double fore = Luminance(foreground);
        return (Math.Max(back, fore) + 0.05) / (Math.Min(back, fore) + 0.05);
    }
    /// <summary>
    /// 將 WinForms 驗證隔離於 STA，並將背景執行緒的實際失敗傳回測試執行緒。
    /// </summary>
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// 僅在測試控制項本身觸發事件，不向任何其他視窗模擬輸入。
    /// </summary>
    private sealed class FeedbackButton : Button
    {
        public VisualStylesMode ResolvedMode => EffectiveVisualStylesMode;
        public void Hover()
        {
            // 僅驗證繪製：不依賴 Windows 是否允許測試主機搶到前景。
            object table = typeof(ButtonEyeTrackerExtensions).GetField("_buttonStates", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            object?[] args = [this, null];
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            object state = args[1]!;
            state.GetType().GetProperty("IsHovered")!.SetValue(state, true);
            typeof(ButtonEyeTrackerExtensions).GetMethod("StartAnimationFeedback", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [this]);
        }
        public void SetProgress(float progress)
        {
            object table = typeof(ButtonEyeTrackerExtensions).GetField("_buttonStates", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            object?[] args = [this, null];
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            args[1]!.GetType().GetProperty("DwellProgress")!.SetValue(args[1], progress);
        }
        public void RefreshSystemColors() => OnSystemColorsChanged(EventArgs.Empty);
        public void Press() => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        public void ExitHover() => OnMouseLeave(EventArgs.Empty);
    }
}