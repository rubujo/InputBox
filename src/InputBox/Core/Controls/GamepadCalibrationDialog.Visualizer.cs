using InputBox.Core.Configuration;
using InputBox.Core.Input;
using InputBox.Resources;
using System.Drawing.Drawing2D;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 顯示遊戲控制器校準狀態的視覺化診斷對話框（校正視覺化分部）。
/// <para>本分部檔案包含搖桿校正繪製面板、狀態文字格式化與搖桿軌跡繪製等成員。</para>
/// </summary>
internal sealed partial class GamepadCalibrationDialog
{
    /// <summary>
    /// 使用雙緩衝避免繪圖閃爍。
    /// </summary>
    private sealed class BufferedPanel : Panel
    {
        /// <summary>
        /// 初始化 BufferedPanel，啟用雙緩衝與縮放重繪。
        /// </summary>
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            TabStop = false;
        }
    }

    /// <summary>
    /// 依目前快照更新狀態標籤文字，並同步重設按鈕的啟用狀態。
    /// </summary>
    private void UpdateStatusText()
    {
        if (_lblStatus == null)
        {
            return;
        }

        _lblStatus.Text = FormatStatusText(_snapshot);

        _btnReset?.Enabled = _snapshot.IsConnected;
    }

    /// <summary>
    /// 依連線狀態與裝置名稱產生無障礙廣播訊息字串。
    /// </summary>
    /// <param name="isConnected">控制器是否已連線。</param>
    /// <param name="deviceName">裝置名稱；可為 null。</param>
    /// <returns>格式化後的連線狀態廣播訊息。</returns>
    internal static string FormatConnectionAnnouncement(bool isConnected, string? deviceName)
    {
        string template = isConnected ?
            Strings.A11y_Gamepad_Connected :
            Strings.A11y_Gamepad_Disconnected;

        string message = string.Format(template, deviceName?.Trim() ?? string.Empty);

        while (message.Contains("  ", StringComparison.Ordinal))
        {
            message = message.Replace("  ", " ", StringComparison.Ordinal);
        }

        return message.Replace(" .", ".", StringComparison.Ordinal).Trim();
    }

    /// <summary>
    /// 依校準快照產生狀態文字標籤內容。
    /// </summary>
    /// <param name="snapshot">目前的校準狀態快照。</param>
    /// <returns>格式化後的狀態文字；控制器未連線時回傳中斷連線提示訊息。</returns>
    internal static string FormatStatusText(GamepadCalibrationSnapshot snapshot)
    {
        if (!snapshot.IsConnected)
        {
            return Strings.Dialog_GamepadCalibrationVisualizer_StatusDisconnected;
        }

        return string.Format(
            Strings.Dialog_GamepadCalibrationVisualizer_StatusConnected,
            FormatAxis(snapshot.RawLeftX),
            FormatAxis(snapshot.RawLeftY),
            FormatAxis(snapshot.CorrectedLeftX),
            FormatAxis(snapshot.CorrectedLeftY),
            FormatAxis(snapshot.RawRightX),
            FormatAxis(snapshot.RawRightY),
            FormatAxis(snapshot.CorrectedRightX),
            FormatAxis(snapshot.CorrectedRightY),
            snapshot.ThumbDeadzoneEnter,
            snapshot.ThumbDeadzoneExit);
    }

    /// <summary>
    /// 將正規化軸值格式化為帶符號的兩位小數字串。
    /// </summary>
    /// <param name="value">正規化軸值（-1.0 ~ 1.0）。</param>
    /// <returns>格式化後的字串，例如 "+0.75" 或 "-0.12"。</returns>
    private static string FormatAxis(float value)
    {
        return value.ToString("+0.00;-0.00;0.00");
    }

    /// <summary>
    /// 繪製校準視覺化畫布，包含雙搖桿軌跡圖與死區圓圈。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">包含繪圖 Graphics 的事件引數。</param>
    private void HandleSurfacePaint(object? sender, PaintEventArgs e)
    {
        if (_surface == null)
        {
            return;
        }

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SystemColors.Window);

        Rectangle clientRect = _surface.ClientRectangle;

        if (clientRect.Width <= 20 || clientRect.Height <= 20)
        {
            return;
        }

        float s = DeviceDpi / AppSettings.BaseDpi;
        int margin = (int)(12 * s);
        RectangleF contentBounds = new(
            clientRect.Left + margin,
            clientRect.Top + margin,
            clientRect.Width - (margin * 2),
            clientRect.Height - (margin * 2));

        Color axisColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.DimGray;
        Color deadzoneColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(72, 120, 120, 120);
        Color rawColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(90, 90, 90);
        Color correctedColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.DodgerBlue;

        using Pen outerPen = new(axisColor, 2f * s);
        using Pen crossPen = new(axisColor, 1.5f * s) { DashStyle = DashStyle.Dash };
        using Pen deadzonePen = new(deadzoneColor, 2.5f * s);
        using Pen deadzoneExitPen = new(deadzoneColor, 2f * s) { DashStyle = DashStyle.Dash };
        using Pen rawPen = new(rawColor, 2.5f * s);
        using Pen correctedOutlinePen = new(axisColor, 2f * s);
        using SolidBrush deadzoneFillBrush = new(Color.FromArgb(SystemInformation.HighContrast ? 60 : 48, deadzoneColor));
        using SolidBrush rawBrush = new(Color.FromArgb(SystemInformation.HighContrast ? 100 : 64, rawColor));
        using SolidBrush correctedBrush = new(correctedColor);
        using SolidBrush centerBrush = new(axisColor);

        if (!_snapshot.IsConnected)
        {
            TextRenderer.DrawText(
                graphics,
                Strings.Dialog_GamepadCalibrationVisualizer_StatusDisconnected,
                _a11yFont ?? Font,
                Rectangle.Round(contentBounds),
                axisColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

            return;
        }

        float labelHeight = 54f * s;
        float plotGap = 16f * s;
        float plotVerticalPadding = 6f * s;
        float plotWidth = (contentBounds.Width - plotGap) / 2f;
        float plotSize = Math.Min(plotWidth, contentBounds.Height - labelHeight - plotVerticalPadding);
        float verticalOffset = (contentBounds.Height - labelHeight - plotSize) / 2f;

        RectangleF leftPlot = new(
            contentBounds.Left + ((plotWidth - plotSize) / 2f),
            contentBounds.Top + labelHeight + verticalOffset,
            plotSize,
            plotSize);
        RectangleF rightPlot = new(
            contentBounds.Left + plotWidth + plotGap + ((plotWidth - plotSize) / 2f),
            contentBounds.Top + labelHeight + verticalOffset,
            plotSize,
            plotSize);

        DrawStickPlot(graphics, leftPlot, "LS", _snapshot.RawLeftX, _snapshot.RawLeftY, _snapshot.CorrectedLeftX, _snapshot.CorrectedLeftY, axisColor, outerPen, crossPen, deadzonePen, deadzoneExitPen, rawPen, correctedOutlinePen, deadzoneFillBrush, rawBrush, correctedBrush, centerBrush);
        DrawStickPlot(graphics, rightPlot, "RS", _snapshot.RawRightX, _snapshot.RawRightY, _snapshot.CorrectedRightX, _snapshot.CorrectedRightY, axisColor, outerPen, crossPen, deadzonePen, deadzoneExitPen, rawPen, correctedOutlinePen, deadzoneFillBrush, rawBrush, correctedBrush, centerBrush);
    }

    /// <summary>
    /// 在指定範圍內繪製單一搖桿的校準圖，包含死區、原始與修正後軌跡。
    /// </summary>
    /// <param name="graphics">目標 GDI+ 繪圖物件。</param>
    /// <param name="plotBounds">搖桿圖的像素邊界矩形。</param>
    /// <param name="label">搖桿標籤（如 "LS" 或 "RS"）。</param>
    /// <param name="rawX">原始 X 軸正規化值（-1.0 ~ 1.0）。</param>
    /// <param name="rawY">原始 Y 軸正規化值（-1.0 ~ 1.0）。</param>
    /// <param name="correctedX">死區修正後 X 軸正規化值。</param>
    /// <param name="correctedY">死區修正後 Y 軸正規化值。</param>
    /// <param name="axisColor">座標軸與外框顏色。</param>
    /// <param name="outerPen">外框圓圈畫筆。</param>
    /// <param name="crossPen">十字準線畫筆。</param>
    /// <param name="deadzonePen">進入死區圓圈畫筆。</param>
    /// <param name="deadzoneExitPen">退出死區虛線圓圈畫筆。</param>
    /// <param name="rawPen">原始軌跡線畫筆。</param>
    /// <param name="correctedOutlinePen">修正點外框畫筆。</param>
    /// <param name="deadzoneFillBrush">死區填滿筆刷。</param>
    /// <param name="rawBrush">原始位置填滿筆刷。</param>
    /// <param name="correctedBrush">修正後位置填滿筆刷。</param>
    /// <param name="centerBrush">中心點填滿筆刷。</param>
    private void DrawStickPlot(Graphics graphics, RectangleF plotBounds, string label, float rawX, float rawY, float correctedX, float correctedY, Color axisColor, Pen outerPen, Pen crossPen, Pen deadzonePen, Pen deadzoneExitPen, Pen rawPen, Pen correctedOutlinePen, Brush deadzoneFillBrush, Brush rawBrush, Brush correctedBrush, Brush centerBrush)
    {
        graphics.DrawEllipse(outerPen, plotBounds);

        float centerX = plotBounds.Left + (plotBounds.Width / 2f),
            centerY = plotBounds.Top + (plotBounds.Height / 2f);

        graphics.DrawLine(crossPen, plotBounds.Left, centerY, plotBounds.Right, centerY);
        graphics.DrawLine(crossPen, centerX, plotBounds.Top, centerX, plotBounds.Bottom);

        float deadzoneRadius = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(_snapshot.ThumbDeadzoneEnter) * (plotBounds.Width / 2f);
        graphics.FillEllipse(deadzoneFillBrush, centerX - deadzoneRadius, centerY - deadzoneRadius, deadzoneRadius * 2f, deadzoneRadius * 2f);
        graphics.DrawEllipse(deadzonePen, centerX - deadzoneRadius, centerY - deadzoneRadius, deadzoneRadius * 2f, deadzoneRadius * 2f);

        float exitDeadzoneRadius = GamepadCalibrationVisualizerMapper.CalculateDeadzoneRadius(_snapshot.ThumbDeadzoneExit) * (plotBounds.Width / 2f);
        graphics.DrawEllipse(deadzoneExitPen, centerX - exitDeadzoneRadius, centerY - exitDeadzoneRadius, exitDeadzoneRadius * 2f, exitDeadzoneRadius * 2f);

        float dpiScale = DeviceDpi / AppSettings.BaseDpi;
        graphics.FillEllipse(centerBrush, centerX - 3f * dpiScale, centerY - 3f * dpiScale, 6f * dpiScale, 6f * dpiScale);

        PointF rawPoint = GamepadCalibrationVisualizerMapper.MapToCanvas(plotBounds, rawX, rawY),
            correctedPoint = GamepadCalibrationVisualizerMapper.MapToCanvas(plotBounds, correctedX, correctedY);

        graphics.DrawLine(rawPen, centerX, centerY, rawPoint.X, rawPoint.Y);

        float dm = 8f * dpiScale;
        PointF[] diamond =
        [
            new PointF(rawPoint.X, rawPoint.Y - dm),
            new PointF(rawPoint.X + dm, rawPoint.Y),
            new PointF(rawPoint.X, rawPoint.Y + dm),
            new PointF(rawPoint.X - dm, rawPoint.Y)
        ];
        graphics.FillPolygon(rawBrush, diamond);
        graphics.DrawPolygon(rawPen, diamond);
        float cr = 6f * dpiScale;
        graphics.FillEllipse(correctedBrush, correctedPoint.X - cr, correctedPoint.Y - cr, cr * 2f, cr * 2f);
        graphics.DrawEllipse(correctedOutlinePen, correctedPoint.X - cr, correctedPoint.Y - cr, cr * 2f, cr * 2f);

        Rectangle labelBounds = Rectangle.Round(new RectangleF(plotBounds.Left + 12f * dpiScale, plotBounds.Top - 44f * dpiScale, plotBounds.Width - 24f * dpiScale, 30f * dpiScale));
        graphics.FillRectangle(SystemBrushes.Window, labelBounds);
        TextRenderer.DrawText(
            graphics,
            label,
            _a11yFont ?? Font,
            labelBounds,
            axisColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
