using System.Runtime.InteropServices;

namespace InputBox.Core.Interop;

/// <summary>
/// XInput
/// </summary>
internal static partial class XInput
{
    /// <summary>
    /// 取得指定控制器的目前輸入狀態
    /// </summary>
    /// <param name="userIndex">控制器索引（0～3）。</param>
    /// <param name="state">接收狀態快照的輸出結構。</param>
    /// <returns>成功時為 0（ERROR_SUCCESS）；控制器未連接時為 1167（ERROR_DEVICE_NOT_CONNECTED）。</returns>
    [LibraryImport("xinput1_4.dll")]
    public static partial uint XInputGetState(uint userIndex, out XInputState state);

    /// <summary>
    /// 設定指定控制器的震動馬達速度
    /// </summary>
    /// <param name="userIndex">控制器索引（0～3）。</param>
    /// <param name="vibration">包含左右馬達速度的震動參數。</param>
    /// <returns>成功時為 0（ERROR_SUCCESS）；控制器未連接時為 1167（ERROR_DEVICE_NOT_CONNECTED）。</returns>
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
        /// 控制器按鍵與類比狀態
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
    /// XInput 控制器資料
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
        /// <summary>
        /// 十字鍵上
        /// </summary>
        DpadUp = 0x0001,
        /// <summary>
        /// 十字鍵下
        /// </summary>
        DpadDown = 0x0002,
        /// <summary>
        /// 十字鍵左
        /// </summary>
        DpadLeft = 0x0004,
        /// <summary>
        /// 十字鍵右
        /// </summary>
        DpadRight = 0x0008,
        /// <summary>
        /// Start 鍵
        /// </summary>
        Start = 0x0010,
        /// <summary>
        /// Back 鍵
        /// </summary>
        Back = 0x0020,
        /// <summary>
        /// 左搖桿按下
        /// </summary>
        LeftThumb = 0x0040,
        /// <summary>
        /// 右搖桿按下
        /// </summary>
        RightThumb = 0x0080,
        /// <summary>
        /// 左肩鍵（LB）
        /// </summary>
        LeftShoulder = 0x0100,
        /// <summary>
        /// 右肩鍵（RB）
        /// </summary>
        RightShoulder = 0x0200,
        /// <summary>
        /// A 鍵（底部面板鍵）
        /// </summary>
        A = 0x1000,
        /// <summary>
        /// B 鍵（右側面板鍵）
        /// </summary>
        B = 0x2000,
        /// <summary>
        /// X 鍵（左側面板鍵）
        /// </summary>
        X = 0x4000,
        /// <summary>
        /// Y 鍵（頂部面板鍵）
        /// </summary>
        Y = 0x8000
    }
}