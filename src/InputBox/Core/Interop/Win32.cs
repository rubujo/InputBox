using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

internal static partial class Win32
{
    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    #region XInput

    [LibraryImport("xinput1_4.dll")]
    public static partial int XInputGetState(int dwUserIndex, out XInputState xInputState);

    [LibraryImport("xinput1_4.dll")]
    public static partial int XInputSetState(int dwUserIndex, ref XInputVibration pVibration);

    /// <summary>
    /// XInputState
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        /// <summary>
        /// dwPacketNumber
        /// </summary>
        public uint dwPacketNumber;

        /// <summary>
        /// gamepad
        /// </summary>
        public XInputGamepad Gamepad;

        /// <summary>
        /// 有
        /// </summary>
        /// <param name="gamepadButton">GamepadButton</param>
        /// <returns>布林值</returns>
        public readonly bool Has(GamepadButton gamepadButton) => (Gamepad.wButtons & (ushort)gamepadButton) != 0;
    }

    /// <summary>
    /// XInputGamepad
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        /// <summary>
        /// wButtons
        /// </summary>
        public ushort wButtons;

        /// <summary>
        /// bLeftTrigger
        /// </summary>
        public byte bLeftTrigger;

        /// <summary>
        /// bRightTrigger
        /// </summary>
        public byte bRightTrigger;

        /// <summary>
        /// sThumbLX
        /// </summary>
        public short sThumbLX;

        /// <summary>
        /// sThumbLY
        /// </summary>
        public short sThumbLY;

        /// <summary>
        /// sThumbRX
        /// </summary>
        public short sThumbRX;

        /// <summary>
        /// sThumbRY
        /// </summary>
        public short sThumbRY;
    }

    /// <summary>
    /// XInputVibration
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        /// <summary>
        /// wLeftMotorSpeed
        /// </summary>
        public ushort wLeftMotorSpeed;

        /// <summary>
        /// wRightMotorSpeed
        /// </summary>
        public ushort wRightMotorSpeed;
    }

    /// <summary>
    /// 列舉：控制器按鈕
    /// </summary>
    [Flags]
    public enum GamepadButton : ushort
    {
        XINPUT_GAMEPAD_DPAD_UP = 0x0001,
        XINPUT_GAMEPAD_DPAD_DOWN = 0x0002,
        XINPUT_GAMEPAD_DPAD_LEFT = 0x0004,
        XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008,
        XINPUT_GAMEPAD_START = 0x0010,
        XINPUT_GAMEPAD_BACK = 0x0020,
        XINPUT_GAMEPAD_LEFT_THUMB = 0x0040,
        XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080,
        XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100,
        XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200,
        XINPUT_GAMEPAD_A = 0x1000,
        XINPUT_GAMEPAD_B = 0x2000,
        XINPUT_GAMEPAD_X = 0x4000,
        XINPUT_GAMEPAD_Y = 0x8000
    }

    #endregion

    /// <summary>
    /// 視窗還原命令（ShowWindow API）
    /// </summary>
    /// <remarks>
    /// 對應 Win32 API 的 <c>SW_RESTORE</c> 常數，
    /// 用於將最小化或最大化的視窗還原為正常狀態。
    /// </remarks>
    public const int SW_RESTORE = 9;

    /// <summary>
    /// 視窗快速鍵訊息（WM_HOTKEY）
    /// </summary>
    /// <remarks>
    /// 當使用者觸發已註冊的全域快速鍵時，
    /// 系統會透過此訊息通知視窗程序。
    /// </remarks>
    public const int WM_HOTKEY = 0x0312;

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