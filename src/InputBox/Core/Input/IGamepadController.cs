namespace InputBox.Core.Input;

/// <summary>
/// 遊戲手把控制器介面
/// </summary>
internal interface IGamepadController : IDisposable, IAsyncDisposable
{
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
    /// 控制器開始鍵按下事件
    /// </summary>
    event Action? StartPressed;

    /// <summary>
    /// 控制器返回鍵按下事件
    /// </summary>
    event Action? BackPressed;

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
    /// 當左觸發鍵（LT 鍵）被按下時觸發
    /// </summary>
    event Action? LeftTriggerPressed;

    /// <summary>
    /// 當右觸發鍵（RT 鍵）被按下時觸發
    /// </summary>
    event Action? RightTriggerPressed;

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
    /// 讓控制器震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">持續時間（毫秒）</param>
    /// <returns>Task。</returns>
    Task VibrateAsync(ushort strength, int milliseconds = 60);

    /// <summary>
    /// 暫停輪詢
    /// </summary>
    void Pause();

    /// <summary>
    /// 恢復輪詢
    /// </summary>
    void Resume();
}