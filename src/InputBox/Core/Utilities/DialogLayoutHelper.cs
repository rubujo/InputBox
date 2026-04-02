namespace InputBox.Core.Utilities;

/// <summary>
/// 對話框版面配置輔助器：集中處理 DPI 防抖、最小尺寸限制與按鈕量測
/// </summary>
internal static class DialogLayoutHelper
{
    /// <summary>
    /// 預設工作區邊界留白（像素），避免視窗緊貼螢幕邊緣造成的使用不便
    /// </summary>
    private const int DefaultWorkAreaMargin = 40;

    /// <summary>
    /// 依 DPI 快取判斷是否需要重新計算版面
    /// </summary>
    /// <param name="currentDpi">目前 DPI。</param>
    /// <param name="lastAppliedDpi">上次已套用的 DPI 快取。</param>
    /// <param name="forceRecalculate">是否忽略 DPI 防抖並強制重新計算。</param>
    /// <returns>若需要重新計算則回傳 true。</returns>
    public static bool TryBeginDpiLayout
        (float currentDpi,
        ref float lastAppliedDpi,
        bool forceRecalculate = false)
    {
        if (!forceRecalculate &&
            Math.Abs(lastAppliedDpi - currentDpi) < 0.01f)
        {
            return false;
        }

        lastAppliedDpi = currentDpi;

        return true;
    }

    /// <summary>
    /// 估算工作區可容納的最大寬高（保留固定邊界）
    /// </summary>
    /// <param name="workArea">螢幕工作區域。</param>
    /// <param name="margin">保留邊界。</param>
    /// <returns>可容納的最大寬高。</returns>
    public static (int MaxFitWidth, int MaxFitHeight) GetMaxFitSize(
        Rectangle workArea,
        int margin = DefaultWorkAreaMargin)
    {
        return (
            Math.Max(1, workArea.Width - margin),
            Math.Max(1, workArea.Height - margin));
    }

    /// <summary>
    /// 估算視窗非客戶區高度（標題列與邊框）
    /// </summary>
    /// <param name="form">目標視窗。</param>
    /// <returns>非客戶區高度。</returns>
    public static int GetEstimatedNonClientHeight(Form form)
    {
        int nonClientHeight = form.Height - form.ClientSize.Height;

        if (nonClientHeight <= 0)
        {
            nonClientHeight = SystemInformation.CaptionHeight +
                SystemInformation.FrameBorderSize.Height * 2;
        }

        return nonClientHeight;
    }

    /// <summary>
    /// 更新按鈕最小尺寸，避免焦點加粗造成抖動並滿足可點擊面積下限
    /// </summary>
    /// <param name="button">目標按鈕。</param>
    /// <param name="measuringFont">用於量測的字型。</param>
    /// <param name="scale">目前 DPI 縮放比例。</param>
    /// <param name="logicalMinWidth">邏輯最小寬度。</param>
    /// <param name="logicalMinHeight">邏輯最小高度。</param>
    /// <param name="horizontalPadding">文字外額外水平留白。</param>
    /// <param name="verticalPadding">文字外額外垂直留白。</param>
    public static void UpdateButtonMinimumSize(
        Button button,
        Font measuringFont,
        float scale,
        int logicalMinWidth,
        int logicalMinHeight,
        int horizontalPadding,
        int verticalPadding)
    {
        if (button.IsDisposed)
        {
            return;
        }

        button.MinimumSize = Size.Empty;

        Size measuredTextSize = TextRenderer.MeasureText(button.Text, measuringFont);

        int minWidth = Math.Max((int)(logicalMinWidth * scale), measuredTextSize.Width + (int)(horizontalPadding * scale)),
            minHeight = Math.Max((int)(logicalMinHeight * scale), measuredTextSize.Height + (int)(verticalPadding * scale));

        button.MinimumSize = new Size(minWidth, minHeight);
    }

    /// <summary>
    /// 依最小值與工作區上限同步修正視窗尺寸
    /// </summary>
    /// <param name="form">目標視窗。</param>
    /// <param name="minWidth">最小寬度。</param>
    /// <param name="minHeight">最小高度。</param>
    /// <param name="maxFitWidth">最大可容納寬度。</param>
    /// <param name="maxFitHeight">最大可容納高度。</param>
    /// <param name="onClamped">尺寸被修正後的回呼。</param>
    public static void ClampFormSize(
        Form form,
        int minWidth,
        int minHeight,
        int maxFitWidth,
        int maxFitHeight,
        Action? onClamped = null)
    {
        form.MinimumSize = new Size(minWidth, minHeight);

        if (form.Width >= minWidth &&
            form.Height >= minHeight &&
            form.Width <= maxFitWidth &&
            form.Height <= maxFitHeight)
        {
            return;
        }

        int finalMaxWidth = Math.Max(minWidth, maxFitWidth),
            finalMaxHeight = Math.Max(minHeight, maxFitHeight);

        form.Size = new Size(
            Math.Clamp(form.Width, minWidth, finalMaxWidth),
            Math.Clamp(form.Height, minHeight, finalMaxHeight));

        onClamped?.Invoke();
    }
}