using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語管理對話框（視覺與佈局分部）。
/// <para>本分部檔案包含 DPI 與系統偏好變更處理、按鈕建立與字型、最小尺寸、透明度與智慧定位等成員。</para>
/// </summary>
internal sealed partial class PhraseManagerDialog
{
    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);

            ApplyFont();
            RefreshList();
            UpdateButtonStates();
            UpdateButtonMinimumSizes();
            UpdateMinimumSize();

            this.SafeBeginInvoke(() =>
            {
                try
                {
                    UpdateOpacity();
                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "PhraseManagerDialog.OnHandleCreated 延遲邏輯失敗");

                    Debug.WriteLine($"[片語] OnHandleCreated 延遲邏輯失敗：{ex.Message}");
                }
            });

            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "PhraseManagerDialog.OnHandleCreated 失敗");

            Debug.WriteLine($"[片語] OnHandleCreated 失敗：{ex.Message}");
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);

            this.SafeInvoke(() =>
            {
                try
                {
                    ApplyFont();
                    UpdateButtonMinimumSizes();
                    UpdateMinimumSize();
                    ApplySmartPosition();
                    InvalidateAllButtons();
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "PhraseManagerDialog.OnDpiChanged 延遲邏輯失敗");

                    Debug.WriteLine($"[片語] OnDpiChanged 失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "PhraseManagerDialog.OnDpiChanged 失敗");

            Debug.WriteLine($"[片語] OnDpiChanged 失敗：{ex.Message}");
        }
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        ApplySmartPosition();
    }

    /// <summary>
    /// Handle 銷毀時解除靜態系統事件訂閱，避免遺留參考。
    /// </summary>
    /// <param name="e">控制項事件參數。</param>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    /// <summary>
    /// 建立動作按鈕
    /// </summary>
    /// <param name="text">按鈕文字</param>
    /// <param name="a11yDesc">輔助功能描述</param>
    /// <param name="mnemonic">快捷鍵字元</param>
    /// <returns>已設定樣式、快捷鍵與無障礙屬性的動作按鈕執行個體。</returns>
    private static Button CreateActionButton(
        string text,
        string a11yDesc,
        char mnemonic)
    {
        Button btn = new()
        {
            Text = ControlExtensions.GetMnemonicText(text, mnemonic),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = text,
            AccessibleDescription = a11yDesc,
            AccessibleRole = AccessibleRole.PushButton,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            Margin = new Padding(2, 2, 2, 2),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        btn.FlatAppearance.BorderSize = 0;

        // 暫存 base description 到 Tag 供後續 AttachEyeTrackerFeedback 使用。
        btn.Tag = a11yDesc;

        return btn;
    }

    /// <summary>
    /// 套用字型（Regular + Bold）並掛載眼動儀擴充
    /// </summary>
    private void ApplyFont()
    {
        _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Regular);
        _boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

        Font = _a11yFont;

        _lstPhrases.Font = _a11yFont;

        foreach (Control ctrl in _flpButtons.Controls)
        {
            if (ctrl is Button btn)
            {
                btn.Font = _a11yFont;
                btn.AttachEyeTrackerFeedback(
                    baseDescription: btn.Tag?.ToString() ?? string.Empty,
                    regularFont: _a11yFont,
                    boldFont: _boldFont,
                    formCt: _cts?.Token ?? CancellationToken.None);
            }
        }

        _btnClose.Font = _a11yFont;
        _btnClose.AttachEyeTrackerFeedback(
            baseDescription: Strings.Phrase_A11y_Btn_Close_Desc,
            regularFont: _a11yFont,
            boldFont: _boldFont,
            formCt: _cts?.Token ?? CancellationToken.None);

        _lblPhraseCount.Font = _a11yFont;
        UpdatePhraseCountLabelMinimumWidth();
    }

    /// <summary>
    /// 更新每個按鈕的最小尺寸（抗抖動 + WCAG 2.5.5 AAA 44×44）
    /// </summary>
    private void UpdateButtonMinimumSizes()
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        UpdateSingleButtonMinimumSize(_btnAdd, scale);
        UpdateSingleButtonMinimumSize(_btnEdit, scale);
        UpdateSingleButtonMinimumSize(_btnDelete, scale);
        UpdateSingleButtonMinimumSize(_btnMoveUp, scale);
        UpdateSingleButtonMinimumSize(_btnMoveDown, scale);
        UpdateSingleButtonMinimumSize(_btnClose, scale);
    }

    /// <summary>
    /// 更新單一按鈕的最小尺寸
    /// </summary>
    /// <param name="btn">要更新最小尺寸的按鈕。</param>
    /// <param name="scale">目前 DPI 相對於基準 DPI 的縮放比例。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateSingleButtonMinimumSize(Button btn, float scale)
    {
        try
        {
            if (btn.IsDisposed)
            {
                return;
            }
            Font boldFont = _boldFont ?? MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

            DialogLayoutHelper.UpdateButtonMinimumSize(btn, boldFont, scale, 44, 44, 24, 16);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] UpdateSingleButtonMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新最小尺寸（依按鈕面板實際內容高度動態計算）
    /// </summary>
    /// <param name="forceRecalculate">設為 <see langword="true"/> 強制重算，忽略 DPI 快取。</param>
    private void UpdateMinimumSize(bool forceRecalculate = false)
    {
        float currentDpi = DeviceDpi;

        if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi, forceRecalculate))
        {
            return;
        }

        float scale = currentDpi / AppSettings.BaseDpi;

        UpdatePhraseCountLabelMinimumWidth();

        // 加上非客戶區（標題列＋邊框）高度，MinimumSize 是外框尺寸。
        // OnHandleCreated 時 Height == ClientSize.Height（非客戶區尚未就緒），
        // 故以 SystemInformation 估算作為最低保底值。
        int nonClientH = DialogLayoutHelper.GetEstimatedNonClientHeight(this);

        // 以主版面實際偏好尺寸作為基準，避免最後一顆按鈕在 row 0 被裁切。
        _tlpMain.PerformLayout();

        Size preferred = _tlpMain.GetPreferredSize(Size.Empty);

        int desiredMinWidth = (int)(BaseDialogMinWidth * scale),
            minW = Math.Max(desiredMinWidth, preferred.Width + Padding.Horizontal),
            baseClientH = Math.Max(
                (int)(300 * scale) - nonClientH,
                preferred.Height + Padding.Vertical + (int)(8 * scale)),
            minH = baseClientH + nonClientH;

        Rectangle workArea = Screen.GetWorkingArea(this);

        (int maxFitW, int maxFitH) = DialogLayoutHelper.GetMaxFitSize(workArea);

        minW = Math.Min(minW, maxFitW);
        minH = Math.Min(minH, maxFitH);

        DialogLayoutHelper.ClampFormSize(this, minW, minH, maxFitW, maxFitH);
    }

    /// <summary>
    /// 更新視窗不透明度
    /// </summary>
    private void UpdateOpacity()
    {
        if (SystemInformation.HighContrast)
        {
            Opacity = 1.0;

            return;
        }

        Opacity = AppSettings.Current.WindowOpacity;
    }

    /// <summary>
    /// 智慧定位
    /// </summary>
    private void ApplySmartPosition()
    {
        if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
        {
            Location = clampedLocation;
        }
    }

    /// <summary>
    /// 強制重繪所有按鈕
    /// </summary>
    private void InvalidateAllButtons()
    {
        _btnAdd.Invalidate();
        _btnEdit.Invalidate();
        _btnDelete.Invalidate();
        _btnMoveUp.Invalidate();
        _btnMoveDown.Invalidate();
        _btnClose.Invalidate();
    }

    /// <summary>
    /// 系統偏好設定變更時同步更新尺寸、定位與按鈕視覺。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">系統偏好設定事件參數。</param>
    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            if (e.Category is UserPreferenceCategory.Accessibility or
                UserPreferenceCategory.Color or
                UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    try
                    {
                        UpdateButtonMinimumSizes();
                        UpdateMinimumSize(forceRecalculate: true);
                        ApplySmartPosition();
                        InvalidateAllButtons();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[片語] SystemEvents 更新失敗：{ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語] SystemEvents 處理失敗：{ex.Message}");
        }
    }
}
