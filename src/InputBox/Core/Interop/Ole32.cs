using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace InputBox.Core.Interop;

/// <summary>
/// OLE32.dll 互操作介面
/// </summary>
internal static partial class Ole32
{
    /// <summary>
    /// 建立一個指定的類別實體
    /// </summary>
    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        in Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out nint ppv);

    /// <summary>
    /// 觸控鍵盤切換介面（ITipInvocation）
    /// </summary>
    /// <remarks>
    /// CLSID: 4ce576fa-83dc-4f88-951c-9d0782b4e376
    /// </remarks>
    [GeneratedComInterface, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface ITipInvocation
    {
        /// <summary>
        /// 切換觸控鍵盤的顯示狀態
        /// </summary>
        /// <param name="hwnd">視窗句柄</param>
        void Toggle(nint hwnd);
    }

    /// <summary>
    /// COM 類別執行上下文旗標
    /// </summary>
    internal static class ClsCtx
    {
        /// <summary>
        /// Inproc 伺服器
        /// </summary>
        public const uint InprocServer = 0x1;

        /// <summary>
        /// Inproc 處理器
        /// </summary>
        public const uint InprocHandler = 0x2;

        /// <summary>
        /// 本地伺服器
        /// </summary>
        public const uint LocalServer = 0x4;

        /// <summary>
        /// 遠端伺服器
        /// </summary>
        public const uint RemoteServer = 0x10;

        /// <summary>
        /// 全部
        /// </summary>
        public const uint All = InprocServer |
            InprocHandler |
            LocalServer |
            RemoteServer;
    }
}