using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證不同重啟觸發來源的確認策略，避免使用者主動點選重新啟動後仍被重複詢問。
/// </summary>
public sealed class RestartRequestDeciderTests
{
    /// <summary>
    /// 當使用者已明確從右鍵選單主動要求重新啟動時，流程應直接繼續，不應再要求第二次確認。
    /// </summary>
    [Fact]
    public void ShouldRestart_WhenSourceIsManualMenu_ReturnsTrueWithoutInvokingConfirmation()
    {
        bool confirmationInvoked = false;

        bool shouldRestart = RestartRequestDecider.ShouldRestart(
            RestartRequestSource.ManualMenu,
            () =>
            {
                confirmationInvoked = true;
                return DialogResult.No;
            });

        Assert.True(shouldRestart);
        Assert.False(confirmationInvoked);
    }

    /// <summary>
    /// 若重啟是由設定變更觸發且使用者選擇取消，則不應繼續執行重新啟動。
    /// </summary>
    [Fact]
    public void ShouldRestart_WhenSourceIsSettingChangeAndUserCancels_ReturnsFalse()
    {
        bool shouldRestart = RestartRequestDecider.ShouldRestart(
            RestartRequestSource.SettingChange,
            () => DialogResult.No);

        Assert.False(shouldRestart);
    }

    /// <summary>
    /// 若重啟是由設定變更觸發且使用者確認同意，則應繼續執行重新啟動。
    /// </summary>
    [Fact]
    public void ShouldRestart_WhenSourceIsSettingChangeAndUserConfirms_ReturnsTrue()
    {
        bool shouldRestart = RestartRequestDecider.ShouldRestart(
            RestartRequestSource.SettingChange,
            () => DialogResult.Yes);

        Assert.True(shouldRestart);
    }
}
