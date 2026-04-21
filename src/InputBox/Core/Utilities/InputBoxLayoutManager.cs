using InputBox.Core.Configuration;

namespace InputBox.Core.Utilities;

/// <summary>
/// 提供 InputBox 主視窗的佈局約束、最小尺寸與智慧定位計算
/// </summary>
public static class InputBoxLayoutManager
{
    /// <summary>
    /// 計算視窗在目前螢幕工作區內的合法位置
    /// </summary>
    /// <param name="form">目標視窗實例。</param>
    /// <param name="clampedLocation">輸出的合法位置。</param>
    /// <returns>若需修正位置則為 true，否則為 false。</returns>
    public static bool TryGetClampedLocation(Form form, out Point clampedLocation)
    {
        clampedLocation = form.Location;

        if (!form.IsHandleCreated ||
            form.IsDisposed)
        {
            return false;
        }

        Rectangle workArea = Screen.FromControl(form).WorkingArea;

        int x = Math.Max(workArea.Left, Math.Min(form.Left, workArea.Right - form.Width)),
            y = Math.Max(workArea.Top, Math.Min(form.Top, workArea.Bottom - form.Height));

        clampedLocation = new Point(x, y);

        return x != form.Left || y != form.Top;
    }

    /// <summary>
    /// 執行智慧定位修正，確保視窗不會跑出螢幕邊界
    /// </summary>
    /// <param name="form">主視窗實例</param>
    /// <param name="announceSnapBack">位置修正後的通知回呼</param>
    public static void ApplySmartPosition(Form form, Action announceSnapBack)
    {
        if (!form.IsHandleCreated ||
            form.IsDisposed)
        {
            return;
        }

        // 強制同步 Win32 座標狀態。
        form.Update();

        if (TryGetClampedLocation(form, out Point clampedLocation))
        {
            form.Location = clampedLocation;

            announceSnapBack();
        }
    }

    /// <summary>
    /// 更新最小尺寸流程的防抖控制，僅在 DPI 變化時重新計算
    /// </summary>
    /// <param name="currentDpi">目前 DPI</param>
    /// <param name="lastAppliedDpi">前次 DPI 快取</param>
    /// <param name="updateLayoutConstraints">更新佈局約束回呼</param>
    /// <param name="updateOpacityWhenHighContrast">高對比模式下的不透明度同步回呼</param>
    /// <returns>新的 DPI 快取值</returns>
    public static float UpdateMinimumSize(
        float currentDpi,
        float lastAppliedDpi,
        Action updateLayoutConstraints,
        Action updateOpacityWhenHighContrast)
    {
        if (Math.Abs(lastAppliedDpi - currentDpi) < 0.01f)
        {
            return lastAppliedDpi;
        }

        float nextAppliedDpi = currentDpi;

        // 此處不執行昂貴的 ApplyLocalization，而是僅更新佈局約束。
        updateLayoutConstraints();

        if (SystemInformation.HighContrast)
        {
            updateOpacityWhenHighContrast();
        }

        return nextAppliedDpi;
    }

    /// <summary>
    /// 更新視窗佈局約束，包含 MinimumSize 與 ClientSize 的精確測量
    /// </summary>
    /// <param name="form">主視窗實例</param>
    /// <param name="layoutHost">佈局容器</param>
    /// <param name="copyButton">複製按鈕</param>
    /// <param name="inputBox">輸入框</param>
    /// <param name="boldButtonFont">按鈕加粗字型</param>
    /// <param name="sizeFromClientSize">將 ClientSize 轉換為視窗實際大小的回呼</param>
    /// <param name="applySmartPosition">佈局擴張後的智慧定位回呼</param>
    public static void UpdateLayoutConstraints(
        Form form,
        TableLayoutPanel layoutHost,
        Button copyButton,
        TextBox inputBox,
        Font boldButtonFont,
        Func<Size, Size> sizeFromClientSize,
        Action applySmartPosition)
    {
        if (form.IsDisposed ||
            !form.IsHandleCreated)
        {
            return;
        }

        bool isRunningOnGamescope = SystemHelper.IsRunningOnGamescope();
        float currentScale = form.DeviceDpi / AppSettings.BaseDpi;

        copyButton.MinimumSize = Size.Empty;

        Size boldSize = TextRenderer.MeasureText(copyButton.Text, boldButtonFont);

        int requiredBtnWidth = boldSize.Width + copyButton.Padding.Horizontal + (int)(10 * currentScale);

        if (copyButton.MinimumSize.Width < requiredBtnWidth)
        {
            copyButton.MinimumSize = new Size(requiredBtnWidth, copyButton.MinimumSize.Height);
        }

        Size phtSize = TextRenderer.MeasureText(inputBox.PlaceholderText, inputBox.Font);

        int measuredLogicWidth = (int)(phtSize.Width / currentScale),
            finalInputLogicWidth = Math.Clamp(measuredLogicWidth + 20, 180, 280),
            minInputAreaWidth = (int)(finalInputLogicWidth * currentScale),
            totalMinWidth = requiredBtnWidth +
                minInputAreaWidth +
                layoutHost.Padding.Horizontal +
                (int)(20 * currentScale),
            clientFloorHeight = (int)(60 * currentScale);

        Size textSize = TextRenderer.MeasureText("Ag", inputBox.Font);

        int measuredTextHeight = textSize.Height + (int)(12 * currentScale),
            finalClientHeight = Math.Max(clientFloorHeight, measuredTextHeight);

        Size minWindowSize = sizeFromClientSize(new Size(totalMinWidth, finalClientHeight));

        int finalWindowHeight = Math.Max(minWindowSize.Height, (int)(80 * currentScale));

        if (isRunningOnGamescope)
        {
            return;
        }

        Rectangle workArea = Screen.FromControl(form).WorkingArea;

        int maxFitWidth = Math.Max(1, workArea.Width - 40),
            maxFitHeight = Math.Max(1, workArea.Height - 40),
            clampedMinWidth = Math.Min(minWindowSize.Width, maxFitWidth),
            clampedMinHeight = Math.Min(finalWindowHeight, maxFitHeight);

        form.MinimumSize = new Size(clampedMinWidth, clampedMinHeight);

        // 僅在 Normal 狀態下強制修正尺寸。
        // 若視窗已最大化（如 ROG Ally X 平板模式在 Handle 建立前即最大化），
        // 跳過 Size 設定，避免視窗從 Maximized 被強制固定至接近全螢幕的 Normal 大小，
        // 導致後續 ShowWindow(Restore) 成為 no-op 而無法縮回合理尺寸。
        if (form.WindowState == FormWindowState.Normal &&
            (form.Width < clampedMinWidth ||
             form.Height < clampedMinHeight ||
             form.Width > maxFitWidth ||
             form.Height > maxFitHeight))
        {
            int finalMaxW = Math.Max(clampedMinWidth, maxFitWidth),
                finalMaxH = Math.Max(clampedMinHeight, maxFitHeight);

            form.Size = new Size(
                Math.Clamp(form.Width, clampedMinWidth, finalMaxW),
                Math.Clamp(form.Height, clampedMinHeight, finalMaxH));

            applySmartPosition();
        }
    }
}
