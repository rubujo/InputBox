using System.Runtime.InteropServices;

namespace InputBox.Core.Input;

/// <summary>
/// GameInput native shim 的受控進入點，負責把窄版 C ABI 轉成專案內部型別。
/// </summary>
internal sealed class GameInput : IDisposable
{
    private readonly SafeGameInputContextHandle _handle;
    private GameInputDevice[] _devices = [];

    private GameInput(SafeGameInputContextHandle handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// 建立 GameInput 內容；若 shim 或 runtime 不可用會丟出例外，交由 XInput 退避流程處理。
    /// </summary>
    /// <returns>GameInput 受控包裝。</returns>
    public static GameInput Create()
    {
        int hr = GameInputNativeMethods.Create(out SafeGameInputContextHandle handle);

        if (hr < 0 ||
            handle.IsInvalid)
        {
            handle.Dispose();
            Marshal.ThrowExceptionForHR(hr);
        }

        return new GameInput(handle);
    }

    /// <summary>
    /// 設定 GameInput focus policy。
    /// </summary>
    /// <param name="policy">Focus policy。</param>
    public void SetFocusPolicy(GameInputFocusPolicy policy)
    {
        _ = GameInputNativeMethods.SetFocusPolicy(_handle, (uint)policy);
    }

    /// <summary>
    /// 重新列舉目前可用的 GameInput gamepad 裝置。
    /// </summary>
    /// <param name="kind">輸入類型；目前只支援 Gamepad。</param>
    /// <returns>目前裝置清單。</returns>
    public IReadOnlyList<GameInputDevice> EnumerateDevices(GameInputKind kind)
    {
        if (kind != GameInputKind.Gamepad)
        {
            return [];
        }

        int hr = GameInputNativeMethods.RefreshDevices(_handle);

        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        int count = GameInputNativeMethods.GetDeviceCount(_handle);
        GameInputDevice[] devices = new GameInputDevice[count];

        for (int i = 0; i < count; i++)
        {
            hr = GameInputNativeMethods.GetDeviceInfo(_handle, i, out GameInputNativeDeviceInfo nativeInfo);

            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            devices[i] = new GameInputDevice(this, nativeInfo.ToDeviceInfo());
        }

        _devices = devices;

        return devices;
    }

    /// <summary>
    /// 取得指定裝置目前最新的 gamepad 狀態。
    /// </summary>
    /// <param name="kind">輸入類型；目前只支援 Gamepad。</param>
    /// <param name="device">目標裝置。</param>
    /// <returns>成功讀取時回傳 reading，否則回傳 null。</returns>
    public GameInputReading? GetCurrentReading(GameInputKind kind, GameInputDevice device)
    {
        if (kind != GameInputKind.Gamepad ||
            device.Owner != this)
        {
            return null;
        }

        int hr = GameInputNativeMethods.ReadGamepadState(
            _handle,
            device.DeviceInfo.DeviceId,
            out GameInputGamepadState state);

        return hr >= 0 ? new GameInputReading(new GamepadStateSnapshot(state)) : null;
    }

    /// <summary>
    /// 註冊讀取回呼。自有 shim 目前以 60 FPS 輪詢為主，此方法保留為 no-op 以維持 controller 結構。
    /// </summary>
    public GameInputCallbackRegistration RegisterReadingCallback(
        GameInputDevice _,
        GameInputKind __,
        Action<GameInputReading> ___)
        => new();

    /// <summary>
    /// 註冊裝置回呼。自有 shim 以定期重新列舉偵測變化，此方法保留為 no-op。
    /// </summary>
    public GameInputCallbackRegistration RegisterDeviceCallback(
        GameInputDevice? _,
        GameInputKind __,
        GameInputDeviceStatus ___,
        GameInputEnumerationKind ____,
        Action<GameInputDevice?, ulong, GameInputDeviceStatus, GameInputDeviceStatus> _____)
        => new();

    internal void SetRumbleState(GameInputDevice device, GameInputRumbleParams rumble)
    {
        if (device.Owner != this)
        {
            return;
        }

        _ = GameInputNativeMethods.SetRumbleState(
            _handle,
            device.DeviceInfo.DeviceId,
            rumble.LowFrequency,
            rumble.HighFrequency,
            rumble.LeftTrigger,
            rumble.RightTrigger);
    }

    internal GameInputDeviceStatus GetDeviceStatus(GameInputDevice device)
    {
        if (device.Owner != this)
        {
            return 0;
        }

        int hr = GameInputNativeMethods.GetDeviceStatus(
            _handle,
            device.DeviceInfo.DeviceId,
            out uint status);

        return hr >= 0 ? (GameInputDeviceStatus)status : 0;
    }

    /// <summary>
    /// 釋放 GameInput 內容。
    /// </summary>
    public void Dispose()
    {
        _devices = [];
        _handle.Dispose();
    }

    /// <summary>
    /// GameInput 回呼註冊憑證。
    /// </summary>
    internal sealed class GameInputCallbackRegistration : IDisposable
    {
        /// <summary>
        /// 釋放回呼註冊。
        /// </summary>
        public void Dispose()
        {

        }
    }
}

/// <summary>
/// GameInput 裝置包裝。
/// </summary>
internal sealed class GameInputDevice : IDisposable
{
    internal GameInputDevice(GameInput owner, GameInputDeviceInfo deviceInfo)
    {
        Owner = owner;
        DeviceInfo = deviceInfo;
    }

    internal GameInput Owner { get; }

    internal GameInputDeviceInfo DeviceInfo { get; }

    /// <summary>
    /// 取得裝置資訊。
    /// </summary>
    /// <returns>裝置資訊快照。</returns>
    public GameInputDeviceInfo GetDeviceInfo() => DeviceInfo;

    /// <summary>
    /// 取得目前裝置狀態。
    /// </summary>
    /// <returns>目前裝置狀態。</returns>
    public GameInputDeviceStatus GetDeviceStatus()
        => Owner.GetDeviceStatus(this);

    /// <summary>
    /// 設定震動狀態。
    /// </summary>
    /// <param name="rumble">震動參數。</param>
    public void SetRumbleState(GameInputRumbleParams rumble)
        => Owner.SetRumbleState(this, rumble);

    /// <summary>
    /// 釋放裝置包裝。裝置生命週期由 native context 管理，因此這裡不釋放底層實體。
    /// </summary>
    public void Dispose()
    {

    }
}

/// <summary>
/// GameInput reading 包裝。
/// </summary>
internal sealed class GameInputReading : IDisposable
{
    private readonly GamepadStateSnapshot _state;

    internal GameInputReading(GamepadStateSnapshot state)
    {
        _state = state;
    }

    /// <summary>
    /// 取得 gamepad 狀態快照。
    /// </summary>
    /// <returns>gamepad 狀態快照。</returns>
    public GamepadStateSnapshot GetGamepadState() => _state;

    /// <summary>
    /// 釋放 reading 包裝。
    /// </summary>
    public void Dispose()
    {

    }
}

/// <summary>
/// 安全釋放 native GameInput context。
/// </summary>
internal sealed class SafeGameInputContextHandle : SafeHandle
{
    private SafeGameInputContextHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {

    }

    /// <summary>
    /// 判斷 handle 是否無效。
    /// </summary>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// 釋放 native context。
    /// </summary>
    /// <returns>若釋放成功則回傳 true。</returns>
    protected override bool ReleaseHandle()
    {
        GameInputNativeMethods.Destroy(handle);

        return true;
    }
}

/// <summary>
/// GameInput native shim P/Invoke 宣告。
/// </summary>
internal static partial class GameInputNativeMethods
{
    private const string NativeLibraryName = "InputBox.GameInput.Native";

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputCreate")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int Create(out SafeGameInputContextHandle context);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputDestroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern void Destroy(nint context);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputSetFocusPolicy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int SetFocusPolicy(SafeGameInputContextHandle context, uint policy);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputRefreshDevices")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int RefreshDevices(SafeGameInputContextHandle context);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetDeviceCount")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetDeviceCount(SafeGameInputContextHandle context);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetDeviceInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetDeviceInfo(
        SafeGameInputContextHandle context,
        int index,
        out GameInputNativeDeviceInfo info);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetDeviceStatus")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetDeviceStatus(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId,
        out uint status);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputReadGamepadState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int ReadGamepadState(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId,
        out GameInputGamepadState state);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputSetRumbleState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int SetRumbleState(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId,
        float lowFrequency,
        float highFrequency,
        float leftTrigger,
        float rightTrigger);
}