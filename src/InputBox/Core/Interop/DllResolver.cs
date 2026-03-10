using System.Reflection;
using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

/// <summary>
/// DllResolver
/// </summary>
internal class DllResolver
{
    /// <summary>
    /// 鎖定物件，用於確保多執行緒環境下對 XInput DLL 的載入過程是線程安全的，避免競爭條件和重複載入問題
    /// </summary>
    private static readonly Lock ResolverLock = new();

    /// <summary>
    /// 快取的 XInput DLL handle，用於避免重複載入
    /// </summary>
    private static volatile nint _cachedHandle = IntPtr.Zero;

    /// <summary>
    /// 自訂的 XInput 載入解析器，用於覆寫 DllImport 的載入邏輯，僅在要求載入 "xinput1_4.dll" 時介入，否則直接返回 IntPtr.Zero
    /// </summary>
    /// <param name="libraryName">開啟端要求載入的 DLL 名稱</param>
    /// <param name="_">觸發載入的組件（未使用）</param>
    /// <param name="__">DllImportSearchPath 設定（未使用）</param>
    /// <returns>成功載入時回傳 DLL handle；若無法載入或名稱不符則回傳 IntPtr.Zero。</returns>
    public static nint ResolveXInput(
        string libraryName,
        Assembly _,
        DllImportSearchPath? __)
    {
        // 僅攔截 xinput1_4.dll，其餘 DLL 交由系統處理。
        if (!libraryName.Equals("xinput1_4.dll", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        // 使用 Lock 確保多執行緒下只會執行一次搜尋與載入。
        lock (ResolverLock)
        {
            if (_cachedHandle != IntPtr.Zero)
            {
                return _cachedHandle;
            }

            // 依序嘗試載入不同版本的 XInput。
            string[] arrayCandidate =
            [
                // Windows 8／10／11。
                "xinput1_4.dll",
                // DirectX End-User Runtime。
                "xinput1_3.dll",
                // Vista／Win7 最低相容版本。
                "xinput9_1_0.dll"
            ];

            // 取得系統目錄路徑（通常是 C:\Windows\System32），
            // 防禦 DLL 劫持，只信任系統內建的檔案。
            string strSystemFolder = Environment.SystemDirectory;

            foreach (string strDllName in arrayCandidate)
            {
                // 組合絕對路徑。
                string strFullPath = Path.Combine(strSystemFolder, strDllName);

                // 使用絕對路徑嘗試載入。
                if (NativeLibrary.TryLoad(strFullPath, out nint handle))
                {
                    _cachedHandle = handle;

                    return handle;
                }
            }

            return IntPtr.Zero;
        }
    }
}