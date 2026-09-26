using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語編輯對話框（版面配置與視覺分部）。
/// <para>本分部檔案包含 DPI 與系統偏好變更處理、字數標籤、最小尺寸、智慧定位，以及輸入框焦點視覺等成員。</para>
/// </summary>
internal sealed partial class PhraseEditDialog
{
    /// <summary>
    /// 建立控制項 Handle 後套用最小尺寸、系統事件訂閱與初始定位。
    /// </summary>
    /// <param name="e">控制項事件參數。</param>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        UpdateMinimumSize();

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        this.SafeBeginInvoke(ApplySmartPosition);
    }

    /// <summary>
    /// DPI 變更時重新量測按鈕尺寸與對話框最小尺寸。
    /// </summary>
    /// <param name="e">DPI 變更事件參數。</param>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);

            this.SafeInvoke(() =>
            {
                try
                {
                    // DPI 變更後刷新字型快取引用（共享快取依 DPI 分開儲存，必須重新取得）。
                    _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Regular);
                    _boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);
                    Font = _a11yFont;

                    // 輸入框使用 2.0× 倍率字型（已明確設定，不繼承 Form.Font，需手動更新）。
                    Font sharedInputFont = MainForm.GetSharedA11yFont(
                        DeviceDpi,
                        FontStyle.Regular,
                        _a11yFont?.FontFamily,
                        2.0f);
                    _txtName.Font = sharedInputFont;
                    _txtContent.Font = sharedInputFont;

                    // 重新掛載眼動儀回饋，刷新 ButtonVisualState 中儲存的字型引用。
                    _btnOk.AttachEyeTrackerFeedback(
                        baseDescription: Strings.Phrase_A11y_Btn_Confirm_Desc,
                        regularFont: _a11yFont,
                        boldFont: _boldFont,
                        formCt: _cts?.Token ?? CancellationToken.None);

                    _btnCancel.AttachEyeTrackerFeedback(
                        baseDescription: Strings.Phrase_A11y_Btn_Cancel_Desc,
                        regularFont: _a11yFont,
                        boldFont: _boldFont,
                        formCt: _cts?.Token ?? CancellationToken.None);

                    UpdateButtonMinimumSizes();
                    UpdateMinimumSize();
                    ApplySmartPosition();
                    _btnOk.Invalidate();
                    _btnCancel.Invalidate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語編輯] OnDpiChanged 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] OnDpiChanged 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 使用者結束調整視窗大小後重新套用智慧定位。
    /// </summary>
    /// <param name="e">事件參數。</param>
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        ApplySmartPosition();
    }

    /// <summary>
    /// Handle 銷毀時確保解除靜態系統事件訂閱
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
    /// 系統偏好設定變更時同步更新按鈕尺寸、最小尺寸與焦點視覺
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">系統偏好設定事件參數。</param>
    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            if (e.Category is UserPreferenceCategory.Accessibility or
                UserPreferenceCategory.Color or
                UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    UpdateButtonMinimumSizes();
                    UpdateMinimumSize(forceRecalculate: true);
                    ApplySmartPosition();

                    _btnOk.Invalidate();
                    _btnCancel.Invalidate();

                    UpdateNameCharCount();
                    UpdateContentCharCount();

                    TextBox? active = GetActiveTextBox();

                    if (active != null)
                    {
                        ApplyInputBoxStrongVisual(active);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] SystemEvents_UserPreferenceChanged 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 輸入框取得焦點時套用強化焦點視覺。
    /// </summary>
    /// <param name="sender">觸發事件的輸入框。</param>
    /// <param name="eventArgs">事件參數。</param>
    private void HandleTextBoxEnter(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (sender is TextBox textBox)
            {
                ApplyInputBoxStrongVisual(textBox);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleTextBoxEnter 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 輸入框失去焦點時還原一般視覺樣式。
    /// </summary>
    /// <param name="sender">觸發事件的輸入框。</param>
    /// <param name="eventArgs">事件參數。</param>
    private void HandleTextBoxLeave(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (sender is TextBox textBox)
            {
                ResetInputBoxVisual(textBox);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleTextBoxLeave 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 套用與主輸入框一致的強視覺焦點樣式（高對比優先，其次主題感知反轉）。
    /// </summary>
    /// <param name="textBox">要套用焦點樣式的輸入框。</param>
    private static void ApplyInputBoxStrongVisual(TextBox textBox)
    {
        if (textBox.IsDisposed)
        {
            return;
        }

        if (SystemInformation.HighContrast)
        {
            textBox.BackColor = SystemColors.Highlight;
            textBox.ForeColor = SystemColors.HighlightText;

            return;
        }

        if (textBox.IsDarkModeActive())
        {
            // 深色模式：反轉為白底黑字。
            textBox.BackColor = Color.White;
            textBox.ForeColor = Color.Black;
        }
        else
        {
            // 淺色模式：反轉為黑底白字。
            textBox.BackColor = Color.Black;
            textBox.ForeColor = Color.White;
        }
    }

    /// <summary>
    /// 還原輸入框為系統預設背景與前景色
    /// </summary>
    /// <param name="tb">目標輸入框。</param>
    private static void ResetInputBoxVisual(TextBox tb)
    {
        if (tb.IsDisposed)
        {
            return;
        }

        tb.BackColor = Color.Empty;
        tb.ForeColor = Color.Empty;
    }

    /// <summary>
    /// 更新片語名稱字元數提示標籤（{current}/{max}），近上限時顯示橙色。
    /// </summary>
    private void UpdateNameCharCount()
    {
        if (_lblNameCount == null || _lblNameCount.IsDisposed)
        {
            return;
        }

        int len = _txtName.TextLength;
        int max = AppSettings.MaxPhraseNameLength;
        string countLabel = GetPhraseTextOrFallback("Phrase_Edit_Name_Count", "Name length: ");
        string countText = $"{countLabel}{len}/{max}";

        _lblNameCount.Text = countText;
        _lblNameCount.AccessibleName = countText;
        _lblNameCount.AccessibleDescription = $"{Strings.Phrase_A11y_Edit_Name_Desc} {countText}";

        if (SystemInformation.HighContrast)
        {
            _lblNameCount.ForeColor = Color.Empty;

            return;
        }

        _lblNameCount.ForeColor = len >= max - 10 ?
            Color.DarkOrange :
            Color.Empty;
    }

    /// <summary>
    /// 更新片語內容字元數提示標籤（{current}/{max}），近上限時顯示橙色。
    /// </summary>
    private void UpdateContentCharCount()
    {
        if (_lblContentCount == null || _lblContentCount.IsDisposed)
        {
            return;
        }

        int len = _txtContent.TextLength;
        int max = AppSettings.MaxInputLength;
        string countLabel = GetPhraseTextOrFallback("Phrase_Edit_Content_Count", "Content length: ");
        string countText = $"{countLabel}{len}/{max}";

        _lblContentCount.Text = countText;
        _lblContentCount.AccessibleName = countText;
        _lblContentCount.AccessibleDescription = $"{Strings.Phrase_A11y_Edit_Content_Desc} {countText}";

        if (SystemInformation.HighContrast)
        {
            _lblContentCount.ForeColor = Color.Empty;

            return;
        }

        _lblContentCount.ForeColor = len >= max - 50 ?
            Color.DarkOrange :
            Color.Empty;
    }

    /// <summary>
    /// 更新按鈕最小尺寸（抗抖動 + WCAG 2.5.5 AAA 44×44）
    /// </summary>
    private void UpdateButtonMinimumSizes()
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        UpdateSingleButtonMinimumSize(_btnOk, scale);
        UpdateSingleButtonMinimumSize(_btnCancel, scale);
    }

    /// <summary>
    /// 預先鎖定動態字數標籤的最小寬度，避免數值變化時造成版面抖動。
    /// </summary>
    private void UpdateCountLabelMinimumWidths()
    {
        UpdateSingleCountLabelMinimumWidth(
            _lblNameCount,
            GetPhraseTextOrFallback("Phrase_Edit_Name_Count", "Name length: "),
            AppSettings.MaxPhraseNameLength);
        UpdateSingleCountLabelMinimumWidth(
            _lblContentCount,
            GetPhraseTextOrFallback("Phrase_Edit_Content_Count", "Content length: "),
            AppSettings.MaxInputLength);
    }

    /// <summary>
    /// 鎖定單一動態標籤的寬度，讓最長狀態文字也不會改變物理尺寸。
    /// </summary>
    /// <param name="label">要鎖定寬度的標籤；為 null 時略過。</param>
    /// <param name="labelPrefix">標籤前綴文字，用於計算最寬狀態。</param>
    /// <param name="maxValue">欄位的最大值，用於計算最寬文字。</param>
    private static void UpdateSingleCountLabelMinimumWidth(Label? label, string labelPrefix, int maxValue)
    {
        if (label == null ||
            label.IsDisposed)
        {
            return;
        }

        string widestText = $"{labelPrefix}{maxValue}/{maxValue}";
        Size measured = TextRenderer.MeasureText(
            widestText,
            label.Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        int width = Math.Max(label.MinimumSize.Width, measured.Width + 6);
        int height = Math.Max(label.MinimumSize.Height, measured.Height);

        label.AutoSize = false;
        label.MinimumSize = new Size(width, height);
        label.Size = new Size(width, height);
    }

    /// <summary>
    /// 更新單一按鈕的最小尺寸，避免焦點加粗造成版面抖動
    /// </summary>
    /// <param name="btn">目標按鈕。</param>
    /// <param name="scale">目前 DPI 縮放比例。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateSingleButtonMinimumSize(Button btn, float scale)
    {
        try
        {
            if (btn.IsDisposed) return;

            Font boldFont = _boldFont ?? MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

            DialogLayoutHelper.UpdateButtonMinimumSize(btn, boldFont, scale, 44, 44, 24, 16);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] UpdateSingleButtonMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 依 DPI 更新最小尺寸，讓片語名稱／內容輸入框有更充足的可視範圍。
    /// </summary>
    /// <param name="forceRecalculate">是否強制重新計算，忽略 DPI 未變更的快取防呆。</param>
    private void UpdateMinimumSize(bool forceRecalculate = false)
    {
        float currentDpi = DeviceDpi;

        if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi, forceRecalculate))
        {
            return;
        }

        float scale = currentDpi / AppSettings.BaseDpi;

        UpdateCountLabelMinimumWidths();

        int desiredMinWidth = (int)(BaseDialogMinWidth * scale);

        Rectangle workArea = Screen.GetWorkingArea(this);

        // 小尺寸螢幕保護：保留 40px 邊界，避免高縮放下最小尺寸超出可視區。
        (int maxFitWidth, int maxFitHeight) = DialogLayoutHelper.GetMaxFitSize(workArea);

        int
            // 正常情況至少保留 320px 的可編輯寬度；若工作區本身更窄，則以工作區上限為準。
            minWidth = maxFitWidth >= 320 ?
                Math.Clamp(desiredMinWidth, 320, maxFitWidth) :
                maxFitWidth;

        int desiredMinHeight = (int)(340 * scale),
            minH = Math.Min(desiredMinHeight, maxFitHeight);

        DialogLayoutHelper.ClampFormSize(this, minWidth, minH, maxFitWidth, maxFitHeight, ApplySmartPosition);
    }

    /// <summary>
    /// 保持對話框位於目前螢幕可視範圍內
    /// </summary>
    private void ApplySmartPosition()
    {
        if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
        {
            Location = clampedLocation;
        }
    }
}
