using InputBox.Core.Interop;
using Microsoft.Win32;
using System.Diagnostics;

namespace InputBox.Core.Utilities;

/// <summary>
/// 系統相關的輔助方法
/// </summary>
public static partial class SystemHelper
{
    /// <summary>
    /// 程式啟動時的 Wine (Proton) 環境偵測結果；由 <see cref="DetectWine"/> 初始化。
    /// </summary>
    private static readonly bool _isOnWine = DetectWine();

    /// <summary>
    /// 程式啟動時的 Gamescope 合成器環境偵測結果；由 <see cref="DetectGamescope"/> 初始化。
    /// </summary>
    private static readonly bool _isOnGamescope = DetectGamescope();

    /// <summary>
    /// 偵測目前應用程式是否執行於 Wine (Proton) 環境下
    /// </summary>
    /// <remarks>
    /// 藉由檢查 ntdll.dll 是否匯出 wine_get_version 函數來判定是否位於 Wine 模擬環境。
    /// 這是偵測 Linux/Steam Deck (Proton) 環境下 Windows 程式執行狀態的最可靠方式。
    /// </remarks>
    /// <returns>若在 Wine 環境下執行則回傳 true，否則為 false。</returns>
    public static bool IsRunningOnWine()
    {
        return _isOnWine;
    }

    /// <summary>
    /// 偵測目前應用程式是否執行於 Steam Deck 的 Gamescope（遊戲模式）環境下
    /// </summary>
    /// <remarks>
    /// 透過檢查環境變數 GAMESCOPE_WAYLAND_DISPLAY 來判斷是否受 Gamescope 合成器控管。
    /// 在遊戲模式下，WinForms 的多視窗管理與還原邏輯常會導致渲染表面遺失，需進行特定保護。
    /// </remarks>
    /// <returns>若在 Gamescope 下執行則回傳 true，否則為 false。</returns>
    public static bool IsRunningOnGamescope()
    {
        return _isOnGamescope;
    }

    /// <summary>
    /// 判斷目前平台是否應限制高風險快捷鍵與自動返回行為。
    /// </summary>
    /// <remarks>
    /// Proton / Wine 與 Gamescope 環境下，A11y 廣播與視窗切換可靠度較低，
    /// 因此需停用依賴明確播報或前景切換的高風險操作。
    /// </remarks>
    /// <returns>若應限制高風險快捷鍵則回傳 true。</returns>
    public static bool ShouldRestrictHighRiskShortcuts()
    {
        return _isOnGamescope || _isOnWine;
    }

    /// <summary>
    /// 在程式啟動時執行一次 Wine 偵測，結果快取至 <see cref="_isOnWine"/>
    /// </summary>
    /// <returns>若偵測到 Wine (Proton) 環境則回傳 true，偵測失敗或非 Wine 環境則回傳 false。</returns>
    private static bool DetectWine()
    {
        try
        {
            // Wine 的 ntdll.dll 會導出 wine_get_version 函數；原生 Windows 不含此匯出。
            nint hModule = Kernel32.GetModuleHandle("ntdll.dll");

            if (hModule == 0)
            {
                return false;
            }

            return Kernel32.GetProcAddress(hModule, "wine_get_version") != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 在程式啟動時執行一次 Gamescope 合成器偵測，結果快取至 <see cref="_isOnGamescope"/>
    /// </summary>
    /// <returns>若偵測到 Gamescope 環境則回傳 true，環境變數不存在或讀取失敗則回傳 false。</returns>
    private static bool DetectGamescope()
    {
        try
        {
            // GAMESCOPE_WAYLAND_DISPLAY 為 Gamescope 合成器設定的專有 Wayland 顯示環境變數。
            return !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"));
        }
        catch
        {
            return false;
        }
    }

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
                    // 清理路徑（先 Trim('"') 去除前後的雙引號，再 Trim() 去除殘餘空白）並展開環境變數。
                    strPath = Environment.ExpandEnvironmentVariables(strPath.Trim('"').Trim());

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