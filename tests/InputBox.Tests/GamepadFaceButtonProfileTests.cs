using InputBox.Core.Configuration;
using InputBox.Core.Input;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證手把 Face 鍵配置模式的解析與顯示規則，確保 Xbox、PlayStation 傳統與 Nintendo 模式在功能、標示與助記詞上保持一致。
/// </summary>
public sealed class GamepadFaceButtonProfileTests
{
    /// <summary>
    /// Auto 模式在 GameInput 偵測到 Sony 控制器時，應解析為 PlayStation 國際配置。
    /// </summary>
    [Fact]
    public void ResolveEffectiveLayout_AutoWithSonyGameInput_ReturnsPlayStationCrossConfirm()
    {
        AppSettings.GamepadFaceButtonMode resolved = GamepadFaceButtonProfile.ResolveEffectiveLayout(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            "Sony Interactive Entertainment DualSense Wireless Controller");

        Assert.Equal(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm, resolved);
    }

    /// <summary>
    /// Auto 模式偵測到 Sony 控制器時，應明確預設為 PS5 國際版配置，也就是 × 確認、○ 取消。
    /// </summary>
    [Fact]
    public void ResolveEffectiveLayout_AutoWithSonyGameInput_DefaultsToInternationalCrossConfirm()
    {
        AppSettings.GamepadFaceButtonMode resolved = GamepadFaceButtonProfile.ResolveEffectiveLayout(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            "Sony Interactive Entertainment DualSense Wireless Controller");
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetProfile(resolved);

        Assert.Equal(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm, resolved);
        Assert.Equal("×", profile.ConfirmLabel);
        Assert.Equal("○", profile.CancelLabel);
    }

    /// <summary>
    /// Auto 模式在 GameInput 偵測到 Nintendo 控制器時，應解析為 Nintendo 配置。
    /// </summary>
    [Fact]
    public void ResolveEffectiveLayout_AutoWithNintendoGameInput_ReturnsNintendo()
    {
        AppSettings.GamepadFaceButtonMode resolved = GamepadFaceButtonProfile.ResolveEffectiveLayout(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            "Nintendo Switch Pro Controller");

        Assert.Equal(AppSettings.GamepadFaceButtonMode.Nintendo, resolved);
    }

    /// <summary>
    /// 業界常見的藍牙名稱有時只會顯示 PS4 / PS5 或 Wireless Controller，仍應辨識為 PlayStation，而不是落回 Xbox。
    /// </summary>
    /// <param name="deviceName">測試用的 PlayStation 裝置名稱樣本。</param>
    [Theory]
    [InlineData("PS5 Wireless Controller")]
    [InlineData("PS4 Controller")]
    [InlineData("DS5 Wireless Controller")]
    [InlineData("DS4 Controller")]
    public void ResolveEffectiveLayout_AutoWithCommonPlayStationAliases_ReturnsPlayStationCrossConfirm(string deviceName)
    {
        AppSettings.GamepadFaceButtonMode resolved = GamepadFaceButtonProfile.ResolveEffectiveLayout(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            deviceName);

        Assert.Equal(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm, resolved);
    }

    /// <summary>
    /// 連線方式或配件名稱如 Wireless / Adapter 只應視為雜訊；若沒有品牌特徵，應保守落回 Xbox 配置。
    /// </summary>
    /// <param name="deviceName">只包含傳輸或配件資訊的裝置名稱樣本。</param>
    [Theory]
    [InlineData("Wireless Adapter")]
    [InlineData("USB Wireless Receiver")]
    [InlineData("Bluetooth Gamepad Adapter")]
    public void ResolveEffectiveLayout_AutoWithTransportOnlyNames_FallsBackToXbox(string deviceName)
    {
        AppSettings.GamepadFaceButtonMode resolved = GamepadFaceButtonProfile.ResolveEffectiveLayout(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            deviceName);

        Assert.Equal(AppSettings.GamepadFaceButtonMode.Xbox, resolved);
    }

    /// <summary>
    /// 使用者一旦明確指定 Xbox 模式，即使偵測到 Sony 裝置也不得被自動覆蓋。
    /// </summary>
    [Fact]
    public void ResolveEffectiveLayout_ManualXbox_IgnoresDetectedSonyController()
    {
        AppSettings.GamepadFaceButtonMode resolved = GamepadFaceButtonProfile.ResolveEffectiveLayout(
            AppSettings.GamepadFaceButtonMode.Xbox,
            AppSettings.GamepadProvider.GameInput,
            "Sony Interactive Entertainment DualSense Wireless Controller");

        Assert.Equal(AppSettings.GamepadFaceButtonMode.Xbox, resolved);
    }

    /// <summary>
    /// PlayStation 傳統模式的主動作與取消動作助記詞應對調，對應右側確認、下側取消的實際習慣。
    /// </summary>
    [Fact]
    public void GetProfile_PlayStationTraditional_SwapsConfirmCancelMnemonics()
    {
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetProfile(AppSettings.GamepadFaceButtonMode.PlayStationTraditional);

        Assert.Equal('B', profile.PrimaryMnemonic);
        Assert.Equal('A', profile.CancelMnemonic);
        Assert.Equal("○", profile.ConfirmLabel);
        Assert.Equal("×", profile.CancelLabel);
        Assert.Equal("□", profile.DeleteLabel);
        Assert.Equal("△", profile.MenuLabel);
    }

    /// <summary>
    /// PlayStation 國際配置應以 × 作為確認、○ 作為取消，對應現代主機介面的常見配置。
    /// </summary>
    [Fact]
    public void GetProfile_PlayStationCrossConfirm_UsesSouthConfirmLabels()
    {
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetProfile(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm);

        Assert.Equal('A', profile.PrimaryMnemonic);
        Assert.Equal('B', profile.CancelMnemonic);
        Assert.Equal("×", profile.ConfirmLabel);
        Assert.Equal("○", profile.CancelLabel);
        Assert.Equal("□", profile.DeleteLabel);
        Assert.Equal("△", profile.MenuLabel);
    }

    /// <summary>
    /// PlayStation 兩種配置的名稱應直接標示 ○ / × 的確認差異，避免 Traditional 之類的模糊命名。
    /// </summary>
    [Fact]
    public void FriendlyModeName_PlayStationModes_UseExplicitConfirmSymbols()
    {
        string circleConfirm = GamepadFaceButtonProfile.GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.PlayStationTraditional);
        string crossConfirm = GamepadFaceButtonProfile.GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm);

        Assert.Contains("○", circleConfirm);
        Assert.Contains("×", crossConfirm);
    }

    /// <summary>
    /// Nintendo 模式應保留 A / B 作為確認與取消字樣，但 X / Y 的額外功能顯示需與 Xbox 風格相反。
    /// </summary>
    [Fact]
    public void GetProfile_Nintendo_UsesNintendoFaceLabels()
    {
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetProfile(AppSettings.GamepadFaceButtonMode.Nintendo);

        Assert.Equal('A', profile.PrimaryMnemonic);
        Assert.Equal('B', profile.CancelMnemonic);
        Assert.Equal("A", profile.ConfirmLabel);
        Assert.Equal("B", profile.CancelLabel);
        Assert.Equal("Y", profile.DeleteLabel);
        Assert.Equal("X", profile.MenuLabel);
    }

    /// <summary>
    /// Xbox 與 Nintendo 模式已可透過助記詞辨識對應實體按鍵，因此按鈕文字不應再重複顯示 A / B / X / Y 前綴。
    /// </summary>
    [Fact]
    public void FormatConfirmButtonText_Xbox_DoesNotDuplicateFaceLabelPrefix()
    {
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetProfile(AppSettings.GamepadFaceButtonMode.Xbox);

        string text = profile.FormatConfirmButtonText("Confirm");

        Assert.StartsWith("Confirm", text);
        Assert.DoesNotContain("A Confirm", text);
        Assert.EndsWith("(&A)", text);
    }

    /// <summary>
    /// PlayStation 模式仍應保留 ○ / × 等符號前綴，方便使用者辨識不同主按鍵配置。
    /// </summary>
    [Fact]
    public void FormatConfirmButtonText_PlayStation_KeepsSymbolPrefix()
    {
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetProfile(AppSettings.GamepadFaceButtonMode.PlayStationTraditional);

        string text = profile.FormatConfirmButtonText("Confirm");

        Assert.StartsWith("○ Confirm", text);
        Assert.EndsWith("(&B)", text);
    }

    /// <summary>
    /// Xbox 與 Nintendo 模式的選單／刪除類按鈕，也只應用助記詞標示，不應把 X / Y 額外顯示在按鈕文字前方。
    /// </summary>
    [Fact]
    public void FormatMenuButtonText_Nintendo_DoesNotDuplicateVisibleFaceLabelPrefix()
    {
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetProfile(AppSettings.GamepadFaceButtonMode.Nintendo);

        string text = profile.FormatMenuButtonText("Menu");

        Assert.StartsWith("Menu", text);
        Assert.DoesNotContain("X Menu", text);
        Assert.EndsWith("(&X)", text);
    }

    /// <summary>
    /// 新增的配置選單標題與套用提示應透過資源系統提供預設文字，避免 UI 字串硬編碼。
    /// </summary>
    [Fact]
    public void ResourceBackedLabels_ReturnLocalizedNonEmptyStrings()
    {
        string menuTitle = GamepadFaceButtonProfile.GetLayoutMenuTitle();
        string modeName = GamepadFaceButtonProfile.GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.PlayStationTraditional);
        string announcement = GamepadFaceButtonProfile.GetLayoutAppliedAnnouncement(AppSettings.GamepadFaceButtonMode.PlayStationTraditional);

        Assert.False(string.IsNullOrWhiteSpace(menuTitle));
        Assert.False(string.IsNullOrWhiteSpace(announcement));
        Assert.Contains(modeName, announcement);
    }

    /// <summary>
    /// 主畫面的手把說明文字應依目前配置動態切換，避免在 PlayStation 模式下仍播報 Xbox 的 A / B / X。
    /// </summary>
    [Fact]
    public void MainFormDescription_PlayStation_UsesCurrentFaceLabels()
    {
        string description = GamepadFaceButtonProfile.GetMainFormDescription(AppSettings.GamepadFaceButtonMode.PlayStationTraditional);

        Assert.Contains("○", description);
        Assert.Contains("×", description);
        Assert.Contains("□", description);
    }

    /// <summary>
    /// Auto 模式下若偵測到 Sony 控制器，UI 狀態摘要應同時反映 Auto 與實際生效的 PlayStation 配置。
    /// </summary>
    [Fact]
    public void LayoutStatusSummary_AutoWithSonyDevice_ReportsDetectedPlayStationLayout()
    {
        string summary = GamepadFaceButtonProfile.GetLayoutStatusSummary(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            "Sony Interactive Entertainment DualSense Wireless Controller");

        Assert.Contains(GamepadFaceButtonProfile.GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.Auto), summary);
        Assert.Contains(GamepadFaceButtonProfile.GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm), summary);
    }

    /// <summary>
    /// 主畫面標題列的手把模式提示，在 Auto + Sony 偵測下應同時顯示 Auto 與目前生效的 PlayStation。
    /// </summary>
    [Fact]
    public void TitleLayoutHint_AutoWithSonyDevice_ShowsAutoAndDetectedLayout()
    {
        string titleHint = GamepadFaceButtonProfile.GetTitleLayoutHint(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            "Sony Interactive Entertainment DualSense Wireless Controller");

        Assert.DoesNotContain("Gamepad", titleHint);
        Assert.DoesNotContain("控制器：", titleHint);
        Assert.Contains(GamepadFaceButtonProfile.GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.Auto), titleHint);
        Assert.Contains(GamepadFaceButtonProfile.GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm), titleHint);
    }

    /// <summary>
    /// Face 鍵配置選單的勾選項目應反映目前實際生效的配置；若使用 Auto，則應勾選解析後的有效模式，而非永遠停在 Auto。
    /// </summary>
    [Fact]
    public void MenuCheckedMode_AutoWithSonyDevice_ReturnsEffectivePlayStationLayout()
    {
        AppSettings.GamepadFaceButtonMode checkedMode = GamepadFaceButtonProfile.GetMenuCheckedMode(
            AppSettings.GamepadFaceButtonMode.Auto,
            AppSettings.GamepadProvider.GameInput,
            "Sony Interactive Entertainment DualSense Wireless Controller");

        Assert.Equal(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm, checkedMode);

        checkedMode = GamepadFaceButtonProfile.GetMenuCheckedMode(
            AppSettings.GamepadFaceButtonMode.Xbox,
            AppSettings.GamepadProvider.GameInput,
            "Sony Interactive Entertainment DualSense Wireless Controller");

        Assert.Equal(AppSettings.GamepadFaceButtonMode.Xbox, checkedMode);
    }
}