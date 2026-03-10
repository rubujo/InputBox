using System.ComponentModel;
using System.Diagnostics;
using InputBox.Core.Extensions;
using InputBox.Core.Utilities;

namespace InputBox.Core.Services;

/// <summary>
/// 觸控式鍵盤服務
/// </summary>
internal class TouchKeyboardService
{
    /// <summary>
    /// 是否正在啟動中（原子旗標：0=否, 1=是）
    /// </summary>
    private static volatile int _isOpening = 0;

    /// <summary>
    /// 嘗試開啟觸控式鍵盤
    /// </summary>
    /// <returns>是否成功啟動程序</returns>
    public static bool TryOpen()
    {
        // 進入保護。
        if (Interlocked.CompareExchange(ref _isOpening, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            // 動態取得路徑。
            string? strTabTipPath = SystemHelper.GetTabTipPath();

            if (string.IsNullOrEmpty(strTabTipPath))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo(strTabTipPath)
            {
                UseShellExecute = true
            });

            return true;
        }
        catch (Win32Exception ex)
        {
            // 處理例如：使用者在 UAC 提權對話框點擊了「否」，或是檔案被鎖定。
            Debug.WriteLine($"無法啟動觸控式鍵盤（Win32）：{ex.Message}");

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"無法啟動觸控式鍵盤：{ex.Message}");

            return false;
        }
        finally
        {
            // 延遲一下再重置，防止快速連點。
            Task.Run(async () =>
            {
                await Task.Delay(500);

                Interlocked.Exchange(ref _isOpening, 0);
            }).SafeFireAndForget();
        }
    }
}