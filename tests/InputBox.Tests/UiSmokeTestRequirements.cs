using Xunit;

namespace InputBox.Tests;

/// <summary>
/// UI 冒煙測試的執行前置條件與 xUnit Collection 定義。
/// </summary>
internal static class UiSmokeTestRequirements
{
    /// <summary>
    /// UI 冒煙測試專用 Collection 名稱。
    /// </summary>
    public const string CollectionName = "UI Smoke";

    /// <summary>
    /// 是否允許執行桌面 UI 冒煙測試。
    /// 預設需顯式設定環境變數，避免本機一般測試誤啟動桌面應用程式。
    /// </summary>
    /// <remarks>
    /// 目前需同時滿足：Windows 平台、互動式桌面工作階段，以及
    /// <c>INPUTBOX_RUN_UI_TESTS=1</c> 環境變數已明確啟用。
    /// </remarks>
    public static bool IsEnabled =>
        OperatingSystem.IsWindows() &&
        Environment.UserInteractive &&
        string.Equals(
            Environment.GetEnvironmentVariable("INPUTBOX_RUN_UI_TESTS"),
            "1",
            StringComparison.Ordinal);
}

/// <summary>
/// 禁止 UI 冒煙測試平行執行，避免 WinForms 視窗、自動化焦點與剪貼簿互相干擾。
/// </summary>
[CollectionDefinition(UiSmokeTestRequirements.CollectionName, DisableParallelization = true)]
public sealed class UiSmokeTestCollection;
