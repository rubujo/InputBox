using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Services;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Xunit;

namespace InputBox.Tests;

[Collection(UiSmokeTestRequirements.CollectionName)]
[Trait("Category", "UI")]
public sealed class NumericInputLayoutTests
{
    static NumericInputLayoutTests()
    {
        Application.SetHighDpiMode(Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_TEST_96DPI") == "1" ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetColorMode(Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_TEST_DARK") == "1" ? SystemColorMode.Dark : SystemColorMode.Classic);
        Application.SetDefaultVisualStylesMode(VisualStylesMode.Latest);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(35)]
    [InlineData(48)]
    public void LatestNumericEditor_LargeFont_FitsCompleteLine(float size)
    {
        // 正式數值對話框由 MainForm 開啟，共用相同的已初始化字型快取。
        using MainForm owner = new();
        using NumericInputDialog dialog = new("number", 100, 100, 2, 1, -10000, 10000);
        NumericUpDown number = (NumericUpDown)typeof(NumericInputDialog)
            .GetField("_nud", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        _ = number.Handle;
        Control editor = Assert.Single(number.Controls.Cast<Control>(), control => control is TextBox);
        Assert.True(editor.ClientSize.Height >= MeasureNumericGlyphHeight(number.Font, number.DeviceDpi),
            $"Initial editor {editor.ClientSize.Height}px, font {number.Font.Height}px, control {number.Height}px, preferred {number.PreferredHeight}px at {number.DeviceDpi} DPI.");
        using Font font = new(number.Font.FontFamily, size, FontStyle.Bold);
        number.Font = font;
        number.PerformLayout();

        Assert.True(editor.ClientSize.Height >= MeasureNumericGlyphHeight(font, number.DeviceDpi),
            $"Editor {editor.ClientSize.Height}px must fit font {font.Height}px at {number.DeviceDpi} DPI.");
        Assert.True(editor.ClientSize.Width >= TextRenderer.MeasureText(number.Minimum.ToString("F2"), font).Width);
        Assert.True(editor.ClientSize.Width >= TextRenderer.MeasureText(number.Maximum.ToString("F2"), font).Width);
        Size stableSize = number.Size;
        number.Value = number.Minimum;
        Assert.Equal(stableSize, number.Size);
        number.Value = number.Maximum;
        Assert.Equal(stableSize, number.Size);
        number.Value = 100;
        Assert.Equal(100, number.Value);
    }

    [Fact(Skip = "Requires explicit UI test opt-in.", SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    public void Reactivation_AfterInactiveThemeReset_RestoresInputFocusColors()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using MainForm owner = new();
                using NumericInputDialog dialog = new("number", 100, 100, 0, 1, 1, 10000);
                using Form other = new() { Location = new Point(850, 550), StartPosition = FormStartPosition.Manual };
                NumericUpDown number = (NumericUpDown)typeof(NumericInputDialog)
                    .GetField("_nud", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                dialog.Shown += async (_, _) =>
                {
                    try
                    {
                        _ = await WindowFocusService.RestoreWindowAsync(dialog.Handle, TestContext.Current.CancellationToken);
                        number.Focus();
                        await Task.Delay(80);
                        Color background = number.BackColor;
                        Color foreground = number.ForeColor;
                        Size size = number.Size;
                        other.Show();
                        _ = await WindowFocusService.RestoreWindowAsync(other.Handle, TestContext.Current.CancellationToken);
                        Assert.False(dialog.ContainsFocus);
                        number.ResetThemeRecursive();
                        Assert.NotEqual(background, number.BackColor);

                        _ = await WindowFocusService.RestoreWindowAsync(dialog.Handle, TestContext.Current.CancellationToken);
                        await Task.Delay(80);
                        Assert.True(number.ContainsFocus);
                        Assert.Equal(background, number.BackColor);
                        Assert.Equal(foreground, number.ForeColor);
                        Assert.Equal(size, number.Size);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { other.Close(); dialog.Close(); }
                };
                Application.Run(dialog);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private static int MeasureNumericGlyphHeight(Font font, int dpi)
    {
        // 原生 Classic 的行距可能含編輯區外的留白；驗證完整數字字形而非空白行距。
        using Bitmap bitmap = new(1000, font.Height * 3);
        bitmap.SetResolution(dpi, dpi);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        TextRenderer.DrawText(graphics, "-0123456789.,", font, new Point(0, font.Height), Color.White,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        int top = bitmap.Height;
        int bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != Color.Black.ToArgb())
                {
                    top = Math.Min(top, y);
                    bottom = y;
                    break;
                }
            }
        }
        Assert.True(bottom >= top, "Numeric glyph reference must contain visible pixels.");
        return bottom - top + 1;
    }
}
