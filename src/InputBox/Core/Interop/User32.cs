using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

/// <summary>
/// User32
/// </summary>
public static partial class User32
{
    /// <summary>
    /// 取得目前前景視窗的控制代碼
    /// </summary>
    public static nint ForegroundWindow => GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint handle, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint handle, ShowWindowCommand command);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint handle, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint handle, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateCaret(nint hWnd, nint hBitmap, int nWidth, int nHeight);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowCaret(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlashWindowEx(in FlashWindowInfo flashInfo);

    /// <summary>
    /// FlashWindowEx 結構
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FlashWindowInfo
    {
        public uint Size;
        public nint Hwnd;
        public FlashWindowFlags Flags;
        public uint Count;
        public uint Timeout;
    }

    /// <summary>
    /// FlashWindowEx 旗標
    /// </summary>
    [Flags]
    public enum FlashWindowFlags : uint
    {
        /// <summary>
        /// 閃爍標題列與工作列
        /// </summary>
        All = 3,
        /// <summary>
        /// 閃爍直到視窗回到前景
        /// </summary>
        TimerNoForeground = 12
    }

    /// <summary>
    /// 視窗命令列舉（ShowWindow）
    /// </summary>
    public enum ShowWindowCommand : int
    {
        /// <summary>
        /// 還原視窗
        /// </summary>
        Restore = 9
    }

    /// <summary>
    /// 視窗訊息列舉（Window Messages）
    /// </summary>
    public enum WindowMessage : int
    {
        /// <summary>
        /// 全域快速鍵訊息
        /// </summary>
        HotKey = 0x0312
    }

    /// <summary>
    /// 鍵盤修飾鍵旗標（RegisterHotKey API）
    /// </summary>
    /// <remarks>
    /// 可組合使用，用於指定全域快速鍵所需的修飾鍵。
    /// </remarks>
    [Flags]
    public enum KeyModifiers : uint
    {
        /// <summary>
        /// 無修飾鍵
        /// </summary>
        None = 0,
        /// <summary>
        /// Alt 鍵
        /// </summary>
        Alt = 0x0001,
        /// <summary>
        /// Ctrl 鍵
        /// </summary>
        Control = 0x0002,
        /// <summary>
        /// Shift 鍵
        /// </summary>
        Shift = 0x0004,
        /// <summary>
        /// Windows 鍵
        /// </summary>
        Win = 0x0008,
        /// <summary>
        /// 禁止重複觸發（僅在按鍵首次按下時觸發）
        /// </summary>
        NoRepeat = 0x4000
    }
}