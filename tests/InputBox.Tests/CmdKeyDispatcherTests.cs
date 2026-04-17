using InputBox.Core.Services;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證右鍵選單在混合輸入情境下的鍵盤命令分派，避免滑鼠開啟選單後無法再用鍵盤或控制器接手操作。
/// </summary>
public sealed class CmdKeyDispatcherTests
{
    /// <summary>
    /// 當右鍵選單未顯示時，不應攔截任何方向鍵或確認鍵，避免影響一般輸入流程。
    /// </summary>
    [Fact]
    public void TryGetContextMenuAction_MenuHidden_ReturnsFalse()
    {
        bool handled = CmdKeyDispatcher.TryGetContextMenuAction(
            Keys.Enter,
            menuVisible: false,
            out string? action);

        Assert.False(handled);
        Assert.Null(action);
    }

    /// <summary>
    /// 當右鍵選單顯示時，方向鍵應被轉譯為一致的選單導覽動作，讓滑鼠與鍵盤可交替使用。
    /// </summary>
    [Fact]
    public void TryGetContextMenuAction_ArrowKeys_MapToNavigation()
    {
        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Up, true, out string? upAction));
        Assert.Equal("Up", upAction);

        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Down, true, out string? downAction));
        Assert.Equal("Down", downAction);

        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Left, true, out string? leftAction));
        Assert.Equal("Left", leftAction);

        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Right, true, out string? rightAction));
        Assert.Equal("Right", rightAction);
    }

    /// <summary>
    /// 當右鍵選單顯示時，只有 Enter 應保留為原生確認鍵；Space 不應被額外客製成選單確認動作。
    /// </summary>
    [Fact]
    public void TryGetContextMenuAction_ConfirmKey_OnlyEnterMapsToConfirm()
    {
        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Enter, true, out string? enterAction));
        Assert.Equal("Confirm", enterAction);

        Assert.False(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Space, true, out string? spaceAction));
        Assert.Null(spaceAction);
    }

    /// <summary>
    /// 當右鍵選單顯示時，Escape、Home 與 End 應保留為取消與片語分頁邊界導覽命令。
    /// </summary>
    [Fact]
    public void TryGetContextMenuAction_SpecialKeys_MapToExpectedActions()
    {
        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Escape, true, out string? cancelAction));
        Assert.Equal("Cancel", cancelAction);

        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.Home, true, out string? firstPageAction));
        Assert.Equal("PhrasePageFirst", firstPageAction);

        Assert.True(CmdKeyDispatcher.TryGetContextMenuAction(Keys.End, true, out string? lastPageAction));
        Assert.Equal("PhrasePageLast", lastPageAction);
    }
}
