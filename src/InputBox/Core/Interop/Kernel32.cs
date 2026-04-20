using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

/// <summary>
/// Kernel32.dll 互操作介面
/// </summary>
internal static partial class Kernel32
{
    /// <summary>
    /// 取得指定模組的控制代碼
    /// </summary>
    /// <param name="lpModuleName">模組名稱（例如 "ntdll.dll"）。</param>
    /// <returns>若成功則回傳模組控制代碼，否則為 0。</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string lpModuleName);

    /// <summary>
    /// 取得匯出函數的位址
    /// </summary>
    /// <param name="hModule">模組控制代碼。</param>
    /// <param name="procName">函數名稱。</param>
    /// <returns>若成功則回傳函數位址，否則為 0。</returns>
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(nint hModule, string procName);
}