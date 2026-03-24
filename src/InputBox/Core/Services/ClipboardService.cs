using System.Diagnostics;
using System.Runtime.InteropServices;
using InputBox.Core.Configuration;

namespace InputBox.Core.Services;

/// <summary>
/// 剪貼簿服務
/// </summary>
internal class ClipboardService
{
    /// <summary>
    /// 重試時的通知回呼（主要用於 A11y 廣播通知）
    /// </summary>
    public static Action? OnRetry { get; set; }

    /// <summary>
    /// 剪貼簿存取號誌（確保一次只有一個重試迴圈在運行）
    /// </summary>
    private static readonly SemaphoreSlim ClipboardSemaphore = new(1, 1);

    /// <summary>
    /// 嘗試將文字寫入剪貼簿（包含重試機制，建議在 UI 執行緒呼叫以確保 STA 環境）
    /// </summary>
    /// <param name="text">要寫入的文字</param>
    /// <param name="timeoutMs">最大超時時間（毫秒），預設 2000ms</param>
    /// <returns>是否成功</returns>
    public static async Task<bool> TrySetTextAsync(
        string? text,
        int timeoutMs = 2000)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // 使用 WaitAsync(0) 立即嘗試取得號誌，防止重疊的重試迴圈競爭。
        // 如果目前已有執行緒正在重試中，新的請求應直接放棄或稍微等待。
        // 這裡設定 500ms 等待，若前一個寫入仍未完成則視為衝突。
        if (!await ClipboardSemaphore.WaitAsync(500))
        {
            Debug.WriteLine("[剪貼簿] 寫入請求因目前已有執行緒正在存取中而取消。");

            return false;
        }

        try
        {
            // 標準化換行符。
            string normalizedSource = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", "\r\n");

            using CancellationTokenSource cts = new(timeoutMs);

            int retryCount = 0;

            while (!cts.IsCancellationRequested &&
                retryCount < AppSettings.ClipboardMaxRetries)
            {
                try
                {
                    // WinForms 剪貼簿 API 嚴格要求在 STA 執行緒執行。
                    // 我們嘗試取得目前活動視窗來進行 Invoke，確保執行緒安全性。
                    Form? syncForm = Application.OpenForms.Cast<Form>().FirstOrDefault();

                    if (syncForm != null &&
                        syncForm.InvokeRequired)
                    {
                        syncForm.Invoke(new Action(() => Clipboard.SetText(normalizedSource)));
                    }
                    else
                    {
                        Clipboard.SetText(normalizedSource);
                    }

                    // 寫入後稍微等待，確保作業系統已完成通知廣播。
                    await Task.Delay(AppSettings.ClipboardBufferDelayMs, cts.Token);

                    // 驗證寫入結果。
                    string clipboardText = string.Empty;

                    if (syncForm != null &&
                        syncForm.InvokeRequired)
                    {
                        syncForm.Invoke(new Action(() => clipboardText = Clipboard.GetText()));
                    }
                    else
                    {
                        clipboardText = Clipboard.GetText();
                    }

                    if (!string.IsNullOrEmpty(clipboardText))
                    {
                        string normalizedClipboard = clipboardText
                            .Replace("\r\n", "\n")
                            .Replace("\r", "\n")
                            .Replace("\n", "\r\n");

                        if (string.Equals(normalizedSource, normalizedClipboard, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[剪貼簿] 寫入超時。");

                    break;
                }
                catch (ExternalException)
                {
                    // 剪貼簿被佔用（CLIPBRD_E_CANT_OPEN），進行指數退避。
                    // 使用局部變數捕捉快照，確保執行緒安全性。
                    Action? retryAction = OnRetry;

                    if (retryCount == 3 &&
                        retryAction != null)
                    {
                        retryAction.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[剪貼簿] 寫入異常：{ex.Message}");

                    // 對於非 ExternalException 的錯誤，重試可能無效（如記憶體不足）。
                    break;
                }

                retryCount++;

                if (retryCount >= AppSettings.ClipboardMaxRetries)
                {
                    break;
                }

                // 指數退避延遲。
                int baseDelay = AppSettings.Current.ClipboardRetryDelay * (int)Math.Pow(2, retryCount - 1);

                try
                {
                    await Task.Delay(Math.Min(baseDelay, AppSettings.ClipboardMaxRetryDelayMs), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return false;
        }
        finally
        {
            ClipboardSemaphore.Release();
        }
    }
}