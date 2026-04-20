using InputBox.Core.Configuration;

namespace InputBox.Core.Services;

/// <summary>
/// 表示目前尚未套用、需要重新啟動才能完整生效的原因類型。
/// </summary>
[Flags]
internal enum RestartPendingReason
{
    /// <summary>
    /// 沒有待處理的重啟需求。
    /// </summary>
    None = 0,

    /// <summary>
    /// 因應用程式內設定變更而需要重新啟動。
    /// </summary>
    AppSettings = 1,

    /// <summary>
    /// 因系統主題、高對比等環境變更而需要重新啟動。
    /// </summary>
    SystemSettings = 2,
}

/// <summary>
/// 記錄啟動當下所有「需重新啟動才會完全生效」的設定快照，
/// 供主視窗後續判斷是否仍存在待處理的重啟需求。
/// </summary>
internal readonly record struct RestartRequirementSnapshot(
    bool IsDarkMode,
    bool IsHighContrast,
    AppSettings.GamepadProvider Provider,
    int HistoryCapacity)
{
    /// <summary>
    /// 依指定的執行期狀態建立重啟需求快照。
    /// </summary>
    /// <param name="isDarkMode">是否為深色模式。</param>
    /// <param name="isHighContrast">是否啟用高對比模式。</param>
    /// <param name="provider">目前使用的遊戲控制器提供者。</param>
    /// <param name="historyCapacity">目前的歷史記錄容量。</param>
    /// <returns>封裝了指定狀態的重啟需求快照。</returns>
    public static RestartRequirementSnapshot Capture(
        bool isDarkMode,
        bool isHighContrast,
        AppSettings.GamepadProvider provider,
        int historyCapacity)
    {
        return new RestartRequirementSnapshot(
            isDarkMode,
            isHighContrast,
            provider,
            historyCapacity);
    }

    /// <summary>
    /// 依目前設定與環境狀態建立快照。
    /// </summary>
    /// <param name="isDarkMode">是否為深色模式。</param>
    /// <param name="isHighContrast">是否啟用高對比模式。</param>
    /// <returns>以目前 <see cref="AppSettings.Current"/> 為基礎建立的重啟需求快照。</returns>
    public static RestartRequirementSnapshot CaptureCurrent(bool isDarkMode, bool isHighContrast)
    {
        return Capture(
            isDarkMode,
            isHighContrast,
            AppSettings.Current.GamepadProviderType,
            AppSettings.Current.HistoryCapacity);
    }

    /// <summary>
    /// 判斷目前系統環境是否與啟動時不同，表示仍有待重新啟動套用的系統變更。
    /// </summary>
    /// <param name="isDarkMode">目前的深色模式狀態。</param>
    /// <param name="isHighContrast">目前的高對比模式狀態。</param>
    /// <returns>若深色模式或高對比狀態與快照不一致則為 true。</returns>
    public bool HasPendingSystemSettingChanges(bool isDarkMode, bool isHighContrast)
    {
        return IsDarkMode != isDarkMode ||
               IsHighContrast != isHighContrast;
    }

    /// <summary>
    /// 判斷目前應用程式內設定是否與啟動時不同，表示仍有待重新啟動套用的設定變更。
    /// </summary>
    /// <param name="settings">目前的應用程式設定實例。</param>
    /// <returns>若控制器提供者或歷史記錄容量與快照不一致則為 true。</returns>
    public bool HasPendingAppSettingChanges(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Provider != settings.GamepadProviderType ||
               HistoryCapacity != settings.HistoryCapacity;
    }

    /// <summary>
    /// 取得目前待重新啟動的原因旗標。
    /// </summary>
    /// <param name="isDarkMode">目前的深色模式狀態。</param>
    /// <param name="isHighContrast">目前的高對比模式狀態。</param>
    /// <param name="provider">目前的遊戲控制器提供者。</param>
    /// <param name="historyCapacity">目前的歷史記錄容量。</param>
    /// <returns>描述待重啟原因的 <see cref="RestartPendingReason"/> 旗標組合。</returns>
    public RestartPendingReason GetPendingReason(
        bool isDarkMode,
        bool isHighContrast,
        AppSettings.GamepadProvider provider,
        int historyCapacity)
    {
        RestartPendingReason reason = RestartPendingReason.None;

        if (HasPendingSystemSettingChanges(isDarkMode, isHighContrast))
        {
            reason |= RestartPendingReason.SystemSettings;
        }

        if (Provider != provider ||
            HistoryCapacity != historyCapacity)
        {
            reason |= RestartPendingReason.AppSettings;
        }

        return reason;
    }

    /// <summary>
    /// 判斷目前狀態是否與啟動時快照不同，表示仍有待重新啟動套用的變更。
    /// </summary>
    /// <param name="isDarkMode">目前的深色模式狀態。</param>
    /// <param name="isHighContrast">目前的高對比模式狀態。</param>
    /// <param name="provider">目前的遊戲控制器提供者。</param>
    /// <param name="historyCapacity">目前的歷史記錄容量。</param>
    /// <returns>若存在任何待重啟原因則為 true。</returns>
    public bool HasPendingRestartChanges(
        bool isDarkMode,
        bool isHighContrast,
        AppSettings.GamepadProvider provider,
        int historyCapacity)
    {
        return GetPendingReason(isDarkMode, isHighContrast, provider, historyCapacity) != RestartPendingReason.None;
    }

    /// <summary>
    /// 取得目前設定與環境對應的待重啟原因旗標。
    /// </summary>
    /// <param name="settings">目前的應用程式設定實例。</param>
    /// <param name="isDarkMode">目前的深色模式狀態。</param>
    /// <param name="isHighContrast">目前的高對比模式狀態。</param>
    /// <returns>描述待重啟原因的 <see cref="RestartPendingReason"/> 旗標組合。</returns>
    public RestartPendingReason GetPendingReason(AppSettings settings, bool isDarkMode, bool isHighContrast)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return GetPendingReason(
            isDarkMode,
            isHighContrast,
            settings.GamepadProviderType,
            settings.HistoryCapacity);
    }

    /// <summary>
    /// 判斷目前設定與環境是否仍存在待重新啟動的差異。
    /// </summary>
    /// <param name="settings">目前的應用程式設定實例。</param>
    /// <param name="isDarkMode">目前的深色模式狀態。</param>
    /// <param name="isHighContrast">目前的高對比模式狀態。</param>
    /// <returns>若存在任何待重啟原因則為 true。</returns>
    public bool HasPendingRestartChanges(AppSettings settings, bool isDarkMode, bool isHighContrast)
    {
        return GetPendingReason(settings, isDarkMode, isHighContrast) != RestartPendingReason.None;
    }
}