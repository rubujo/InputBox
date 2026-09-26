using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Utilities;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 顯示遊戲控制器校準狀態的視覺化診斷對話框（版面配置分部）。
/// <para>本分部檔案包含 DPI 變更處理、最小尺寸與智慧定位等成員。</para>
/// </summary>
internal sealed partial class GamepadCalibrationDialog
{
    /// <summary>
    /// 視窗 Handle 建立後，更新最小尺寸並定位至初始位置。
    /// </summary>
    /// <param name="e">事件引數。</param>
    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);
            UpdateMinimumSize(forceRecalculate: true);
            this.SafeBeginInvoke(ApplySmartPosition);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] OnHandleCreated 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 使用者完成調整視窗大小後，重新套用智慧定位。
    /// </summary>
    /// <param name="e">事件引數。</param>
    protected override void OnResizeEnd(EventArgs e)
    {
        try
        {
            base.OnResizeEnd(e);
            ApplySmartPosition();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] OnResizeEnd 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// DPI 變更時更新字型、最小尺寸，並重新套用智慧定位。
    /// </summary>
    /// <param name="e">包含新舊 DPI 值的事件引數。</param>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi);

                    if (_a11yFont != null)
                    {
                        _lblIntro?.Font = _a11yFont;

                        _lblStatus?.Font = _a11yFont;

                        _btnReset?.Font = _a11yFont;

                        _btnClose?.Font = _a11yFont;
                    }

                    UpdateMinimumSize(forceRecalculate: true);
                    ApplySmartPosition();
                    _surface?.Invalidate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GamepadCalibrationDialog] OnDpiChanged 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] OnDpiChanged 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 依目前 DPI 與可用工作區重新計算並套用對話框的最小尺寸。
    /// </summary>
    /// <param name="forceRecalculate">是否強制重新計算，忽略 DPI 未變更的快取防呆。</param>
    private void UpdateMinimumSize(bool forceRecalculate = false)
    {
        try
        {
            if (_layoutHost == null ||
                _surface == null ||
                _lblIntro == null ||
                _lblStatus == null ||
                _btnReset == null ||
                _btnClose == null ||
                _buttonRow == null)
            {
                return;
            }

            float currentDpi = DeviceDpi;

            if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi, forceRecalculate))
            {
                return;
            }

            float scale = currentDpi / AppSettings.BaseDpi;
            Rectangle workArea = Screen.GetWorkingArea(this);
            (int maxFitWidth, int maxFitHeight) = DialogLayoutHelper.GetMaxFitSize(workArea);

            int targetWindowWidth = Math.Clamp((int)(600 * scale), Math.Min(maxFitWidth, (int)(420 * scale)), maxFitWidth);
            int contentWidth = Math.Max(220, targetWindowWidth - Padding.Horizontal);

            _lblIntro.MaximumSize = new Size(contentWidth, 0);
            _lblIntro.Margin = new Padding(0, 0, 0, (int)(6 * scale));

            Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, (_a11yFont ?? Font).FontFamily);
            DialogLayoutHelper.UpdateButtonMinimumSize(_btnReset, boldFont, scale, 120, 56, 32, 20);
            DialogLayoutHelper.UpdateButtonMinimumSize(_btnClose, boldFont, scale, 120, 56, 32, 20);

            int buttonHeight = _buttonRow.GetPreferredSize(new Size(contentWidth, 0)).Height;
            int introHeight = _lblIntro.GetPreferredSize(new Size(contentWidth, 0)).Height;
            int statusHeight = Math.Max((int)(96 * scale), _lblStatus.GetPreferredSize(new Size(contentWidth, 0)).Height);
            int availableSurfaceHeight = Math.Max((int)(160 * scale), maxFitHeight - Padding.Vertical - introHeight - statusHeight - buttonHeight - (int)(36 * scale));
            int surfaceHeight = Math.Min((int)(280 * scale), availableSurfaceHeight);

            _surface.MinimumSize = new Size(contentWidth, surfaceHeight);
            _surface.Size = _surface.MinimumSize;
            _lblStatus.MinimumSize = new Size(contentWidth, statusHeight);
            _lblStatus.Size = _lblStatus.MinimumSize;
            _buttonRow.WrapContents = _buttonRow.GetPreferredSize(Size.Empty).Width > contentWidth;

            int preferredHeight = _layoutHost.GetPreferredSize(new Size(contentWidth, 0)).Height + Padding.Vertical;
            int targetWindowHeight = Math.Min(preferredHeight, maxFitHeight);
            int minWindowWidth = Math.Min(targetWindowWidth, maxFitWidth);
            int minWindowHeight = Math.Min(targetWindowHeight, maxFitHeight);

            ClientSize = new Size(minWindowWidth, targetWindowHeight);
            DialogLayoutHelper.ClampFormSize(this, minWindowWidth, minWindowHeight, maxFitWidth, maxFitHeight, ApplySmartPosition);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] UpdateMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 將對話框位置限制在螢幕可視範圍內，避免視窗超出邊界。
    /// </summary>
    private void ApplySmartPosition()
    {
        try
        {
            if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
            {
                Location = clampedLocation;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamepadCalibrationDialog] ApplySmartPosition 失敗：{ex.Message}");
        }
    }
}
