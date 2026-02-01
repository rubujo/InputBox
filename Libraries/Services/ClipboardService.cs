using InputBox.Libraries.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputBox.Libraries.Services;

/// <summary>
/// 剪貼簿服務
/// </summary>
internal class ClipboardService
{
    /// <summary>
    /// 剪貼簿重試次數
    /// </summary>
    private const int Retry_Clipboard = 10;

    /// <summary>
    /// 剪貼簿延遲上限
    /// </summary>
    private const int MaxDelay_Clipboard = 200;

    /// <summary>
    /// 剪貼簿寫入後的緩衝時間（讓作業系統有時間反應）
    /// </summary>
    private const int Delay_ClipboardBuffer = 50;

    /// <summary>
    /// 嘗試將文字寫入剪貼簿（包含重試機制）
    /// </summary>
    /// <returns>是否成功</returns>
#pragma warning disable CA1822 // 將成員標記為靜態
    public async Task<bool> TrySetTextAsync(string text)
#pragma warning restore CA1822 // 將成員標記為靜態
    {
        // 依據設定檔的基礎延遲進行指數退避重試。
        // 預設約為：20ms -> 40ms -> 80ms……
        for (int i = 0; i < Retry_Clipboard; i++)
        {
            try
            {
                Clipboard.SetText(text);

                // 給 Windows 剪貼簿一個穩定時間。
                await Task.Delay(Delay_ClipboardBuffer);

                // 驗證：真的寫進去了嗎？
                if (Clipboard.GetText() == text)
                {
                    return true;
                }
            }
            catch (ExternalException)
            {
                // 剪貼簿暫時無法使用，將進行重試。
            }
            catch (Exception ex)
            {
                // 其他錯誤忽略，繼續重試。
                Debug.WriteLine($"剪貼簿寫入異常：{ex.Message}");
            }

            // 如果是最後一次嘗試失敗，就不等待了。
            if (i == Retry_Clipboard - 1)
            {
                break;
            }

            // 使用 AppSettings 的設定值來做指數退避等待。
            int baseDelay = AppSettings.Current.ClipboardRetryDelay * (int)Math.Pow(2, i);

            await Task.Delay(Math.Min(baseDelay, MaxDelay_Clipboard));
        }

        return false;
    }
}