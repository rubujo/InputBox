using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Services;
using System.Diagnostics;
using System.Media;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語編輯對話框（無障礙與驗證回饋分部）。
/// <para>本分部檔案包含 A11y 廣播、驗證失敗閃爍，以及字數上限回饋等成員。</para>
/// </summary>
internal sealed partial class PhraseEditDialog
{
    /// <summary>
    /// 針對驗證失敗的輸入框提供焦點、音效、震動與視覺提示
    /// </summary>
    /// <param name="target">驗證失敗的輸入框。</param>
    /// <param name="message">要播報的錯誤訊息。</param>
    private void NotifyValidationFailure(TextBox target, string message)
    {
        if (target.CanFocus && !target.Focused)
        {
            target.Focus();
        }

        AnnounceA11y(message, interrupt: true);

        FeedbackService.PlaySound(SystemSounds.Hand);

        FeedbackService.VibrateAsync(
            _gamepadController,
            VibrationPatterns.ActionFail,
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();

        FlashValidationCueAsync(target).SafeFireAndForget();
    }

    /// <summary>
    /// 暫時閃爍輸入框以提供驗證失敗的視覺提示
    /// </summary>
    /// <param name="target">要閃爍提示的輸入框。</param>
    /// <returns>非同步作業。</returns>
    private async Task FlashValidationCueAsync(TextBox target)
    {
        if (target.IsDisposed ||
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
            bool isDark = target.IsDarkModeActive();
            Color alertColor = FlashAlertAnimator.GetAlertColor(isDark, SystemInformation.HighContrast);

            void ApplyAlertVisuals(float intensity)
            {
                if (target.IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                (Color back, Color fore) = FlashAlertAnimator.ComputeFrameColors(
                    intensity,
                    isDark,
                    alertColor,
                    SystemInformation.HighContrast);

                target.BackColor = back;
                target.ForeColor = fore;
            }

            await FlashAlertAnimator.RunAsync(this, ApplyAlertVisuals, token);
        }
        catch (OperationCanceledException)
        {
            // 正常取消。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] FlashValidationCueAsync 失敗：{ex.Message}");
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
                    if (target.IsDisposed ||
                        !IsHandleCreated)
                    {
                        return;
                    }

                    if (target.Focused)
                    {
                        ApplyInputBoxStrongVisual(target);
                    }
                    else
                    {
                        ResetInputBoxVisual(target);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語編輯] 驗證提示動畫 UI 還原失敗：{ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// 依剩餘字元數分級，僅在接近上限時回傳有效 bucket。
    /// </summary>
    /// <param name="remainingCharacters">距離上限的剩餘字元數。</param>
    /// <returns>警示等級（0 ~ 3）；超過警示閾值時回傳 -1。</returns>
    private static int GetTextLimitWarningBucket(int remainingCharacters)
    {
        return remainingCharacters switch
        {
            <= 0 => 3,
            <= 2 => 2,
            <= 5 => 1,
            <= TextLimitWarningThreshold => 0,
            _ => -1
        };
    }

    /// <summary>
    /// 當片語欄位長度變動時，提供接近字數上限的物理預警與硬牆回饋。
    /// </summary>
    /// <param name="textBox">監控中的輸入框。</param>
    /// <param name="maxLength">欄位的最大字元數限制。</param>
    /// <param name="lastObservedLength">上次觀察到的長度（ref，會被更新）。</param>
    /// <param name="lastWarningBucket">上次警示的 bucket 等級（ref，會被更新）。</param>
    private void HandleTextLimitFeedbackFromLengthChange(
        TextBox textBox,
        int maxLength,
        ref int lastObservedLength,
        ref int lastWarningBucket)
    {
        if (textBox.IsDisposed)
        {
            return;
        }

        int currentLength = textBox.TextLength;

        if (currentLength < lastObservedLength)
        {
            lastObservedLength = currentLength;
            lastWarningBucket = currentLength >= maxLength - TextLimitWarningThreshold ?
                GetTextLimitWarningBucket(maxLength - currentLength) :
                -1;
            return;
        }

        int remainingCharacters = maxLength - currentLength;

        if (remainingCharacters > TextLimitWarningThreshold)
        {
            lastObservedLength = currentLength;
            lastWarningBucket = -1;
            return;
        }

        int currentBucket = GetTextLimitWarningBucket(remainingCharacters);

        if (currentLength > lastObservedLength &&
            (currentBucket != lastWarningBucket || remainingCharacters <= 2))
        {
            if (remainingCharacters <= 0)
            {
                FeedbackService.PlaySound(SystemSounds.Beep);
            }

            FeedbackService.VibrateSequenceAsync(
                _gamepadController,
                VibrationPatterns.GetTextLimitSequence(
                    remainingCharacters,
                    _gamepadController?.VibrationMotorSupport ?? VibrationMotorSupport.None),
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }

        lastObservedLength = currentLength;
        lastWarningBucket = currentBucket;
    }

    /// <summary>
    /// 使用者在字數已滿時仍嘗試輸入一般字元，播放硬牆回饋。
    /// </summary>
    /// <param name="textBox">目標輸入框。</param>
    /// <param name="maxLength">欄位的最大字元數限制。</param>
    /// <param name="lastWallUtc">上次觸發硬牆回饋的 UTC 時間戳（ref，用於節流）。</param>
    /// <param name="e">按鍵事件引數。</param>
    private void HandleTextLimitKeyPress(TextBox textBox, int maxLength, ref DateTime lastWallUtc, KeyPressEventArgs e)
    {
        if (textBox.IsDisposed ||
            char.IsControl(e.KeyChar) ||
            textBox.TextLength < maxLength)
        {
            return;
        }

        if ((DateTime.UtcNow - lastWallUtc).TotalMilliseconds < RepeatedBoundaryFeedbackThrottleMs)
        {
            return;
        }

        lastWallUtc = DateTime.UtcNow;
        FeedbackService.PlaySound(SystemSounds.Beep);
        FeedbackService.VibrateSequenceAsync(
            _gamepadController,
            VibrationPatterns.GetTextLimitSequence(
                0,
                _gamepadController?.VibrationMotorSupport ?? VibrationMotorSupport.None),
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
    }

    /// <summary>
    /// 廣播無障礙訊息
    /// </summary>
    /// <param name="message">要廣播的訊息</param>
    /// <param name="interrupt">是否中斷目前的廣播</param>
    private void AnnounceA11y(string message, bool interrupt = false)
    {
        if (IsDisposed ||
            string.IsNullOrEmpty(message))
        {
            return;
        }

        if (Owner is MainForm mainForm)
        {
            mainForm.AnnounceA11y(message, interrupt);
        }
        else if (Owner is PhraseManagerDialog phraseManager)
        {
            // 嘗試寫往主視窗。
            if (phraseManager.Owner is MainForm main)
            {
                main.AnnounceA11y(message, interrupt);

                return;
            }
        }

        // 本地備援
        long currentId = Interlocked.Increment(ref _a11yDebounceId);

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AppSettings.AudioDuckingDelayMs, _cts?.Token ?? CancellationToken.None);

                if (Interlocked.Read(ref _a11yDebounceId) == currentId &&
                    !IsDisposed &&
                    IsHandleCreated)
                {
                    await this.SafeInvokeAsync(() =>
                        _announcer.Announce(message, interrupt && AppSettings.Current.A11yInterruptEnabled));
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[片語編輯] A11y 廣播失敗：{ex.Message}");
            }
        },
        _cts?.Token ?? CancellationToken.None)
        .SafeFireAndForget();
    }
}
