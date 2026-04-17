namespace InputBox.Core.Input;

/// <summary>
/// 控制器事件綁定器：集中維護 MainForm 與 IGamepadController 的事件對映
/// </summary>
internal sealed class GamepadEventBinder
{
    /// <summary>
    /// 事件對映資料。
    /// </summary>
    /// <param name="OnConnectionChanged">控制器連線狀態變更時執行的處理常式。</param>
    /// <param name="OnBackPressed">控制器返回鍵按下時執行的處理常式。</param>
    /// <param name="OnBackReleased">控制器返回鍵放開時執行的處理常式。</param>
    /// <param name="OnUpPressed">控制器上鍵按下時執行的處理常式。</param>
    /// <param name="OnDownPressed">控制器下鍵按下時執行的處理常式。</param>
    /// <param name="OnUpRepeat">控制器上鍵連發時執行的處理常式。</param>
    /// <param name="OnDownRepeat">控制器下鍵連發時執行的處理常式。</param>
    /// <param name="OnLeftPressed">控制器左鍵按下時執行的處理常式。</param>
    /// <param name="OnLeftRepeat">控制器左鍵連發時執行的處理常式。</param>
    /// <param name="OnRightPressed">控制器右鍵按下時執行的處理常式。</param>
    /// <param name="OnRightRepeat">控制器右鍵連發時執行的處理常式。</param>
    /// <param name="OnLeftShoulderPressed">左肩鍵按下時執行的處理常式。</param>
    /// <param name="OnLeftShoulderReleased">左肩鍵放開時執行的處理常式。</param>
    /// <param name="OnLeftShoulderRepeat">左肩鍵連發時執行的處理常式。</param>
    /// <param name="OnRightShoulderPressed">右肩鍵按下時執行的處理常式。</param>
    /// <param name="OnRightShoulderReleased">右肩鍵放開時執行的處理常式。</param>
    /// <param name="OnRightShoulderRepeat">右肩鍵連發時執行的處理常式。</param>
    /// <param name="OnLeftTriggerPressed">左觸發鍵按下時執行的處理常式。</param>
    /// <param name="OnLeftTriggerRepeat">左觸發鍵連發時執行的處理常式。</param>
    /// <param name="OnRightTriggerPressed">右觸發鍵按下時執行的處理常式。</param>
    /// <param name="OnRightTriggerRepeat">右觸發鍵連發時執行的處理常式。</param>
    /// <param name="OnStartPressed">開始鍵按下時執行的處理常式。</param>
    /// <param name="OnAPressed">A 鍵按下時執行的處理常式。</param>
    /// <param name="OnBPressed">B 鍵按下時執行的處理常式。</param>
    /// <param name="OnYPressed">Y 鍵按下時執行的處理常式。</param>
    /// <param name="OnRSLeftPressed">右搖桿左推按下時執行的處理常式。</param>
    /// <param name="OnRSLeftRepeat">右搖桿左推連發時執行的處理常式。</param>
    /// <param name="OnRSRightPressed">右搖桿右推按下時執行的處理常式。</param>
    /// <param name="OnRSRightRepeat">右搖桿右推連發時執行的處理常式。</param>
    /// <param name="OnXPressed">X 鍵按下時執行的處理常式。</param>
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
        Action OnLeftShoulderPressed,
        Action OnLeftShoulderReleased,
        Action OnLeftShoulderRepeat,
        Action OnRightShoulderPressed,
        Action OnRightShoulderReleased,
        Action OnRightShoulderRepeat,
        Action OnLeftTriggerPressed,
        Action OnLeftTriggerRepeat,
        Action OnRightTriggerPressed,
        Action OnRightTriggerRepeat,
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

        controller.LeftShoulderPressed += map.OnLeftShoulderPressed;
        controller.LeftShoulderReleased += map.OnLeftShoulderReleased;
        controller.LeftShoulderRepeat += map.OnLeftShoulderRepeat;
        controller.RightShoulderPressed += map.OnRightShoulderPressed;
        controller.RightShoulderReleased += map.OnRightShoulderReleased;
        controller.RightShoulderRepeat += map.OnRightShoulderRepeat;
        controller.LeftTriggerPressed += map.OnLeftTriggerPressed;
        controller.LeftTriggerRepeat += map.OnLeftTriggerRepeat;
        controller.RightTriggerPressed += map.OnRightTriggerPressed;
        controller.RightTriggerRepeat += map.OnRightTriggerRepeat;

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