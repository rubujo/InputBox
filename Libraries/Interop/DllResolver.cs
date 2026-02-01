using System.Reflection;
using System.Runtime.InteropServices;

namespace InputBox.Libraries.Interop;

internal class DllResolver
{
#pragma warning disable IDE0060 // 移除未使用的參數

    /// <summary>
    /// 自訂的 XInput 載入解析器，用於覆寫 DllImport 的載入邏輯，僅在要求載入 "xinput1_4.dll" 時介入，否則直接返回 IntPtr.Zero
    /// </summary>
    /// <param name="libraryName">開啟端要求載入的 DLL 名稱</param>
    /// <param name="assembly">觸發載入的組件（未使用）</param>
    /// <param name="searchPath">DllImportSearchPath 設定（未使用）</param>
    /// <returns>成功載入時回傳 DLL handle；若無法載入或名稱不符則回傳 IntPtr.Zero。</returns>
    public static nint ResolveXInput(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        // 僅攔截 xinput1_4.dll，其餘 DLL 交由系統處理。
        if (!libraryName.Equals("xinput1_4.dll", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
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
                return handle;
            }
        }

        return IntPtr.Zero;
    }

#pragma warning restore IDE0060
}