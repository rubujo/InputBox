using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 專門用於數值輸入的對話框（版面配置與視覺分部）。
/// <para>本分部檔案包含 DPI 與系統偏好變更處理、最小尺寸與按鈕約束、智慧定位、透明度，以及焦點與游標視覺等成員。</para>
/// </summary>
internal sealed partial class NumericInputDialog
{
    protected override void OnResizeEnd(EventArgs e)
    {
        try
        {
            base.OnResizeEnd(e);

            // 拖曳結束時執行智慧定位修正。
            ApplySmartPosition();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] OnResizeEnd 失敗：{ex.Message}");
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        try
        {
            base.OnHandleCreated(e);

            UpdateMinimumSize();

            // 使用 SafeBeginInvoke 讓字型替換邏輯排在 Handle 建立完成「之後」才執行。
            this.SafeBeginInvoke(() =>
            {
                try
                {
                    // 套用透明度。
                    UpdateOpacity();

                    // 執行初始位置檢查。
                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "NumericInputDialog.OnHandleCreated 延遲邏輯失敗");

                    Debug.WriteLine($"[NumericInputDialog] OnHandleCreated 延遲邏輯失敗：{ex.Message}");
                }
            });

            // 先解除再訂閱靜態事件，防止 Handle 重建時產生重複訂閱。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "NumericInputDialog.OnHandleCreated 失敗");

            Debug.WriteLine($"[NumericInputDialog] OnHandleCreated 失敗：{ex.Message}");
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        try
        {
            base.OnDpiChanged(e);

            // 當 DPI 變更時，強制全視窗重新讀取全域共享字體。
            this.SafeInvoke(() =>
            {
                try
                {
                    // 重新取得 A11y 字型實例（從快取池取得）。
                    _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi);

                    // 同步更新所有按鈕的基礎字體。
                    if (_a11yFont != null)
                    {
                        _btnOk!.Font = _a11yFont;
                        _btnCancel!.Font = _a11yFont;
                        _btnPlus!.Font = _a11yFont;
                        _btnMinus!.Font = _a11yFont;
                        _btnReset!.Font = _a11yFont;
                    }

                    // 數值顯示區字體需重新依據新縮放比例建立。
                    if (_a11yFont != null &&
                        _nud != null)
                    {
                        // 2.0x 放大字體（來自共享快取，不需手動回收）。
                        _nudFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, _a11yFont.FontFamily, 2.0f);

                        _nud.Font = _nudFont;
                    }

                    // 更新佈局約束。
                    UpdateMinimumSize();

                    // 強制所有控制項重新套用最新主題與字體。
                    UpdateFocusVisuals(_nud?.Focused == true || (_nud?.ContainsFocus == true));

                    ApplySmartPosition();
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "NumericInputDialog.OnDpiChanged 延遲邏輯失敗");

                    Debug.WriteLine($"[NumericInputDialog] OnDpiChanged 延遲邏輯失敗：{ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "NumericInputDialog.OnDpiChanged 失敗");

            Debug.WriteLine($"[NumericInputDialog] OnDpiChanged 失敗：{ex.Message}");
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            if (e.Category == UserPreferenceCategory.Accessibility ||
                e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    try
                    {
                        UpdateMinimumSize();

                        UpdateFocusVisuals(_nud?.Focused == true || (_nud?.ContainsFocus == true));
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogException(ex, "[NumericInputDialog] SystemEvents 更新失敗");

                        Debug.WriteLine($"[NumericInputDialog] SystemEvents 更新失敗：{ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "[NumericInputDialog] SystemEvents 處理失敗");

            Debug.WriteLine($"[NumericInputDialog] SystemEvents 處理失敗：{ex.Message}");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            // 確保靜態事件在視窗控制項控制代碼銷毀時被絕對釋放。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    /// <summary>
    /// 更新控制項焦點狀態的視覺表現
    /// </summary>
    /// <param name="isFocused">指示控制項是否具有焦點</param>
    private void UpdateFocusVisuals(bool isFocused)
    {
        try
        {
            if (_nud == null ||
                _nud.IsDisposed)
            {
                return;
            }

            // 綜合判斷：只要數值框本身有焦點，或是旁邊的加減按鈕有焦點，數值框都應該保持高亮。
            bool shouldHighlight = isFocused ||
                (_btnPlus != null && _btnPlus.Focused) ||
                (_btnMinus != null && _btnMinus.Focused);

            if (shouldHighlight)
            {
                if (SystemInformation.HighContrast)
                {
                    _nud.UpdateRecursive(SystemColors.Highlight, SystemColors.HighlightText);
                }
                else
                {
                    bool isDark = this.IsDarkModeActive();

                    // 淺色模式：黑底白字、深色模式：白底黑字。
                    _nud.UpdateRecursive(
                        isDark ? Color.White : Color.Black,
                        isDark ? Color.Black : Color.White);
                }
            }
            else
            {
                _nud.ResetThemeRecursive();
            }

            // 當具有焦點時，強化游標。
            if (isFocused)
            {
                UpdateCaretWidth();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateFocusVisuals 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 根據 DPI 與無障礙設定更新數值框內部游標寬度
    /// </summary>
    private void UpdateCaretWidth()
    {
        try
        {
            if (_nud == null ||
                _nud.IsDisposed ||
                !_nud.IsHandleCreated)
            {
                return;
            }

            // 尋找 NUD 內部的 TextBox 子控制項。
            TextBox? innerTextBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

            if (innerTextBox == null ||
                !innerTextBox.IsHandleCreated)
            {
                return;
            }

            // 基礎寬度 3px，隨 DPI 縮放。
            float scale = DeviceDpi / AppSettings.BaseDpi;

            int caretWidth = (int)Math.Max(3, 3 * scale);

            // 高對比模式下額外加粗。
            if (SystemInformation.HighContrast)
            {
                caretWidth += (int)(2 * scale);
            }

            int caretHeight = innerTextBox.Height;

            // 若寬高與上次一致，則略過 Win32 API 調用，減少 UI 閃爍感。
            if (caretWidth == _lastCaretWidth &&
                caretHeight == _lastCaretHeight)
            {
                return;
            }

            _lastCaretWidth = caretWidth;
            _lastCaretHeight = caretHeight;

            // 使用 Win32 API 重新建立游標。
            if (User32.CreateCaret(innerTextBox.Handle, IntPtr.Zero, caretWidth, caretHeight))
            {
                User32.ShowCaret(innerTextBox.Handle);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateCaretWidth 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新視窗不透明度。
    /// </summary>
    private void UpdateOpacity()
    {
        try
        {
            // 根據規範，若系統開啟高對比模式，則強制為 1.0 以確保絕對可讀性。
            if (SystemInformation.HighContrast)
            {
                Opacity = 1.0;

                return;
            }

            // 數值輸入框鎖定 1.0 不透明度以確保輸入清晰度。
            Opacity = 1.0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateOpacity 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新視窗最小尺寸與按鈕佈局約束
    /// </summary>
    private void UpdateMinimumSize()
    {
        try
        {
            float currentDpi = DeviceDpi;

            if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi))
            {
                return;
            }

            float scale = currentDpi / AppSettings.BaseDpi;

            // 內容感知：更新所有按鈕的佈局約束（Bold 預測）。
            // 確保按鈕在獲得焦點變為粗體時，物理邊界保持絕對靜止，達成 Zero-Jitter。
            UpdateButtonConstraints(_btnOk, scale);
            UpdateButtonConstraints(_btnCancel, scale);
            UpdateButtonConstraints(_btnPlus, scale);
            UpdateButtonConstraints(_btnMinus, scale);
            UpdateButtonConstraints(_btnReset, scale);

            // 改用內容偏好尺寸作為基準。
            _tlpGrid?.PerformLayout();

            Size contentPref = _tlpGrid?.GetPreferredSize(Size.Empty) ??
                new((int)(450 * scale), (int)(250 * scale));

            Rectangle workArea = Screen.GetWorkingArea(this);

            // 計算邊框與標題列所需的額外空間（比照 HelpDialog.cs）。
            int frameW = SystemInformation.FrameBorderSize.Width * 2,
                frameH = SystemInformation.FrameBorderSize.Height * 2,
                captionH = SystemInformation.CaptionHeight;

            (int maxFitW, int maxFitH) = DialogLayoutHelper.GetMaxFitSize(workArea);

            // 視窗寬度：內容寬度 + 表單 Padding + 框架，以工作區上限為準。
            int formW = Math.Clamp(
                contentPref.Width + Padding.Horizontal + frameW + 8,
                (int)(450 * scale),
                maxFitW);

            // 視窗高度：依實際內容偏好尺寸計算，上限為工作區可用高度（保留 40px 邊界）。
            int desiredMinHeight = (int)(300 * scale),
                naturalH = contentPref.Height + Padding.Vertical + captionH + frameH + 8,
                formH = Math.Clamp(naturalH, desiredMinHeight, Math.Max(desiredMinHeight, maxFitH));

            MinimumSize = new Size(Math.Min((int)(450 * scale), maxFitW), desiredMinHeight);

            Size = new Size(formW, formH);

            // 佈局擴張後，執行智慧定位檢查。
            ApplySmartPosition();

            // 高對比模式下強制 100% 不透明度。
            if (SystemInformation.HighContrast)
            {
                UpdateOpacity();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新單個按鈕的佈局約束與最小尺寸鎖定
    /// </summary>
    /// <param name="btn">目標按鈕</param>
    /// <param name="scale">目前 DPI 縮放比例</param>
    private void UpdateButtonConstraints(Button? btn, float scale)
    {
        try
        {
            if (btn == null ||
                btn.IsDisposed)
            {
                return;
            }

            // 取得專屬於此視窗 DPI 的 Bold 字體實例。
            Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, btn.Font.FontFamily);

            // 眼動儀友善：抗抖動寬度鎖定（Anti-Jitter Lock）。
            DialogLayoutHelper.UpdateButtonMinimumSize(btn, boldFont, scale, 120, 60, 32, 24);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] UpdateButtonConstraints 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 執行智慧定位修正，確保視窗不會跑出螢幕邊界
    /// </summary>
    private void ApplySmartPosition()
    {
        // 脫離目前的佈局計算循環，確保所有 StartPosition 與 AutoSize 已處理完畢。
        this.SafeBeginInvoke(() =>
        {
            try
            {
                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                // 強制同步最新的實體佈局尺寸（關鍵：確保 Width／Height 是縮放後的真實值）。
                PerformLayout();

                if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
                {
                    Location = clampedLocation;

                    // 告知使用者視窗已修正位置。
                    AnnounceA11y(Strings.A11y_SnapBack);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] ApplySmartPosition 失敗：{ex.Message}");
            }
        });
    }
}
