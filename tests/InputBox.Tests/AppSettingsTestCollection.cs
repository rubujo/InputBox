using Xunit;

namespace InputBox.Tests;

/// <summary>
/// AppSettings 相關測試的共用集合名稱，強制關閉平行執行，避免並行測試透過共用的 AppSettings.Current 單例互相干擾。
/// </summary>
internal static class AppSettingsTestRequirements
{
    /// <summary>
    /// AppSettings 測試專用 Collection 名稱。
    /// </summary>
    public const string CollectionName = "App Settings";
}

/// <summary>
/// 禁止 AppSettings 相關測試平行執行，防止 AppSettings.Current 單例在並行修改或 Load() 替換期間產生競態條件。
/// </summary>
[CollectionDefinition(AppSettingsTestRequirements.CollectionName, DisableParallelization = true)]
public sealed class AppSettingsTestCollection;