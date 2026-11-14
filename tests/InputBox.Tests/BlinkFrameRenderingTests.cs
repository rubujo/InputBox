using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Interop;
using System.Runtime.ExceptionServices;
using Xunit;

namespace InputBox.Tests;

/// <summary>以實際畫面驗證 Blink 完成／取消後的非客戶區框架，不以重新繪圖的截圖 API 掩蓋殘影。</summary>
[Collection(UiSmokeTestRequirements.CollectionName)]
[Trait("Category", "UI")]
public sealed class BlinkFrameRenderingTests
{
    /// <summary>以明確色彩模式建立獨立驗證程序，保留實際 Windows 高對比判斷。</summary>
    static BlinkFrameRenderingTests()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetColorMode(Environment.GetEnvironmentVariable("INPUTBOX_BUTTON_TEST_DARK") == "1" ?
            SystemColorMode.Dark : SystemColorMode.Classic);
    }

    /// <summary>焦點與非焦點的 Blink 完成及取消都應還原框架像素，並保留 HWND、選取及版面。</summary>
    [Theory(Skip = "Requires explicit UI test opt-in.",
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled), SkipType = typeof(UiSmokeTestRequirements))]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Blink_Finally_RestoresFrameWithoutChangingInputState(bool focused, bool cancel)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            using var form = new Form { ClientSize = new Size(420, 180), TopMost = true, StartPosition = FormStartPosition.Manual, Location = new Point(10, 10) };
            Rectangle screen = Screen.PrimaryScreen!.WorkingArea;
            if (form.Bounds.Contains(Cursor.Position))
            {
                form.Location = new Point(screen.Right - form.Width - 20, screen.Bottom - form.Height - 20);
            }
            using var host = new Panel { Bounds = new Rectangle(10, 10, 350, 90), Padding = new Padding(4) };
            using var input = new TextBox
            {
                Multiline = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
                VisualStylesMode = VisualStylesMode.Latest, Text = "Blink check"
            };
            using var other = new Button { Top = 120, Text = "Other" };
            host.Controls.Add(input);
            form.Controls.AddRange([host, other]);
            form.Shown += async (_, _) =>
            {
                try
                {
                    if (focused)
                    {
                        Assert.True(input.Focus());
                    }
                    else
                    {
                        Assert.True(other.Focus());
                    }
                    Restore(host, input);
                    host.RedrawVisualFrameRecursive();
                    await Task.Delay(500, TestContext.Current.CancellationToken);
                    input.Select(3, 2);
                    using Bitmap before = Capture(input);
                    Color expectedBack = input.BackColor;
                    Color expectedFore = input.ForeColor;
                    Color expectedBorder = host.BackColor;
                    nint handle = input.Handle;
                    Rectangle bounds = input.Bounds;
                    Padding padding = host.Padding;
                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                    bool framePaintedDuringAlert = false;
                    try
                    {
                        await FlashAlertAnimator.RunAsync(form, intensity =>
                        {
                            (Color back, Color fore) = FlashAlertAnimator.ComputeFrameColors(intensity,
                                input.IsDarkModeActive(), FlashAlertAnimator.GetAlertColor(input.IsDarkModeActive(), SystemInformation.HighContrast),
                                SystemInformation.HighContrast);
                            host.UpdateRecursive(back, fore);
                            if (!framePaintedDuringAlert && intensity > 0.8f)
                            {
                                framePaintedDuringAlert = true;
                                _ = User32.RedrawWindow(input.Handle, 0, 0,
                                    User32.RedrawFlags.Invalidate | User32.RedrawFlags.Frame | User32.RedrawFlags.UpdateNow);
                                if (cancel)
                                {
                                    stop.Cancel();
                                }
                            }
                        }, stop.Token);
                    }
                    catch (OperationCanceledException) when (cancel && stop.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        Restore(host, input);
                        host.RedrawVisualFrameRecursive();
                    }
                    await Task.Delay(500, TestContext.Current.CancellationToken);
                    using Bitmap after = Capture(input);
                    Assert.True(framePaintedDuringAlert);
                    Assert.Equal(expectedBack, input.BackColor);
                    Assert.Equal(expectedFore, input.ForeColor);
                    Assert.Equal(expectedBorder, host.BackColor);
                    Assert.Equal(handle, input.Handle);
                    Assert.Equal(bounds, input.Bounds);
                    Assert.Equal(padding, host.Padding);
                    Assert.Equal(focused, input.Focused);
                    Assert.Equal(3, input.SelectionStart);
                    Assert.Equal(2, input.SelectionLength);
                    for (int x = 8; x < input.Width - 8; x++)
                    {
                        AssertFramePixel(before.GetPixel(x, 0), after.GetPixel(x, 0));
                        AssertFramePixel(before.GetPixel(x, input.Height - 1), after.GetPixel(x, input.Height - 1));
                    }
                    for (int y = input.Height / 2; y < input.Height - 8; y++)
                    {
                        AssertFramePixel(before.GetPixel(0, y), after.GetPixel(0, y));
                        AssertFramePixel(before.GetPixel(input.Width - 1, y), after.GetPixel(input.Width - 1, y));
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    form.Close();
                }
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>容許螢幕合成的兩階 RGB 量化差；警示殘色仍會產生遠大於此值的差異。</summary>
    private static void AssertFramePixel(Color expected, Color actual)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, 2);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, 2);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, 2);
    }
    /// <summary>依與主視窗相同的還原順序先重設主題，再套用焦點背景與外框。</summary>
    private static void Restore(Panel host, TextBox input)
    {
        host.ResetThemeRecursive();
        if (input.Focused)
        {
            input.BackColor = SystemInformation.HighContrast ? SystemColors.Highlight :
                input.IsDarkModeActive() ? Color.White : Color.Black;
            input.ForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText :
                input.IsDarkModeActive() ? Color.Black : Color.White;
            host.BackColor = ControlExtensions.GetContextualFocusColors(input.BackColor, input.IsDarkModeActive()).Border;
        }
    }

    /// <summary>只讀取自家控制項所在的螢幕像素，不透過 DrawToBitmap 觸發額外框架重繪。</summary>
    private static Bitmap Capture(Control control)
    {
        Point origin = control.Parent!.PointToScreen(control.Location);
        var bitmap = new Bitmap(control.Width, control.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(origin, Point.Empty, control.Size);
        return bitmap;
    }
}