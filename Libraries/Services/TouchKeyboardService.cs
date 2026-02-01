using System.Diagnostics;

namespace InputBox.Libraries.Services;

/// <summary>
/// 觸控式鍵盤服務
/// </summary>
internal class TouchKeyboardService
{
    /// <summary>
    /// 嘗試開啟觸控式鍵盤
    /// </summary>
    /// <returns>是否成功啟動程序</returns>
#pragma warning disable CA1822 // 將成員標記為靜態
    public bool TryOpen()
#pragma warning restore CA1822 // 將成員標記為靜態
    {
        try
        {
            // 動態取得路徑。
            string? strTabTipPath = CommonMethods.GetTabTipPath();

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
        catch
        {
            return false;
        }
    }
}