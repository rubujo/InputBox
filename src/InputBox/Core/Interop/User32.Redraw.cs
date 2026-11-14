using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

public static partial class User32
{
    /// <summary>要求指定自家控制項的客戶區與非客戶區立即重繪。</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RedrawWindow(nint handle, nint updateRectangle, nint updateRegion, RedrawFlags flags);

    /// <summary>重繪控制項框架所需的 Win32 旗標，不變更大小或重建 HWND。</summary>
    [Flags]
    internal enum RedrawFlags : uint
    {
        Invalidate = 0x0001,
        UpdateNow = 0x0100,
        Frame = 0x0400
    }
}