using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

/// <summary>
/// XInput
/// </summary>
internal static partial class XInput
{
    [LibraryImport("xinput1_4.dll")]
    public static partial uint XInputGetState(uint userIndex, out XInputState state);

    [LibraryImport("xinput1_4.dll")]
    public static partial uint XInputSetState(uint userIndex, in XInputVibration vibration);

    /// <summary>
    /// XInput 狀態快照
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        /// <summary>
        /// 封包序號
        /// </summary>
        public uint PacketNumber;

        /// <summary>
        /// 手把按鍵與類比狀態
        /// </summary>
        public XInputGamepad Gamepad;

        /// <summary>
        /// 檢查按鈕是否被按下
        /// </summary>
        /// <param name="button">GamepadButton</param>
        /// <returns>布林值</returns>
        public readonly bool Has(GamepadButton button) => (Gamepad.Buttons & (ushort)button) != 0;
    }

    /// <summary>
    /// XInput 手把資料
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        /// <summary>
        /// 按鈕遮罩
        /// </summary>
        public ushort Buttons;

        /// <summary>
        /// 左扳機鍵（0～255）
        /// </summary>
        public byte LeftTrigger;

        /// <summary>
        /// 右扳機鍵（0～255）
        /// </summary>
        public byte RightTrigger;

        /// <summary>
        /// 左類比搖桿 X 軸（-32768～32767）
        /// </summary>
        public short ThumbLeftX;

        /// <summary>
        /// 左類比搖桿 Y 軸（-32768～32767）
        /// </summary>
        public short ThumbLeftY;

        /// <summary>
        /// 右類比搖桿 X 軸（-32768～32767）
        /// </summary>
        public short ThumbRightX;

        /// <summary>
        /// 右類比搖桿 Y 軸（-32768～32767）
        /// </summary>
        public short ThumbRightY;
    }

    /// <summary>
    /// XInput 震動參數
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        /// <summary>
        /// 左側低頻馬達速度（0-65535）
        /// </summary>
        public ushort LeftMotorSpeed;

        /// <summary>
        /// 右側高頻馬達速度（0-65535）
        /// </summary>
        public ushort RightMotorSpeed;
    }

    /// <summary>
    /// XInput 控制器按鈕定義
    /// </summary>
    [Flags]
    public enum GamepadButton : ushort
    {
        DpadUp = 0x0001,
        DpadDown = 0x0002,
        DpadLeft = 0x0004,
        DpadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }
}
