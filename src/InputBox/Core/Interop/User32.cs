using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

/// <summary>
/// user32.dll 與 kernel32.dll P/Invoke 互操作介面
/// </summary>
public static partial class User32
{
    /// <summary>
    /// 取得目前前景視窗的控制代碼
    /// </summary>
    /// <returns>前景視窗控制代碼；若無前景視窗則為 0。</returns>
    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    /// <summary>
    /// 取得桌面視窗的控制代碼
    /// </summary>
    /// <returns>桌面視窗控制代碼。</returns>
    [LibraryImport("user32.dll")]
    internal static partial nint GetDesktopWindow();

    /// <summary>
    /// 取得指定視窗所屬的執行緒識別碼與程序識別碼
    /// </summary>
    /// <param name="handle">視窗控制代碼。</param>
    /// <param name="processId">接收程序識別碼的輸出變數。</param>
    /// <returns>建立此視窗的執行緒識別碼。</returns>
    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint handle, out uint processId);

    /// <summary>
    /// 判斷指定控制代碼是否為有效的視窗
    /// </summary>
    /// <param name="handle">待檢查的視窗控制代碼。</param>
    /// <returns>若控制代碼對應有效視窗則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint handle);

    /// <summary>
    /// 將指定視窗設為前景視窗並給予輸入焦點
    /// </summary>
    /// <param name="handle">要設為前景的視窗控制代碼。</param>
    /// <returns>若成功設為前景則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint handle);

    /// <summary>
    /// 將指定視窗移至 Z 順序頂端
    /// </summary>
    /// <param name="handle">要移至頂端的視窗控制代碼。</param>
    /// <returns>若成功則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BringWindowToTop(nint handle);

    /// <summary>
    /// 允許指定程序呼叫 SetForegroundWindow 切換前景
    /// </summary>
    /// <param name="processId">允許切換前景的程序識別碼；傳入 <see cref="AllowSetForegroundWindowAnyProcess"/> 則允許所有程序。</param>
    /// <returns>若成功則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// 附加或卸離兩個執行緒的輸入處理機制
    /// </summary>
    /// <param name="idAttach">要附加的執行緒識別碼。</param>
    /// <param name="idAttachTo">附加目標執行緒識別碼。</param>
    /// <param name="attach">true 表示附加，false 表示卸離。</param>
    /// <returns>若成功則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    /// <summary>
    /// 將鍵盤焦點設定到指定視窗
    /// </summary>
    /// <param name="handle">要接收焦點的視窗控制代碼。</param>
    /// <returns>先前擁有焦點的視窗控制代碼；若無則為 0。</returns>
    [LibraryImport("user32.dll")]
    internal static partial nint SetFocus(nint handle);

    /// <summary>
    /// 取得呼叫執行緒的執行緒識別碼
    /// </summary>
    /// <returns>目前執行緒識別碼。</returns>
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    /// <summary>
    /// 設定指定視窗的顯示狀態
    /// </summary>
    /// <param name="handle">目標視窗控制代碼。</param>
    /// <param name="command">顯示命令，指定視窗如何顯示。</param>
    /// <returns>若視窗先前為可見則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint handle, ShowWindowCommand command);

    /// <summary>
    /// 在系統中登錄全域快速鍵
    /// </summary>
    /// <param name="handle">接收 WM_HOTKEY 訊息的視窗控制代碼。</param>
    /// <param name="id">快速鍵識別碼。</param>
    /// <param name="modifiers">修飾鍵旗標（<see cref="KeyModifiers"/>）。</param>
    /// <param name="virtualKey">虛擬鍵碼。</param>
    /// <returns>若登錄成功則為 true；重複 id 或已被佔用則為 false。</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint handle, int id, uint modifiers, uint virtualKey);

    /// <summary>
    /// 取消登錄指定的全域快速鍵
    /// </summary>
    /// <param name="handle">登錄時使用的視窗控制代碼。</param>
    /// <param name="id">要取消的快速鍵識別碼。</param>
    /// <returns>若成功取消則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint handle, int id);

    /// <summary>
    /// 建立插入點游標並將其指派給指定視窗
    /// </summary>
    /// <param name="hWnd">擁有游標的視窗控制代碼。</param>
    /// <param name="hBitmap">游標點陣圖控制代碼；傳入 0 使用預設線條游標。</param>
    /// <param name="nWidth">游標寬度（像素）。</param>
    /// <param name="nHeight">游標高度（像素）。</param>
    /// <returns>若成功則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateCaret(nint hWnd, nint hBitmap, int nWidth, int nHeight);

    /// <summary>
    /// 顯示插入點游標
    /// </summary>
    /// <param name="hWnd">擁有游標的視窗控制代碼。</param>
    /// <returns>若成功則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowCaret(nint hWnd);

    /// <summary>
    /// 銷毀目前插入點游標並釋放其記憶體
    /// </summary>
    /// <returns>若成功則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyCaret();

    /// <summary>
    /// 閃爍指定視窗的標題列
    /// </summary>
    /// <param name="hWnd">要閃爍的視窗控制代碼。</param>
    /// <param name="bInvert">true 表示反轉目前閃爍狀態；false 表示回到初始狀態。</param>
    /// <returns>若視窗在呼叫前為作用中狀態則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlashWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bInvert);

    /// <summary>
    /// 以延伸參數閃爍視窗（支援次數與計時器控制）
    /// </summary>
    /// <param name="flashInfo">閃爍參數結構（<see cref="FlashWindowInfo"/>）。</param>
    /// <returns>若視窗在呼叫前為作用中狀態則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlashWindowEx(in FlashWindowInfo flashInfo);

    /// <summary>
    /// 搜尋符合類別名稱或視窗標題的頂層視窗
    /// </summary>
    /// <param name="lpClassName">視窗類別名稱；傳入 null 表示不限制類別。</param>
    /// <param name="lpWindowName">視窗標題；傳入 null 表示不限制標題。</param>
    /// <returns>符合條件的視窗控制代碼；若未找到則為 0。</returns>
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string? lpClassName, string? lpWindowName);

    /// <summary>
    /// 判斷指定視窗是否可見
    /// </summary>
    /// <param name="hWnd">視窗控制代碼。</param>
    /// <returns>若視窗可見則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    /// <summary>
    /// 判斷指定視窗是否已最小化（圖示化）
    /// </summary>
    /// <param name="hWnd">視窗控制代碼。</param>
    /// <returns>若視窗已最小化則為 true，否則為 false。</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    /// <summary>
    /// 傳送訊息至指定視窗並等待處理完成
    /// </summary>
    /// <param name="hWnd">目標視窗控制代碼。</param>
    /// <param name="msg">訊息識別碼。</param>
    /// <param name="wParam">訊息的 wParam 參數。</param>
    /// <param name="lParam">訊息的 lParam 參數。</param>
    /// <returns>訊息處理的回傳值，依訊息類型而定。</returns>
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