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
    public bool HasPendingSystemSettingChanges(bool isDarkMode, bool isHighContrast)
    {
        return IsDarkMode != isDarkMode ||
               IsHighContrast != isHighContrast;
    }

    /// <summary>
    /// 判斷目前應用程式內設定是否與啟動時不同，表示仍有待重新啟動套用的設定變更。
    /// </summary>
    public bool HasPendingAppSettingChanges(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Provider != settings.GamepadProviderType ||
               HistoryCapacity != settings.HistoryCapacity;
    }

    /// <summary>
    /// 取得目前待重新啟動的原因旗標。
    /// </summary>
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
    public bool HasPendingRestartChanges(AppSettings settings, bool isDarkMode, bool isHighContrast)
    {
        return GetPendingReason(settings, isDarkMode, isHighContrast) != RestartPendingReason.None;
    }
}
