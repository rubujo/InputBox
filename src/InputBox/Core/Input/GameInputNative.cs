using System.Runtime.InteropServices;
using System.Diagnostics;

namespace InputBox.Core.Input;

/// <summary>
/// GameInput native shim 的受控進入點，負責把窄版 C ABI 轉成專案內部型別。
/// </summary>
internal sealed class GameInput : IDisposable
{
    private static readonly GameInputNativeReadingCallback ReadingCallback = OnNativeReadingCallback;
    private static readonly GameInputNativeDeviceCallback DeviceCallback = OnNativeDeviceCallback;
    private readonly SafeGameInputContextHandle _handle;
    private GameInputDevice[] _devices = [];

    private GameInput(SafeGameInputContextHandle handle, GameInputShimInfo shimInfo)
    {
        _handle = handle;
        ShimInfo = shimInfo;
    }

    /// <summary>
    /// 取得 native shim 與 GameInput runtime 載入診斷資訊。
    /// </summary>
    public GameInputShimInfo ShimInfo { get; }

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

        hr = GameInputNativeMethods.GetShimInfo(handle, out GameInputNativeShimInfo nativeShimInfo);

        if (hr < 0)
        {
            handle.Dispose();
            Marshal.ThrowExceptionForHR(hr);
        }

        return new GameInput(handle, nativeShimInfo.ToShimInfo());
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
    /// 註冊讀取回呼。控制器仍以 60 FPS 輪詢為主，此回呼只作為輔助訊號來源。
    /// </summary>
    public GameInputCallbackRegistration RegisterReadingCallback(
        GameInputDevice device,
        GameInputKind kind,
        Action<GameInputReading> callback)
    {
        if (device.Owner != this ||
            kind != GameInputKind.Gamepad)
        {
            return new GameInputCallbackRegistration();
        }

        var callbackState = new ReadingCallbackState(callback);
        GCHandle callbackStateHandle = GCHandle.Alloc(callbackState);

        int hr = GameInputNativeMethods.RegisterReadingCallback(
            _handle,
            device.DeviceInfo.DeviceId,
            (uint)kind,
            ReadingCallback,
            GCHandle.ToIntPtr(callbackStateHandle),
            out ulong callbackToken);

        if (hr < 0)
        {
            callbackStateHandle.Free();
            Marshal.ThrowExceptionForHR(hr);
        }

        return new GameInputCallbackRegistration(_handle, callbackToken, callbackStateHandle);
    }

    /// <summary>
    /// 註冊裝置回呼，用於要求輪詢執行緒重新整理裝置清單。
    /// </summary>
    public GameInputCallbackRegistration RegisterDeviceCallback(
        GameInputDevice? device,
        GameInputKind kind,
        GameInputDeviceStatus statusFilter,
        GameInputEnumerationKind enumerationKind,
        Action<GameInputDevice?, ulong, GameInputDeviceStatus, GameInputDeviceStatus> callback)
    {
        if (kind != GameInputKind.Gamepad)
        {
            return new GameInputCallbackRegistration();
        }

        var callbackState = new DeviceCallbackState(this, callback);
        GCHandle callbackStateHandle = GCHandle.Alloc(callbackState);

        int hr = GameInputNativeMethods.RegisterDeviceCallback(
            _handle,
            device?.DeviceInfo.DeviceId,
            (uint)kind,
            (uint)statusFilter,
            (uint)enumerationKind,
            DeviceCallback,
            GCHandle.ToIntPtr(callbackStateHandle),
            out ulong callbackToken);

        if (hr < 0)
        {
            callbackStateHandle.Free();
            Marshal.ThrowExceptionForHR(hr);
        }

        return new GameInputCallbackRegistration(_handle, callbackToken, callbackStateHandle);
    }

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

    private GameInputDevice? FindDeviceById(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        GameInputDevice[] devices = _devices;

        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].DeviceInfo.DeviceId.Equals(deviceId, StringComparison.Ordinal))
            {
                return devices[i];
            }
        }

        return null;
    }

    private static void OnNativeReadingCallback(nint callbackContext, ref GameInputGamepadState state)
    {
        try
        {
            if (GCHandle.FromIntPtr(callbackContext).Target is ReadingCallbackState callbackState)
            {
                callbackState.Callback(new GameInputReading(new GamepadStateSnapshot(state)));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GameInput native reading callback failed: {ex.Message}");
        }
    }

    private static void OnNativeDeviceCallback(
        nint callbackContext,
        nint deviceId,
        ulong timestamp,
        uint currentStatus,
        uint previousStatus)
    {
        try
        {
            if (GCHandle.FromIntPtr(callbackContext).Target is not DeviceCallbackState callbackState)
            {
                return;
            }

            string parsedDeviceId = Marshal.PtrToStringUTF8(deviceId) ?? string.Empty;
            GameInputDevice? device = callbackState.Owner.FindDeviceById(parsedDeviceId);

            callbackState.Callback(
                device,
                timestamp,
                (GameInputDeviceStatus)currentStatus,
                (GameInputDeviceStatus)previousStatus);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GameInput native device callback failed: {ex.Message}");
        }
    }

    private sealed class ReadingCallbackState(Action<GameInputReading> callback)
    {
        public Action<GameInputReading> Callback { get; } = callback;
    }

    private sealed class DeviceCallbackState(
        GameInput owner,
        Action<GameInputDevice?, ulong, GameInputDeviceStatus, GameInputDeviceStatus> callback)
    {
        public GameInput Owner { get; } = owner;

        public Action<GameInputDevice?, ulong, GameInputDeviceStatus, GameInputDeviceStatus> Callback { get; } = callback;
    }

    /// <summary>
    /// GameInput 回呼註冊憑證。
    /// </summary>
    internal sealed class GameInputCallbackRegistration : IDisposable
    {
        private readonly SafeGameInputContextHandle? _handle;
        private readonly ulong _callbackToken;
        private GCHandle _callbackStateHandle;
        private int _disposed;

        public GameInputCallbackRegistration()
        {

        }

        internal GameInputCallbackRegistration(
            SafeGameInputContextHandle handle,
            ulong callbackToken,
            GCHandle callbackStateHandle)
        {
            _handle = handle;
            _callbackToken = callbackToken;
            _callbackStateHandle = callbackStateHandle;
        }

        /// <summary>
        /// 釋放回呼註冊。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (_handle != null &&
                    !_handle.IsInvalid &&
                    _callbackToken != 0)
                {
                    _ = GameInputNativeMethods.UnregisterCallback(_handle, _callbackToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameInput unregister callback failed: {ex.Message}");
            }
            finally
            {
                if (_callbackStateHandle.IsAllocated)
                {
                    _callbackStateHandle.Free();
                }
            }

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

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetShimInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetShimInfo(
        SafeGameInputContextHandle context,
        out GameInputNativeShimInfo info);

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

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputRegisterReadingCallback")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int RegisterReadingCallback(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceId,
        uint kind,
        GameInputNativeReadingCallback callback,
        nint callbackContext,
        out ulong callbackToken);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputRegisterDeviceCallback")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int RegisterDeviceCallback(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceId,
        uint kind,
        uint statusFilter,
        uint enumerationKind,
        GameInputNativeDeviceCallback callback,
        nint callbackContext,
        out ulong callbackToken);

    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputUnregisterCallback")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int UnregisterCallback(
        SafeGameInputContextHandle context,
        ulong callbackToken);

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

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void GameInputNativeReadingCallback(
    nint callbackContext,
    ref GameInputGamepadState state);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void GameInputNativeDeviceCallback(
    nint callbackContext,
    nint deviceId,
    ulong timestamp,
    uint currentStatus,
    uint previousStatus);
