using InputBox;
using InputBox.Core.Configuration;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Reflection;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證需重新啟動設定在使用者選擇稍後重啟時，仍會維持可見提醒，避免對話框取消後失去再次提示的入口。
/// </summary>
public sealed class RestartPromptStateTests : IDisposable
{
    /// <summary>
    /// 還原測試前的遊戲控制器輸入 API 設定。
    /// </summary>
    private readonly AppSettings.GamepadProvider _originalProvider = AppSettings.Current.GamepadProviderType;

    /// <summary>
    /// 還原測試前的歷程容量設定。
    /// </summary>
    private readonly int _originalHistoryCapacity = AppSettings.Current.HistoryCapacity;

    /// <summary>
    /// 測試後還原被修改的記憶體內設定，避免影響其他案例。
    /// </summary>
    public void Dispose()
    {
        AppSettings.Current.GamepadProviderType = _originalProvider;
        AppSettings.Current.HistoryCapacity = _originalHistoryCapacity;
    }

    /// <summary>
    /// 當使用者切換到需要重啟的控制器模式後，待重啟狀態應被視為仍未完成，避免取消後就遺失提醒。
    /// </summary>
    [Fact]
    public void HasPendingRestartChanges_WhenProviderChanged_ReturnsTrue()
    {
        RestartRequirementSnapshot baseline = RestartRequirementSnapshot.Capture(
            isDarkMode: false,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.XInput,
            historyCapacity: 100);

        bool hasPendingChanges = baseline.HasPendingRestartChanges(
            isDarkMode: false,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.GameInput,
            historyCapacity: 100);

        Assert.True(hasPendingChanges);
    }

    /// <summary>
    /// 主視窗在需重啟的設定已變更時，右鍵選單應持續保留重新啟動入口，避免使用者於首次取消後無法再次觸發提醒。
    /// </summary>
    [Fact]
    public void RefreshMenu_WhenRestartRequiredSettingChanged_AddsRestartMenuItem()
    {
        AppSettings.Current.GamepadProviderType = AppSettings.GamepadProvider.XInput;
        AppSettings.Current.HistoryCapacity = 100;

        MainForm form = new();

        try
        {
            AppSettings.Current.GamepadProviderType = AppSettings.GamepadProvider.GameInput;

            form.CreateControl();
            form.RefreshMenu();

            FieldInfo menuField = typeof(MainForm).GetField(
                "_cmsInput",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("找不到 MainForm._cmsInput 欄位。");

            ContextMenuStrip menu = Assert.IsType<ContextMenuStrip>(menuField.GetValue(form));

            Assert.Contains(
                menu.Items.OfType<ToolStripMenuItem>(),
                item => item.AccessibleName == Strings.Menu_ApplyThemeRestart);
        }
        finally
        {
            form.Close();
        }
    }

    /// <summary>
    /// 當存在待重啟設定變更時，主視窗標題應保留重啟提示標記，避免使用者只看到選單提示卻失去標題層級的狀態回饋。
    /// </summary>
    [Fact]
    public void RefreshMenu_WhenRestartRequiredSettingChanged_UpdatesTitleWithRestartSuffix()
    {
        AppSettings.Current.GamepadProviderType = AppSettings.GamepadProvider.XInput;
        AppSettings.Current.HistoryCapacity = 100;

        MainForm form = new();

        try
        {
            AppSettings.Current.GamepadProviderType = AppSettings.GamepadProvider.GameInput;

            form.CreateControl();
            form.RefreshMenu();

            FieldInfo titleField = typeof(MainForm).GetField(
                "_cachedTitlePrefix",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("找不到 MainForm._cachedTitlePrefix 欄位。");

            string titlePrefix = Assert.IsType<string>(titleField.GetValue(form));

            Assert.Contains(Strings.App_ThemePending_Suffix, titlePrefix, StringComparison.Ordinal);
        }
        finally
        {
            form.Close();
        }
    }

    /// <summary>
    /// 只有應用程式內設定變更時，待重啟原因應只標記為 AppSettings，供右鍵選單顯示「立即重新啟動程式」。
    /// </summary>
    [Fact]
    public void GetPendingReason_WhenOnlyAppSettingsChanged_ReturnsAppSettings()
    {
        RestartRequirementSnapshot baseline = RestartRequirementSnapshot.Capture(
            isDarkMode: false,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.XInput,
            historyCapacity: 100);

        RestartPendingReason reason = baseline.GetPendingReason(
            isDarkMode: false,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.GameInput,
            historyCapacity: 100);

        Assert.Equal(RestartPendingReason.AppSettings, reason);
    }

    /// <summary>
    /// 只有系統主題或高對比等環境變更時，待重啟原因應只標記為 SystemSettings，供右鍵選單顯示系統變更專用文案。
    /// </summary>
    [Fact]
    public void GetPendingReason_WhenOnlySystemSettingsChanged_ReturnsSystemSettings()
    {
        RestartRequirementSnapshot baseline = RestartRequirementSnapshot.Capture(
            isDarkMode: false,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.XInput,
            historyCapacity: 100);

        RestartPendingReason reason = baseline.GetPendingReason(
            isDarkMode: true,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.XInput,
            historyCapacity: 100);

        Assert.Equal(RestartPendingReason.SystemSettings, reason);
    }

    /// <summary>
    /// 當應用程式設定與系統環境都變更時，待重啟原因應同時包含兩者，供右鍵選單顯示「套用所有變更」。
    /// </summary>
    [Fact]
    public void GetPendingReason_WhenAppAndSystemSettingsChanged_ReturnsCombinedFlags()
    {
        RestartRequirementSnapshot baseline = RestartRequirementSnapshot.Capture(
            isDarkMode: false,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.XInput,
            historyCapacity: 100);

        RestartPendingReason reason = baseline.GetPendingReason(
            isDarkMode: true,
            isHighContrast: false,
            provider: AppSettings.GamepadProvider.GameInput,
            historyCapacity: 100);

        Assert.Equal(
            RestartPendingReason.AppSettings | RestartPendingReason.SystemSettings,
            reason);
    }

    /// <summary>
    /// 右鍵選單文字應依不同待重啟原因切換成相符的情境文案，涵蓋無待處理變更、應用程式設定、系統變更與兩者同時存在等情況。
    /// </summary>
    [Fact]
    public void RestartMenuTextResolver_GetMenuLabel_ReturnsContextSpecificText()
    {
        string systemOnlyLabel = Strings.ResourceManager.GetString("Menu_ApplySystemChangesRestart")
            ?? throw new InvalidOperationException("找不到 Menu_ApplySystemChangesRestart 資源。");

        string allChangesLabel = Strings.ResourceManager.GetString("Menu_ApplyAllChangesRestart")
            ?? throw new InvalidOperationException("找不到 Menu_ApplyAllChangesRestart 資源。");

        Assert.Equal(Strings.Menu_ApplyThemeRestart, RestartMenuTextResolver.GetMenuLabel(RestartPendingReason.None));
        Assert.Equal(Strings.Menu_ApplyThemeRestart, RestartMenuTextResolver.GetMenuLabel(RestartPendingReason.AppSettings));
        Assert.Equal(systemOnlyLabel, RestartMenuTextResolver.GetMenuLabel(RestartPendingReason.SystemSettings));
        Assert.Equal(allChangesLabel, RestartMenuTextResolver.GetMenuLabel(RestartPendingReason.AppSettings | RestartPendingReason.SystemSettings));
    }
}
