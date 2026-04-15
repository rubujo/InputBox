using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

/// <summary>
/// User32
/// </summary>
public static partial class User32
{
    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial nint GetDesktopWindow();

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
    internal static partial bool BringWindowToTop(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    internal static partial nint SetFocus(nint handle);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

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
    internal static partial bool DestroyCaret();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlashWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bInvert);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlashWindowEx(in FlashWindowInfo flashInfo);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

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
    public enum WindowMessage : uint
    {
        /// <summary>
        /// 視窗啟用狀態變更訊息
        /// </summary>
        Activate = 0x0006,
        /// <summary>
        /// 全域快速鍵訊息
        /// </summary>
        HotKey = 0x0312,
        /// <summary>
        /// 設定編輯控制項的選取範圍
        /// </summary>
        EM_SETSEL = 0x00B1
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

    /// <summary>
    /// 允許任意前景程序在目前互動鏈內呼叫 SetForegroundWindow。
    /// </summary>
    public const int AllowSetForegroundWindowAnyProcess = -1;

    /// <summary>
    /// 取得目前前景視窗的控制代碼
    /// </summary>
    public static nint ForegroundWindow => GetForegroundWindow();

    /// <summary>
    /// FlashWindowEx 結構
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FlashWindowInfo
    {
        /// <summary>
        /// 結構大小（以位元組為單位）
        /// </summary>
        public uint Size;

        /// <summary>
        /// 視窗句柄
        /// </summary>
        public nint Hwnd;

        /// <summary>
        /// 閃爍旗標
        /// </summary>
        public FlashWindowFlags Flags;

        /// <summary>
        /// 閃爍次數
        /// </summary>
        public uint Count;

        /// <summary>
        /// 超時時間（以毫秒為單位）
        /// </summary>
        public uint Timeout;
    }
}