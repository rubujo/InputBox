namespace InputBox.Core.Input;

/// <summary>
/// 控制器事件綁定器：集中維護 MainForm 與 IGamepadController 的事件對映
/// </summary>
internal sealed class GamepadEventBinder
{
    /// <summary>
    /// 事件對映資料
    /// </summary>
    internal sealed record BindingMap(
        Action<bool> OnConnectionChanged,
        Action OnBackPressed,
        Action OnBackReleased,
        Action OnUpPressed,
        Action OnDownPressed,
        Action OnUpRepeat,
        Action OnDownRepeat,
        Action OnLeftPressed,
        Action OnLeftRepeat,
        Action OnRightPressed,
        Action OnRightRepeat,
        Action OnStartPressed,
        Action OnAPressed,
        Action OnBPressed,
        Action OnYPressed,
        Action OnRSLeftPressed,
        Action OnRSLeftRepeat,
        Action OnRSRightPressed,
        Action OnRSRightRepeat,
        Action OnXPressed);

    /// <summary>
    /// 套用事件綁定
    /// </summary>
    /// <param name="controller">目標控制器執行個體。</param>
    /// <param name="map">事件對映資料。</param>
    public static void Bind(IGamepadController controller, BindingMap map)
    {
        controller.ConnectionChanged += map.OnConnectionChanged;

        controller.BackPressed += map.OnBackPressed;
        controller.BackReleased += map.OnBackReleased;

        controller.UpPressed += map.OnUpPressed;
        controller.DownPressed += map.OnDownPressed;
        controller.UpRepeat += map.OnUpRepeat;
        controller.DownRepeat += map.OnDownRepeat;

        controller.LeftPressed += map.OnLeftPressed;
        controller.LeftRepeat += map.OnLeftRepeat;
        controller.RightPressed += map.OnRightPressed;
        controller.RightRepeat += map.OnRightRepeat;

        controller.StartPressed += map.OnStartPressed;
        controller.APressed += map.OnAPressed;
        controller.BPressed += map.OnBPressed;
        controller.YPressed += map.OnYPressed;

        controller.RSLeftPressed += map.OnRSLeftPressed;
        controller.RSLeftRepeat += map.OnRSLeftRepeat;
        controller.RSRightPressed += map.OnRSRightPressed;
        controller.RSRightRepeat += map.OnRSRightRepeat;

        controller.XPressed += map.OnXPressed;
    }
}