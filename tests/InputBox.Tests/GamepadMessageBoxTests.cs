using InputBox.Core.Controls;
using System.Reflection;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// GamepadMessageBox 關閉生命週期回歸測試。
/// <para>驗證當 FormClosing 被取消時，對話框不應提早釋放執行期資源，避免畫面仍開啟但內部狀態已被清空。</para>
/// </summary>
public sealed class GamepadMessageBoxTests
{
    /// <summary>
    /// 若 FormClosing 被取消，對話框應保留取消權杖等執行期資源，避免仍顯示於畫面上卻失去互動能力。
    /// </summary>
    [Fact]
    public void OnFormClosing_WhenCancelled_DoesNotReleaseRuntimeResources()
    {
        using GamepadMessageBox dialog = new(
            "test message",
            "test caption",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        dialog.FormClosing += (_, e) => e.Cancel = true;

        MethodInfo onFormClosing = typeof(GamepadMessageBox).GetMethod(
            "OnFormClosing",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GamepadMessageBox.OnFormClosing。");

        FieldInfo ctsField = typeof(GamepadMessageBox).GetField(
            "_cts",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GamepadMessageBox._cts 欄位。");

        FormClosingEventArgs args = new(CloseReason.UserClosing, cancel: false);

        onFormClosing.Invoke(dialog, [args]);

        Assert.True(args.Cancel);
        Assert.False(dialog.IsDisposed);
        Assert.NotNull(ctsField.GetValue(dialog));
    }
}
