using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using System.Globalization;
using System.Runtime.InteropServices;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 顯式啟用時記錄實際 Win32 DPI、視窗尺寸與畫面，供主題及原生縮放驗證。
/// </summary>
internal static partial class UiVisualSnapshot
{
    /// <summary>
    /// 擷取主視窗或對話框，並檢查可見按鈕是否位於視窗內；不以縮放圖片模擬 DPI。
    /// </summary>
    /// <param name="window">已顯示的受測視窗。</param>
    /// <param name="label">產物標籤。</param>
    public static void Capture(Window? window, string label)
    {
        if (Environment.GetEnvironmentVariable("INPUTBOX_CAPTURE_UI") != "1")
        {
            return;
        }

        Assert.NotNull(window);
        uint dpi = GetDpiForWindow(window.Properties.NativeWindowHandle.Value);
        Assert.True(dpi > 0);
        string? expectedDpi = Environment.GetEnvironmentVariable("INPUTBOX_EXPECTED_DPI");
        if (!string.IsNullOrEmpty(expectedDpi))
        {
            Assert.Equal(uint.Parse(expectedDpi, CultureInfo.InvariantCulture), dpi);
        }

        Rectangle bounds = window.BoundingRectangle;
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        foreach (AutomationElement button in window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)))
        {
            if (!button.IsOffscreen)
            {
                Assert.True(bounds.Contains(button.BoundingRectangle),
                    $"按鈕超出視窗：{button.Name}, button={button.BoundingRectangle}, window={bounds}");
            }
        }

        string directory = Environment.GetEnvironmentVariable("INPUTBOX_UI_ARTIFACT_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "UiArtifacts");
        Directory.CreateDirectory(directory);
        string safeLabel = string.Concat(label.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string prefix = Path.Combine(directory, $"{safeLabel}-{dpi}dpi");
        using CaptureImage capture = FlaUI.Core.Capturing.Capture.Element(window);
        capture.ToFile(prefix + ".png");
        File.WriteAllLines(prefix + ".txt",
        [
            $"label={label}",
            $"dpi={dpi}",
            $"bounds={bounds}",
            $"highContrast={SystemInformation.HighContrast}",
            $"utc={DateTime.UtcNow:O}"
        ]);
        Console.WriteLine($"[UI Visual] {label}: dpi={dpi}, bounds={bounds}, highContrast={SystemInformation.HighContrast}");
    }

    /// <summary>
    /// 取得目標 HWND 的實際 DPI。
    /// </summary>
    /// <param name="window">受測視窗控制代碼。</param>
    /// <returns>視窗目前使用的 DPI。</returns>
    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
}