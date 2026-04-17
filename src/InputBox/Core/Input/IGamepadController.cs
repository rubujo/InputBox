using InputBox.Core.Feedback;

namespace InputBox.Core.Input;

/// <summary>
/// Gamepad 控制介面
/// </summary>
internal interface IGamepadController : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 取得目前使用的裝置名稱
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// 取得目前裝置的偵測識別資訊，優先包含可跨藍牙、接收器與 USB 保持穩定的廠商/產品線索。
    /// </summary>
    string DeviceIdentity { get; }

    /// <summary>
    /// 當控制器連線狀態改變時觸發（true: 已連線, false: 已斷開）
    /// </summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>
    /// 取得目前是否已連線
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 控制器上鍵按下事件
    /// </summary>
    event Action? UpPressed;

    /// <summary>
    /// 控制器下鍵按下事件
    /// </summary>
    event Action? DownPressed;

    /// <summary>
    /// 控制器左鍵按下事件
    /// </summary>
    event Action? LeftPressed;

    /// <summary>
    /// 控制器右鍵按下事件
    /// </summary>
    event Action? RightPressed;

    /// <summary>
    /// 當左肩鍵（LB 鍵）被按下時觸發
    /// </summary>
    event Action? LeftShoulderPressed;

    /// <summary>
    /// 當左肩鍵（LB 鍵）被放開時觸發
    /// </summary>
    event Action? LeftShoulderReleased;

    /// <summary>
    /// 左肩鍵（LB）持續按住時的連發事件
    /// </summary>
    event Action? LeftShoulderRepeat;

    /// <summary>
    /// 當右肩鍵（RB 鍵）被按下時觸發
    /// </summary>
    event Action? RightShoulderPressed;

    /// <summary>
    /// 當右肩鍵（RB 鍵）被放開時觸發
    /// </summary>
    event Action? RightShoulderReleased;

    /// <summary>
    /// 右肩鍵（RB）持續按住時的連發事件
    /// </summary>
    event Action? RightShoulderRepeat;

    /// <summary>
    /// 控制器開始鍵按下事件
    /// </summary>
    event Action? StartPressed;

    /// <summary>
    /// 控制器返回鍵按下事件
    /// </summary>
    event Action? BackPressed;

    /// <summary>
    /// 控制器返回鍵放開事件
    /// </summary>
    event Action? BackReleased;

    /// <summary>
    /// 控制器 A 鍵按下事件
    /// </summary>
    event Action? APressed;

    /// <summary>
    /// 控制器 B 鍵按下事件
    /// </summary>
    event Action? BPressed;

    /// <summary>
    /// 控制器 X 鍵按下事件
    /// </summary>
    event Action? XPressed;

    /// <summary>
    /// 控制器 Y 鍵按下事件
    /// </summary>
    event Action? YPressed;

    /// <summary>
    /// 控制器上鍵重複事件
    /// </summary>
    event Action? UpRepeat;

    /// <summary>
    /// 控制器下鍵重複事件
    /// </summary>
    event Action? DownRepeat;

    /// <summary>
    /// 控制器左鍵重複事件
    /// </summary>
    event Action? LeftRepeat;

    /// <summary>
    /// 控制器右鍵重複事件
    /// </summary>
    event Action? RightRepeat;

    /// <summary>
    /// 右搖桿左推按下事件
    /// </summary>
    event Action? RSLeftPressed;

    /// <summary>
    /// 右搖桿右推按下事件
    /// </summary>
    event Action? RSRightPressed;

    /// <summary>
    /// 右搖桿左推重複事件
    /// </summary>
    event Action? RSLeftRepeat;

    /// <summary>
    /// 右搖桿右推重複事件
    /// </summary>
    event Action? RSRightRepeat;

    /// <summary>
    /// 當左觸發鍵（LT 鍵）被按下時觸發
    /// </summary>
    event Action? LeftTriggerPressed;

    /// <summary>
    /// 當右觸發鍵（RT 鍵）被按下時觸發
    /// </summary>
    event Action? RightTriggerPressed;

    /// <summary>
    /// LT 持續按住時的連發事件
    /// </summary>
    event Action? LeftTriggerRepeat;

    /// <summary>
    /// RT 持續按住時的連發事件
    /// </summary>
    event Action? RightTriggerRepeat;

    /// <summary>
    /// 控制器 LB 鍵是否按住
    /// </summary>
    bool IsLeftShoulderHeld { get; }

    /// <summary>
    /// 控制器 RB 鍵是否按住
    /// </summary>
    bool IsRightShoulderHeld { get; }

    /// <summary>
    /// 控制器左觸發鍵（LT 鍵）是否按住
    /// </summary>
    bool IsLeftTriggerHeld { get; }

    /// <summary>
    /// 控制器右觸發鍵（RT 鍵）是否按住
    /// </summary>
    bool IsRightTriggerHeld { get; }

    /// <summary>
    /// 控制器 Back 鍵是否按住
    /// </summary>
    bool IsBackHeld { get; }

    /// <summary>
    /// 控制器 B 鍵是否按住
    /// </summary>
    bool IsBHeld { get; }

    /// <summary>
    /// 控制器 X 鍵是否按住
    /// </summary>
    bool IsXHeld { get; }

    /// <summary>
    /// 目前控制器支援的震動馬達能力。
    /// </summary>
    VibrationMotorSupport VibrationMotorSupport { get; }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">持續時間（毫秒），預設值為 60 毫秒</param>
    /// <param name="priority">震動優先級</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task。</returns>
    Task VibrateAsync(
        ushort strength,
        int milliseconds = 60,
        VibrationPriority priority = VibrationPriority.Normal,
        CancellationToken ct = default);

    /// <summary>
    /// 依指定的多馬達震動設定讓控制器震動。
    /// </summary>
    /// <param name="profile">包含基礎強度、持續時間與各馬達比例的震動設定。</param>
    /// <param name="priority">震動優先級。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>Task。</returns>
    Task VibrateAsync(
        VibrationProfile profile,
        VibrationPriority priority = VibrationPriority.Normal,
        CancellationToken ct = default);

    /// <summary>
    /// 同步強制停止震動（用於應用程式關閉等緊急情境）
    /// </summary>
    void StopVibration();

    /// <summary>
    /// 暫停輪詢
    /// </summary>
    void Pause();

    /// <summary>
    /// 恢復輪詢
    /// </summary>
    void Resume();

    /// <summary>
    /// 重設目前遊戲控制器的執行期校正狀態。
    /// <para>只清除暫態學習值與殘留輸入，不會變更已儲存的死區或連發設定。</para>
    /// </summary>
    void ResetCalibration();

    /// <summary>
    /// 取得或設定連發設定
    /// </summary>
    GamepadRepeatSettings RepeatSettings { get; set; }

    /// <summary>
    /// 取得或設定搖桿進入死區閾值
    /// </summary>
    int ThumbDeadzoneEnter { get; set; }

    /// <summary>
    /// 取得或設定搖桿離開死區閾值
    /// </summary>
    int ThumbDeadzoneExit { get; set; }
}