using System.Diagnostics;
using Microsoft.Win32;

namespace InputBox.Libraries;

/// <summary>
/// 通用方法
/// </summary>
public static partial class CommonMethods
{
    /// <summary>
    /// 取得 TabTip.exe 或 TabTip32.exe 的絕對路徑
    /// </summary>
    /// <remarks>
    /// 搜尋順序：
    /// 1. Registry（HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths）。
    /// 2. WOW64 特殊路徑（針對 64 位元系統上的 32 位元應用程式）。
    /// 3. 標準 Common Files 路徑。
    /// 4. 硬編碼備用路徑。
    /// </remarks>
    /// <returns>檔案完整路徑，若找不到則回傳 null。</returns>
    public static string? GetTabTipPath()
    {
        List<string> listPossiblePath = [];

        // 策略 1：從 Registry 搜尋（最準確）。
        // 我們同時檢查 TabTip.exe 和 TabTip32.exe 的註冊機碼。
        string[] arrayRegistryKey =
        [
            "TabTip.exe",
            "TabTip32.exe"
        ];

        foreach (string strExeName in arrayRegistryKey)
        {
            try
            {
                // 使用 RegistryView.Registry64 以確保在 WOW64 模式下能讀到正確的系統路徑。
                using RegistryKey rkBaseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using RegistryKey? rkSubKey = rkBaseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{strExeName}");

                if (rkSubKey?.GetValue(null) is string strPath)
                {
                    // 清理路徑（先 Trim() 去除空白，再 Trim('"') 去除前後的雙引號，保留中間的內容不變）並展開環境變數。
                    strPath = Environment.ExpandEnvironmentVariables(strPath.Trim().Trim('"'));

                    if (!string.IsNullOrEmpty(strPath))
                    {
                        listPossiblePath.Add(strPath);
                    }
                }
            }
            catch
            {
                // 忽略 Registry 存取錯誤。 
            }
        }

        // 策略 2：針對 WOW64 環境的特殊處理（32-bit 應用程式在 64-bit 作業系統）。
        // 如果是 64 位元系統但目前的處理程序是 32 位元時，優先找 TabTip32.exe。
        bool isWow64 = Environment.Is64BitOperatingSystem &&
            !Environment.Is64BitProcess;

        if (isWow64)
        {
            listPossiblePath.Add(@"%CommonProgramFiles(x86)%\microsoft shared\ink\TabTip32.exe");
            listPossiblePath.Add(@"%CommonProgramFiles(x86)%\microsoft shared\ink\TabTip.exe");
        }

        string strProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            strProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // 策略 3：標準路徑與備用路徑。
        string[] arrayStandardPath =
        [
            // 標準 64 位元（或 32 位元系統）路徑。
            @"%CommonProgramFiles%\microsoft shared\ink\TabTip.exe",
            // 確保 x86 路徑被包含（如果上面沒加過）。
            @"%CommonProgramFiles(x86)%\microsoft shared\ink\TabTip.exe",
            // System32（注意：在 WOW64 下會被導向 SysWOW64，除非用 SysNative）。
            @"%SystemRoot%\System32\TabTip.exe",
            // 最後手段：硬編碼路徑。
            Path.Combine(strProgramFiles, @"Common Files\microsoft shared\ink\TabTip.exe"),
            Path.Combine(strProgramFilesX86, @"Common Files\microsoft shared\ink\TabTip.exe")
        ];

        listPossiblePath.AddRange(arrayStandardPath);

        // 移除重複路徑，避免無謂的 IO 檢查。
        listPossiblePath = [.. listPossiblePath.Distinct()];

        //　執行搜尋。
        foreach (string strRawPath in listPossiblePath)
        {
            if (string.IsNullOrWhiteSpace(strRawPath))
            {
                continue;
            }

            try
            {
                // 展開環境變數（例如 %CommonProgramFiles% -> C:\Program Files\Common Files）。
                string strFullPath = Environment.ExpandEnvironmentVariables(strRawPath);

                if (File.Exists(strFullPath))
                {
                    Debug.WriteLine($"[TabTip] 找到路徑：{strFullPath}");

                    return strFullPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TabTip] 路徑檢查異常（{strRawPath}）：{ex.Message}");
            }
        }

        Debug.WriteLine("[TabTip] 找不到任何可用的 TabTip 執行檔。");

        return null;
    }
}