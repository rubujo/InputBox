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
            Marshal.ThrowExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005));
        }

        try
        {
            hr = GameInputNativeMethods.GetShimInfo(handle, out GameInputNativeShimInfo nativeShimInfo);

            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            GameInputShimInfo shimInfo = nativeShimInfo.ToShimInfo();
            shimInfo.AbiInfo.ThrowIfMismatch();

            return new GameInput(handle, shimInfo);
        }
        catch
        {
            handle.Dispose();

            throw;
        }
    }

    /// <summary>
    /// 嘗試取得 GameInput runtime 載入診斷資訊；此方法不會建立長生命週期 context。
    /// </summary>
    /// <param name="probeInfo">runtime probe 結果。</param>
    /// <returns>若 probe export 可呼叫則回傳 true。</returns>
    public static bool TryProbeRuntime(out GameInputRuntimeProbeInfo probeInfo)
    {
        try
        {
            _ = GameInputNativeMethods.ProbeRuntime(out GameInputNativeRuntimeProbeInfo nativeProbeInfo);
            probeInfo = nativeProbeInfo.ToProbeInfo();

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GameInput runtime probe failed: {ex.Message}");
            probeInfo = default;

            return false;
        }
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

    internal GameInputDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        int hr = GameInputNativeMethods.GetDiagnosticsSnapshot(
            _handle,
            out GameInputNativeDiagnosticsSnapshot snapshot);

        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return snapshot.ToDiagnosticsSnapshot();
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
/// GameInput native shim P/Invoke 宣告。集中對應 <c>InputBox.GameInput.Native</c>
/// shim 匯出的 C ABI；所有方法均在 MTA 背景輪詢執行緒呼叫，回傳值為 native HRESULT。
/// </summary>
internal static partial class GameInputNativeMethods
{
    /// <summary>
    /// Native shim DLL 名稱；由 .NET DllImport 依 <see cref="DllImportSearchPath"/>
    /// 規則於應用程式目錄與使用者目錄中搜尋。
    /// </summary>
    private const string NativeLibraryName = "InputBox.GameInput.Native";

    /// <summary>
    /// 探測 GameInput runtime 載入狀態，不建立持久 context。供啟動期分類
    /// LoadLibrary、GetProcAddress、GameInputInitialize 的失敗來源使用。
    /// </summary>
    /// <param name="info">回傳的探測資訊（ABI 版本、模組種類、嘗試/實際載入路徑等）。</param>
    /// <returns>Native HRESULT；<c>S_OK</c> 表示 runtime 可用。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputProbeRuntime")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int ProbeRuntime(out GameInputNativeRuntimeProbeInfo info);

    /// <summary>
    /// 建立 GameInput shim context；成功時將原生指標包裝為 <see cref="SafeGameInputContextHandle"/>，
    /// 由 SafeHandle 在 finalize/dispose 時呼叫對應的 <see cref="Destroy"/>。
    /// </summary>
    /// <param name="context">回傳的 context SafeHandle；失敗時為 invalid handle。</param>
    /// <returns>Native HRESULT；失敗時呼叫端應走退避 XInput 路徑。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputCreate")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int Create(out SafeGameInputContextHandle context);

    /// <summary>
    /// 釋放 native context；通常由 <see cref="SafeGameInputContextHandle.ReleaseHandle"/>
    /// 自動呼叫，不應由業務程式碼直接呼叫。
    /// </summary>
    /// <param name="context">原生 context 指標。</param>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputDestroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern void Destroy(nint context);

    /// <summary>
    /// 取得 shim 自身與已載入 GameInput runtime 的版本資訊，包含 ABI 版本、跨邊界 struct 大小、
    /// 已載入的模組種類與路徑，供 managed layer 用 <see cref="Marshal.SizeOf{T}()"/> 驗證 ABI。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="info">回傳的 shim/runtime 版本資訊結構。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetShimInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetShimInfo(
        SafeGameInputContextHandle context,
        out GameInputNativeShimInfo info);

    /// <summary>
    /// 取得 shim 內部累積的診斷快照（字串截斷旗標、timestamp stale、missing reading、
    /// device zombie refresh 等計數），僅供日誌與測試使用，不可改變按鍵語意。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="snapshot">回傳的診斷計數快照。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetDiagnosticsSnapshot")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetDiagnosticsSnapshot(
        SafeGameInputContextHandle context,
        out GameInputNativeDiagnosticsSnapshot snapshot);

    /// <summary>
    /// 設定 <c>IGameInput::SetFocusPolicy</c>，控制應用程式失去焦點時是否仍接收輸入。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="policy"><c>GameInputFocusPolicy</c> 位元旗標。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputSetFocusPolicy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int SetFocusPolicy(SafeGameInputContextHandle context, uint policy);

    /// <summary>
    /// 強制 shim 對 GameInput runtime 進行裝置重新列舉；用於連線/斷線回呼觀察到變更後的補抓。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputRefreshDevices")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int RefreshDevices(SafeGameInputContextHandle context);

    /// <summary>
    /// 回傳 shim 目前所知的裝置總數（含已連線與暫存中尚未確認斷線的裝置）。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <returns>裝置數；負值代表錯誤。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetDeviceCount")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetDeviceCount(SafeGameInputContextHandle context);

    /// <summary>
    /// 依索引取得單一裝置的中繼資訊（VID/PID、displayName、capabilities、支援馬達等），
    /// 索引以 <see cref="GetDeviceCount"/> 同一回合的計數為準。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="index">裝置索引（0-based）。</param>
    /// <param name="info">回傳的裝置資訊結構。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetDeviceInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetDeviceInfo(
        SafeGameInputContextHandle context,
        int index,
        out GameInputNativeDeviceInfo info);

    /// <summary>
    /// 依穩定 deviceId 查詢目前裝置狀態旗標（Connected / Synced / Wireless 等）。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="deviceId">穩定裝置識別字串（UTF-8）。</param>
    /// <param name="status">回傳的 <c>GameInputDeviceStatus</c> 旗標。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputGetDeviceStatus")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int GetDeviceStatus(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId,
        out uint status);

    /// <summary>
    /// 同步讀取指定裝置目前 gamepad reading 快照；用於 polling 與 Resume 預同步。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="deviceId">穩定裝置識別字串（UTF-8）。</param>
    /// <param name="state">回傳的 gamepad 狀態快照。</param>
    /// <returns>Native HRESULT；<c>InputBoxGameInputNoReading</c> 代表暫無 reading。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputReadGamepadState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int ReadGamepadState(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId,
        out GameInputGamepadState state);

    /// <summary>
    /// 註冊 reading callback；shim 會在 GameInput runtime 推送新 reading 時喚醒輪詢執行緒。
    /// callback 僅供 wake-up 與診斷快照，不得直接觸發 UI 或輸入命令。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="deviceId">穩定裝置識別字串；傳 <c>null</c> 表示訂閱全部裝置。</param>
    /// <param name="kind">輸入種類過濾（<c>GameInputKind</c> 位元旗標）。</param>
    /// <param name="callback">由 managed 端建立、需 keep-alive 的 delegate。</param>
    /// <param name="callbackContext">回呼時傳回的 user context（通常為 GCHandle）。</param>
    /// <param name="callbackToken">回傳的回呼識別 token，用於 <see cref="UnregisterCallback"/>。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputRegisterReadingCallback")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int RegisterReadingCallback(
        SafeGameInputContextHandle context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceId,
        uint kind,
        GameInputNativeReadingCallback callback,
        nint callbackContext,
        out ulong callbackToken);

    /// <summary>
    /// 註冊 device callback；shim 會在裝置連線/斷線/狀態變更時通知 managed 端進行重列舉。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="deviceId">穩定裝置識別字串；傳 <c>null</c> 表示訂閱全部裝置。</param>
    /// <param name="kind">輸入種類過濾（<c>GameInputKind</c> 位元旗標）。</param>
    /// <param name="statusFilter">關注的狀態變更旗標（<c>GameInputDeviceStatus</c>）。</param>
    /// <param name="enumerationKind">列舉模式（<c>GameInputEnumerationKind</c>）。</param>
    /// <param name="callback">由 managed 端建立、需 keep-alive 的 delegate。</param>
    /// <param name="callbackContext">回呼時傳回的 user context（通常為 GCHandle）。</param>
    /// <param name="callbackToken">回傳的回呼識別 token，用於 <see cref="UnregisterCallback"/>。</param>
    /// <returns>Native HRESULT。</returns>
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

    /// <summary>
    /// 註銷先前由 <see cref="RegisterReadingCallback"/> 或 <see cref="RegisterDeviceCallback"/>
    /// 取得的 callback token；shim 會停止對應的 callback 並讓 managed delegate 可被 GC。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="callbackToken">先前回傳的 callback token。</param>
    /// <returns>Native HRESULT。</returns>
    [DllImport(NativeLibraryName, EntryPoint = "InputBoxGameInputUnregisterCallback")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.UserDirectories)]
    internal static extern int UnregisterCallback(
        SafeGameInputContextHandle context,
        ulong callbackToken);

    /// <summary>
    /// 套用震動參數到指定裝置；四個馬達強度均為 <c>[0.0, 1.0]</c> 正規化值，
    /// 不支援的馬達會被 GameInput runtime 忽略。
    /// </summary>
    /// <param name="context">已建立的 GameInput context。</param>
    /// <param name="deviceId">穩定裝置識別字串（UTF-8）。</param>
    /// <param name="lowFrequency">低頻主馬達強度。</param>
    /// <param name="highFrequency">高頻主馬達強度。</param>
    /// <param name="leftTrigger">左扳機馬達強度（不支援時忽略）。</param>
    /// <param name="rightTrigger">右扳機馬達強度（不支援時忽略）。</param>
    /// <returns>Native HRESULT。</returns>
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

/// <summary>
/// Reading callback delegate：shim 在 GameInput runtime 推送新 gamepad reading 時呼叫，
/// 通常僅用於喚醒 MTA 背景輪詢執行緒；正式輸入命令仍由 60 FPS polling 消費。
/// </summary>
/// <param name="callbackContext">註冊時傳入的 user context（通常為 GCHandle）。</param>
/// <param name="state">推送的 gamepad 狀態（POD）。</param>
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void GameInputNativeReadingCallback(
    nint callbackContext,
    ref GameInputGamepadState state);

/// <summary>
/// Device callback delegate：shim 在裝置連線/斷線/狀態變更時呼叫,通知 managed 端
/// 安排裝置重列舉;callback 內僅可記錄旗標或排入工作佇列,不得直接觸發 UI 或輸入。
/// </summary>
/// <param name="callbackContext">註冊時傳入的 user context（通常為 GCHandle）。</param>
/// <param name="deviceId">穩定裝置識別字串的 UTF-8 指標（由 shim 持有）。</param>
/// <param name="timestamp">事件時戳（<c>QueryPerformanceCounter</c> 等價單位）。</param>
/// <param name="currentStatus">目前的 <c>GameInputDeviceStatus</c> 旗標。</param>
/// <param name="previousStatus">事件前的 <c>GameInputDeviceStatus</c> 旗標。</param>
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void GameInputNativeDeviceCallback(
    nint callbackContext,
    nint deviceId,
    ulong timestamp,
    uint currentStatus,
    uint previousStatus);
