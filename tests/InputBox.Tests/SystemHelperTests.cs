using InputBox.Core.Utilities;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// <see cref="SystemHelper.EvaluateGamescopeEnvironment"/> 的 Gamescope 環境偵測邏輯單元測試。
/// </summary>
/// <remarks>
/// <see cref="SystemHelper.IsRunningOnGamescope"/> 在程式啟動時快取偵測結果，無法於測試中重新執行；
/// 本套件透過 <see cref="SystemHelper.EvaluateGamescopeEnvironment"/> 直接注入環境變數值，
/// 覆蓋所有判斷分支而不依賴實際執行環境。
/// </remarks>
public sealed class SystemHelperTests
{
    // ── EvaluateGamescopeEnvironment：GAMESCOPE_WAYLAND_DISPLAY 未設定 ───────

    /// <summary>
    /// GAMESCOPE_WAYLAND_DISPLAY 為 null 時應回傳 false（非 Gamescope 環境）。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_NullDisplay_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment(null, null, null));
    }

    /// <summary>
    /// GAMESCOPE_WAYLAND_DISPLAY 為空字串時應回傳 false。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_EmptyDisplay_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("", null, null));
    }

    /// <summary>
    /// GAMESCOPE_WAYLAND_DISPLAY 為純空白時應回傳 false。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_WhitespaceDisplay_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("   ", null, null));
    }

    // ── EvaluateGamescopeEnvironment：Gamescope 遊戲模式（應回傳 true）───────

    /// <summary>
    /// GAMESCOPE_WAYLAND_DISPLAY 有值且無 Plasma 桌面環境變數時應回傳 true（標準遊戲模式）。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_DisplaySetNoPlasma_ReturnsTrue()
    {
        Assert.True(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", null, null));
    }

    /// <summary>
    /// GAMESCOPE_WAYLAND_DISPLAY 有值且 XDG_CURRENT_DESKTOP 為非 KDE 值時應回傳 true。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_DisplaySetNonKdeDesktop_ReturnsTrue()
    {
        Assert.True(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", "GNOME", null));
    }

    // ── EvaluateGamescopeEnvironment：KDE Plasma 桌面排除（應回傳 false）────

    /// <summary>
    /// XDG_CURRENT_DESKTOP=KDE 時應視為桌面模式，即使 GAMESCOPE_WAYLAND_DISPLAY 仍被繼承。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_XdgDesktopKde_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", "KDE", null));
    }

    /// <summary>
    /// XDG_CURRENT_DESKTOP=PLASMA 時應視為桌面模式。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_XdgDesktopPlasma_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", "PLASMA", null));
    }

    /// <summary>
    /// XDG_CURRENT_DESKTOP 比對應不區分大小寫（kde 小寫亦應排除）。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_XdgDesktopKdeLowercase_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", "kde", null));
    }

    /// <summary>
    /// DESKTOP_SESSION=plasma 時應視為桌面模式（SteamOS 桌面模式的典型值）。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_DesktopSessionPlasma_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", null, "plasma"));
    }

    /// <summary>
    /// DESKTOP_SESSION=PLASMA 大寫亦應排除（比對不區分大小寫）。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_DesktopSessionPlasmaUppercase_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", null, "PLASMA"));
    }

    /// <summary>
    /// XDG_CURRENT_DESKTOP 與 DESKTOP_SESSION 皆指向 Plasma 時應回傳 false（雙重確認場景）。
    /// </summary>
    [Fact]
    public void EvaluateGamescopeEnvironment_BothPlasmaEnvVars_ReturnsFalse()
    {
        Assert.False(SystemHelper.EvaluateGamescopeEnvironment("gamescope-0", "KDE", "plasma"));
    }
}
