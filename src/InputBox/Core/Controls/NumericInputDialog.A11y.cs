using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 專門用於數值輸入的對話框（無障礙輔助功能分部）。
/// <para>本分部檔案包含 A11y 廣播與視覺閃爍警示等成員。</para>
/// </summary>
internal sealed partial class NumericInputDialog
{
    /// <summary>
    /// 執行視覺警示閃爍效果
    /// </summary>
    /// <returns>Task</returns>
    private async Task FlashAlertAsync()
    {
        if (IsDisposed ||
            !IsHandleCreated ||
            Interlocked.CompareExchange(ref _isFlashing, 1, 0) != 0)
        {
            return;
        }

        // 僅在對話框生命週期仍有效時建立警示權杖，避免關閉途中留下失去連結的動畫。
        CancellationTokenSource? newAlertCts = _cts.TryCreateLinkedTokenSource();

        if (newAlertCts == null)
        {
            Interlocked.Exchange(ref _isFlashing, 0);

            return;
        }

        Interlocked.Exchange(ref _alertCts, newAlertCts)?.CancelAndDispose();

        CancellationToken token = newAlertCts.Token;

        try
        {
            bool isDark = this.IsDarkModeActive();
            Color alertColor = FlashAlertAnimator.GetAlertColor(isDark, SystemInformation.HighContrast);

            void ApplyAlertVisuals(float intensity)
            {
                if (IsDisposed ||
                    !IsHandleCreated ||
                    _nud == null)
                {
                    return;
                }

                (Color back, Color fore) = FlashAlertAnimator.ComputeFrameColors(
                    intensity,
                    isDark,
                    alertColor,
                    SystemInformation.HighContrast);

                _nud.UpdateRecursive(back, fore);
            }

            await FlashAlertAnimator.RunAsync(this, ApplyAlertVisuals, token);
        }
        catch (OperationCanceledException)
        {
            // 正常取消。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] FlashAlertAsync 失敗：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isFlashing, 0);
            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            // 確保 UI 狀態還原。
            this.SafeInvoke(() =>
            {
                try
                {
                    if (IsDisposed ||
                        !IsHandleCreated ||
                        _nud == null)
                    {
                        return;
                    }

                    // 關鍵修正：遞歸重設顏色，觸發 .NET 10 原生主題引擎還原正確配色，防止閃爍殘留。
                    _nud.ResetThemeRecursive();

                    // 恢復焦點視覺狀態。
                    UpdateFocusVisuals(_nud.Focused || _nud.ContainsFocus);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] FlashAlertAsync 還原失敗：{ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// 內部 A11y 廣播方法
    /// </summary>
    /// <param name="message">要廣播的訊息</param>
    /// <param name="interrupt">是否中斷目前的廣播</param>
    private void AnnounceA11y(
        string message,
        bool interrupt = false)
    {
        if (IsDisposed ||
            string.IsNullOrEmpty(message))
        {
            return;
        }

        long currentId = Interlocked.Increment(ref _a11yDebounceId);

        Task.Run(async () =>
        {
            try
            {
                // 統一 Audio Ducking 避讓延遲。
                await Task.Delay(AppSettings.AudioDuckingDelayMs, _cts?.Token ?? CancellationToken.None);

                if (Interlocked.Read(ref _a11yDebounceId) == currentId &&
                    !IsDisposed &&
                    IsHandleCreated)
                {
                    await this.SafeInvokeAsync(() =>
                        _announcer?.Announce(message, interrupt && AppSettings.Current.A11yInterruptEnabled));
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消。
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[A11y] 對話框本地廣播失敗：{ex.Message}");
            }
        },
        _cts?.Token ?? CancellationToken.None)
        .SafeFireAndForget();
    }

    /// <summary>
    /// 取得目前對話框所屬的主視窗
    /// </summary>
    /// <returns>若 Owner 為 <see cref="MainForm"/> 則回傳該實例，否則為 null。</returns>
    private MainForm? GetOwnerMainForm() => Owner as MainForm;
}
