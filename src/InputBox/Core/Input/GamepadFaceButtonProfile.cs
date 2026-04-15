using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Resources;

namespace InputBox.Core.Input;

/// <summary>
/// Face 鍵配置模式的執行期描述，集中管理不同控制器樣式的顯示文字、助記詞與自動解析規則。
/// </summary>
internal readonly record struct GamepadFaceButtonProfile
{
    /// <summary>
    /// 初始化一個 Face 鍵配置描述，封裝目前模式對應的文字標籤與助記詞。
    /// </summary>
    /// <param name="effectiveLayout">目前實際生效的 Face 鍵配置模式。</param>
    /// <param name="confirmLabel">用於顯示「確認」動作的控制器標示。</param>
    /// <param name="cancelLabel">用於顯示「取消」動作的控制器標示。</param>
    /// <param name="deleteLabel">用於顯示「刪除」動作的控制器標示。</param>
    /// <param name="menuLabel">用於顯示「開啟選單」動作的控制器標示。</param>
    /// <param name="primaryMnemonic">主要動作按鈕對應的助記詞。</param>
    /// <param name="cancelMnemonic">取消動作按鈕對應的助記詞。</param>
    /// <param name="deleteMnemonic">刪除動作按鈕對應的助記詞。</param>
    /// <param name="menuMnemonic">選單動作按鈕對應的助記詞。</param>
    public GamepadFaceButtonProfile(
        AppSettings.GamepadFaceButtonMode effectiveLayout,
        string confirmLabel,
        string cancelLabel,
        string deleteLabel,
        string menuLabel,
        char primaryMnemonic,
        char cancelMnemonic,
        char deleteMnemonic,
        char menuMnemonic)
    {
        EffectiveLayout = effectiveLayout;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        DeleteLabel = deleteLabel;
        MenuLabel = menuLabel;
        PrimaryMnemonic = primaryMnemonic;
        CancelMnemonic = cancelMnemonic;
        DeleteMnemonic = deleteMnemonic;
        MenuMnemonic = menuMnemonic;
    }

    /// <summary>
    /// 目前實際生效的 Face 鍵配置模式。
    /// </summary>
    public AppSettings.GamepadFaceButtonMode EffectiveLayout { get; init; }

    /// <summary>
    /// 目前配置下，「確認」動作使用的按鍵標示。
    /// </summary>
    public string ConfirmLabel { get; init; }

    /// <summary>
    /// 目前配置下，「取消」動作使用的按鍵標示。
    /// </summary>
    public string CancelLabel { get; init; }

    /// <summary>
    /// 目前配置下，「刪除」動作使用的按鍵標示。
    /// </summary>
    public string DeleteLabel { get; init; }

    /// <summary>
    /// 目前配置下，「選單」動作使用的按鍵標示。
    /// </summary>
    public string MenuLabel { get; init; }

    /// <summary>
    /// 目前配置下，主要動作按鈕使用的助記詞。
    /// </summary>
    public char PrimaryMnemonic { get; init; }

    /// <summary>
    /// 目前配置下，取消動作按鈕使用的助記詞。
    /// </summary>
    public char CancelMnemonic { get; init; }

    /// <summary>
    /// 目前配置下，刪除動作按鈕使用的助記詞。
    /// </summary>
    public char DeleteMnemonic { get; init; }

    /// <summary>
    /// 目前配置下，選單動作按鈕使用的助記詞。
    /// </summary>
    public char MenuMnemonic { get; init; }

    /// <summary>
    /// 判斷目前配置是否需要直接顯示 PlayStation 的圖示型按鍵標示。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShouldShowFaceButtonLabel =>
        EffectiveLayout == AppSettings.GamepadFaceButtonMode.PlayStationTraditional ||
        EffectiveLayout == AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm;

    /// <summary>
    /// 依目前設定與執行期偵測資訊取得實際生效的 Face 鍵配置模式。
    /// </summary>
    /// <param name="selectedMode">使用者指定的配置模式。</param>
    /// <param name="provider">目前使用中的控制器提供者。</param>
    /// <param name="deviceName">執行期偵測到的裝置名稱。</param>
    /// <param name="deviceIdentity">執行期偵測到的穩定裝置識別資訊，例如 VID/PID 或產品家族名稱。</param>
    /// <returns>實際生效的配置模式。</returns>
    public static AppSettings.GamepadFaceButtonMode ResolveEffectiveLayout(
        AppSettings.GamepadFaceButtonMode selectedMode,
        AppSettings.GamepadProvider provider,
        string? deviceName,
        string? deviceIdentity = null)
    {
        if (selectedMode != AppSettings.GamepadFaceButtonMode.Auto)
        {
            return selectedMode;
        }

        if (provider != AppSettings.GamepadProvider.GameInput)
        {
            return AppSettings.GamepadFaceButtonMode.Xbox;
        }

        // 先看較穩定的硬體識別資訊，例如 VID/PID 與產品家族名稱。
        string normalizedIdentity = NormalizeDeviceIdentity(deviceIdentity);

        if (TryResolveLayoutFromStableIdentity(normalizedIdentity, out AppSettings.GamepadFaceButtonMode resolvedFromIdentity))
        {
            return resolvedFromIdentity;
        }

        // 若上游尚未提供獨立識別資訊，則退回清理後的裝置名稱再判斷一次。
        string normalizedName = NormalizeDeviceIdentity(deviceName);

        if (TryResolveLayoutFromStableIdentity(normalizedName, out AppSettings.GamepadFaceButtonMode resolvedFromName))
        {
            return resolvedFromName;
        }

        // 最後保留原本較寬鬆的名稱關鍵字比對當成備援。
        string fallbackName = deviceName?.Trim() ?? string.Empty;

        if (ContainsAny(fallbackName,
                "Sony",
                "PlayStation",
                "DualSense",
                "DualShock"))
        {
            return AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm;
        }

        if (ContainsAny(fallbackName,
                "Nintendo",
                "Switch",
                "Joy-Con",
                "JoyCon",
                "Pro Controller"))
        {
            return AppSettings.GamepadFaceButtonMode.Nintendo;
        }

        return AppSettings.GamepadFaceButtonMode.Xbox;
    }

    /// <summary>
    /// 取得目前應用程式設定下的 Face 鍵配置描述。
    /// </summary>
    /// <returns>Face 鍵配置描述。</returns>
    public static GamepadFaceButtonProfile GetActiveProfile()
    {
        AppSettings current = AppSettings.Current;

        return GetProfile(ResolveEffectiveLayout(
            current.GamepadFaceButtonModeType,
            current.GamepadProviderType,
            current.RuntimeDetectedGamepadDeviceName,
            current.RuntimeDetectedGamepadDeviceIdentity));
    }

    /// <summary>
    /// 依指定模式取得對應的 Face 鍵配置描述。
    /// </summary>
    /// <param name="mode">要解析的模式。</param>
    /// <returns>對應模式的配置描述。</returns>
    public static GamepadFaceButtonProfile GetProfile(AppSettings.GamepadFaceButtonMode mode)
    {
        // Auto 模式在這裡只作為後備值處理；真正的自動解析由 ResolveEffectiveLayout 負責。
        AppSettings.GamepadFaceButtonMode effectiveMode = mode == AppSettings.GamepadFaceButtonMode.Auto ?
            AppSettings.GamepadFaceButtonMode.Xbox :
            mode;

        return effectiveMode switch
        {
            AppSettings.GamepadFaceButtonMode.PlayStationTraditional => new(
                effectiveMode,
                confirmLabel: "○",
                cancelLabel: "×",
                deleteLabel: "□",
                menuLabel: "△",
                primaryMnemonic: 'B',
                cancelMnemonic: 'A',
                deleteMnemonic: 'X',
                menuMnemonic: 'Y'),
            AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm => new(
                effectiveMode,
                confirmLabel: "×",
                cancelLabel: "○",
                deleteLabel: "□",
                menuLabel: "△",
                primaryMnemonic: 'A',
                cancelMnemonic: 'B',
                deleteMnemonic: 'X',
                menuMnemonic: 'Y'),
            AppSettings.GamepadFaceButtonMode.Nintendo => new(
                effectiveMode,
                confirmLabel: "A",
                cancelLabel: "B",
                deleteLabel: "Y",
                menuLabel: "X",
                primaryMnemonic: 'A',
                cancelMnemonic: 'B',
                deleteMnemonic: 'Y',
                menuMnemonic: 'X'),
            _ => new(
                AppSettings.GamepadFaceButtonMode.Xbox,
                confirmLabel: "A",
                cancelLabel: "B",
                deleteLabel: "X",
                menuLabel: "Y",
                primaryMnemonic: 'A',
                cancelMnemonic: 'B',
                deleteMnemonic: 'X',
                menuMnemonic: 'Y'),
        };
    }

    /// <summary>
    /// 取得目前實際生效的 Face 鍵配置模式。
    /// </summary>
    /// <returns>目前生效的模式。</returns>
    public static AppSettings.GamepadFaceButtonMode GetActiveEffectiveLayout()
    {
        AppSettings current = AppSettings.Current;

        return ResolveEffectiveLayout(
            current.GamepadFaceButtonModeType,
            current.GamepadProviderType,
            current.RuntimeDetectedGamepadDeviceName,
            current.RuntimeDetectedGamepadDeviceIdentity);
    }

    /// <summary>
    /// 取得 Face 鍵配置選單中應顯示為已勾選的模式。
    /// </summary>
    /// <param name="selectedMode">使用者選取的模式。</param>
    /// <param name="provider">目前的控制器提供者。</param>
    /// <param name="deviceName">執行期偵測到的裝置名稱。</param>
    /// <param name="deviceIdentity">執行期偵測到的穩定裝置識別資訊。</param>
    /// <returns>應在選單中顯示為目前作用中的模式。</returns>
    public static AppSettings.GamepadFaceButtonMode GetMenuCheckedMode(
        AppSettings.GamepadFaceButtonMode selectedMode,
        AppSettings.GamepadProvider provider,
        string? deviceName,
        string? deviceIdentity = null)
        => ResolveEffectiveLayout(selectedMode, provider, deviceName, deviceIdentity);

    /// <summary>
    /// 取得目前應用程式設定下，Face 鍵配置選單中應顯示為已勾選的模式。
    /// </summary>
    /// <returns>應在選單中顯示為目前作用中的模式。</returns>
    public static AppSettings.GamepadFaceButtonMode GetActiveMenuCheckedMode()
    {
        AppSettings current = AppSettings.Current;

        return GetMenuCheckedMode(
            current.GamepadFaceButtonModeType,
            current.GamepadProviderType,
            current.RuntimeDetectedGamepadDeviceName,
            current.RuntimeDetectedGamepadDeviceIdentity);
    }

    /// <summary>
    /// 取得 Face 鍵配置子選單的顯示標題。
    /// </summary>
    /// <returns>子選單標題。</returns>
    public static string GetLayoutMenuTitle()
        => GetResourceString("Settings_GamepadFaceButtonLayout", "Face Button Layout");

    /// <summary>
    /// 取得包含目前生效配置名稱的 Face 鍵子選單標題。
    /// </summary>
    /// <returns>含狀態資訊的子選單標題。</returns>
    public static string GetLayoutMenuTitleWithStatus()
        => string.Format(
            GetResourceString("Settings_GamepadFaceButtonLayout_TitleWithStatus", "{0} [{1}]"),
            GetLayoutMenuTitle(),
            GetFriendlyModeName(GetActiveEffectiveLayout()));

    /// <summary>
    /// 取得顯示用的模式名稱。
    /// </summary>
    /// <param name="mode">模式。</param>
    /// <returns>顯示名稱。</returns>
    public static string GetFriendlyModeName(AppSettings.GamepadFaceButtonMode mode)
    {
        return mode switch
        {
            AppSettings.GamepadFaceButtonMode.Auto => GetResourceString("Settings_GamepadFaceButtonMode_Auto", "Auto"),
            AppSettings.GamepadFaceButtonMode.PlayStationTraditional => GetResourceString("Settings_GamepadFaceButtonMode_PlayStation", "PlayStation (○ Confirm)"),
            AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm => GetResourceString("Settings_GamepadFaceButtonMode_PlayStationCrossConfirm", "PlayStation (× Confirm)"),
            AppSettings.GamepadFaceButtonMode.Nintendo => GetResourceString("Settings_GamepadFaceButtonMode_Nintendo", "Nintendo"),
            _ => GetResourceString("Settings_GamepadFaceButtonMode_Xbox", "Xbox"),
        };
    }

    /// <summary>
    /// 取得目前 Face 鍵配置的 UI 狀態摘要。
    /// </summary>
    /// <param name="selectedMode">使用者選取的模式。</param>
    /// <param name="provider">目前的控制器提供者。</param>
    /// <param name="deviceName">執行期偵測到的裝置名稱。</param>
    /// <param name="deviceIdentity">執行期偵測到的穩定裝置識別資訊。</param>
    /// <returns>顯示於 UI 的狀態摘要。</returns>
    public static string GetLayoutStatusSummary(
        AppSettings.GamepadFaceButtonMode selectedMode,
        AppSettings.GamepadProvider provider,
        string? deviceName,
        string? deviceIdentity = null)
    {
        AppSettings.GamepadFaceButtonMode effectiveMode = ResolveEffectiveLayout(selectedMode, provider, deviceName, deviceIdentity);
        string effectiveName = GetFriendlyModeName(effectiveMode);

        return selectedMode == AppSettings.GamepadFaceButtonMode.Auto ?
            string.Format(
                GetResourceString("Settings_GamepadFaceButtonLayout_Current_Auto", "Current: {0} ({1})"),
                effectiveName,
                GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.Auto)) :
            string.Format(
                GetResourceString("Settings_GamepadFaceButtonLayout_Current_Manual", "Current: {0}"),
                effectiveName);
    }

    /// <summary>
    /// 取得目前應用程式設定下的 Face 鍵配置狀態摘要。
    /// </summary>
    /// <returns>顯示於 UI 的狀態摘要。</returns>
    public static string GetActiveLayoutStatusSummary()
    {
        AppSettings current = AppSettings.Current;

        return GetLayoutStatusSummary(
            current.GamepadFaceButtonModeType,
            current.GamepadProviderType,
            current.RuntimeDetectedGamepadDeviceName,
            current.RuntimeDetectedGamepadDeviceIdentity);
    }

    /// <summary>
    /// 取得主畫面標題列使用的控制器模式提示。
    /// </summary>
    /// <param name="selectedMode">使用者選取的模式。</param>
    /// <param name="provider">目前的控制器提供者。</param>
    /// <param name="deviceName">執行期偵測到的裝置名稱。</param>
    /// <param name="deviceIdentity">執行期偵測到的穩定裝置識別資訊。</param>
    /// <returns>簡短的標題列提示文字。</returns>
    public static string GetTitleLayoutHint(
        AppSettings.GamepadFaceButtonMode selectedMode,
        AppSettings.GamepadProvider provider,
        string? deviceName,
        string? deviceIdentity = null)
    {
        AppSettings.GamepadFaceButtonMode effectiveMode = ResolveEffectiveLayout(selectedMode, provider, deviceName, deviceIdentity);
        string effectiveName = GetFriendlyModeName(effectiveMode);

        return selectedMode == AppSettings.GamepadFaceButtonMode.Auto ?
            string.Format(
                GetResourceString("App_GamepadLayout_Auto_Suffix", "[Face Buttons: {0} → {1}]"),
                GetFriendlyModeName(AppSettings.GamepadFaceButtonMode.Auto),
                effectiveName) :
            string.Format(
                GetResourceString("App_GamepadLayout_Suffix", "[Face Buttons: {0}]"),
                effectiveName);
    }

    /// <summary>
    /// 取得目前應用程式設定下，主畫面標題列使用的控制器模式提示。
    /// </summary>
    /// <returns>簡短的標題列提示文字。</returns>
    public static string GetActiveTitleLayoutHint()
    {
        AppSettings current = AppSettings.Current;

        return GetTitleLayoutHint(
            current.GamepadFaceButtonModeType,
            current.GamepadProviderType,
            current.RuntimeDetectedGamepadDeviceName,
            current.RuntimeDetectedGamepadDeviceIdentity);
    }

    /// <summary>
    /// 取得目前 Face 鍵配置已套用的無障礙提示文字。
    /// </summary>
    /// <param name="mode">目前生效的模式。</param>
    /// <returns>播報用的完整句子。</returns>
    public static string GetLayoutAppliedAnnouncement(AppSettings.GamepadFaceButtonMode mode)
        => string.Format(
            GetResourceString("A11y_Gamepad_Profile_Applied", "{0} layout enabled."),
            GetFriendlyModeName(mode));

    /// <summary>
    /// 取得主畫面的控制器操作說明，並依目前配置模式動態帶入對應的 Face 鍵標示。
    /// </summary>
    /// <returns>主畫面的完整操作說明。</returns>
    public static string GetActiveMainFormDescription()
        => BuildMainFormDescription(GetActiveProfile());

    /// <summary>
    /// 依指定模式取得主畫面的控制器操作說明。
    /// </summary>
    /// <param name="mode">要使用的配置模式。</param>
    /// <returns>主畫面的完整操作說明。</returns>
    public static string GetMainFormDescription(AppSettings.GamepadFaceButtonMode mode)
        => BuildMainFormDescription(GetProfile(mode));

    /// <summary>
    /// 取得目前配置下「確認」動作的按鈕文字（含控制器標示與助記詞）。
    /// </summary>
    /// <param name="text">原始按鈕文字。</param>
    /// <returns>格式化後的按鈕文字。</returns>
    public string FormatConfirmButtonText(string text)
        => FormatLabeledButtonText(text, ConfirmLabel, PrimaryMnemonic);

    /// <summary>
    /// 取得目前配置下「取消」動作的按鈕文字（含控制器標示與助記詞）。
    /// </summary>
    /// <param name="text">原始按鈕文字。</param>
    /// <returns>格式化後的按鈕文字。</returns>
    public string FormatCancelButtonText(string text)
        => FormatLabeledButtonText(text, CancelLabel, CancelMnemonic);

    /// <summary>
    /// 取得目前配置下「刪除」動作的按鈕文字（含控制器標示與助記詞）。
    /// </summary>
    /// <param name="text">原始按鈕文字。</param>
    /// <returns>格式化後的按鈕文字。</returns>
    public string FormatDeleteButtonText(string text)
        => FormatLabeledButtonText(text, DeleteLabel, DeleteMnemonic);

    /// <summary>
    /// 取得目前配置下「選單」動作的按鈕文字（含控制器標示與助記詞）。
    /// </summary>
    /// <param name="text">原始按鈕文字。</param>
    /// <returns>格式化後的按鈕文字。</returns>
    public string FormatMenuButtonText(string text)
        => FormatLabeledButtonText(text, MenuLabel, MenuMnemonic);

    /// <summary>
    /// 取得目前配置下「確認」動作的提示文字，供對話框或說明標籤顯示。
    /// </summary>
    /// <param name="text">原始動作文字。</param>
    /// <returns>格式化後的提示文字。</returns>
    public string FormatPrimaryActionHintText(string text)
        => FormatHintText(text, ConfirmLabel, PrimaryMnemonic);

    /// <summary>
    /// 取得目前配置下「取消」動作的提示文字，供對話框或說明標籤顯示。
    /// </summary>
    /// <param name="text">原始動作文字。</param>
    /// <returns>格式化後的提示文字。</returns>
    public string FormatCancelActionHintText(string text)
        => FormatHintText(text, CancelLabel, CancelMnemonic);

    /// <summary>
    /// 依目前配置重建說明對話框中的控制器對照表左欄標籤。
    /// </summary>
    /// <returns>更新後的 Help rows 文字。</returns>
    public static string BuildHelpRows()
    {
        // 逐列重建控制器操作表，僅覆寫左欄按鍵標示，保留右欄動作說明與在地化內容。
        string[] rows = Strings.Help_Gamepad_Rows.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None);

        if (rows.Length < 8)
        {
            return Strings.Help_Gamepad_Rows;
        }

        GamepadFaceButtonProfile profile = GetActiveProfile();

        rows[0] = ReplaceButtonColumn(rows[0], $"{profile.ConfirmLabel} / Start");
        rows[1] = ReplaceButtonColumn(rows[1], profile.CancelLabel);
        rows[2] = ReplaceButtonColumn(rows[2], $"LB + RB + {profile.CancelLabel}");
        rows[4] = ReplaceButtonColumn(rows[4], profile.MenuLabel);
        rows[5] = ReplaceButtonColumn(rows[5], profile.DeleteLabel);
        rows[6] = ReplaceButtonColumn(rows[6], $"LB + RB + {profile.DeleteLabel}");
        rows[7] = ReplaceButtonColumn(rows[7], $"Back + {profile.DeleteLabel}");

        return string.Join(Environment.NewLine, rows);
    }

    /// <summary>
    /// 判斷目前配置是否由下方鍵負責確認。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ConfirmOnSouth =>
        EffectiveLayout == AppSettings.GamepadFaceButtonMode.Xbox ||
        EffectiveLayout == AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm;

    /// <summary>
    /// 建立含控制器標示的按鈕文字，並附加對應的助記詞。
    /// </summary>
    /// <param name="text">原始文字。</param>
    /// <param name="label">控制器標示。</param>
    /// <param name="mnemonic">助記詞。</param>
    /// <returns>格式化後的文字。</returns>
    private string FormatLabeledButtonText(string text, string label, char mnemonic)
    {
        // 只有 PlayStation 需要額外顯示 ○ / × / □ / △ 等圖示；其他控制器改由助記詞提示即可，避免資訊重複。
        string displayText = ShouldShowFaceButtonLabel && !string.IsNullOrWhiteSpace(label) ?
            $"{label} {text}" :
            text;

        return ControlExtensions.GetMnemonicText(displayText, mnemonic);
    }

    /// <summary>
    /// 建立對話框或說明標籤使用的提示文字，並依語系套用合適的冒號樣式。
    /// </summary>
    /// <param name="text">原始動作文字。</param>
    /// <param name="label">控制器標示。</param>
    /// <param name="mnemonic">助記詞。</param>
    /// <returns>格式化後的提示文字。</returns>
    private string FormatHintText(string text, string label, char mnemonic)
    {
        string displayText = text;

        if (ShouldShowFaceButtonLabel &&
            !string.IsNullOrWhiteSpace(label) &&
            !text.StartsWith(label, StringComparison.Ordinal))
        {
            displayText = $"{label} {text}";
        }

        return ControlExtensions.GetActionHintText(mnemonic, displayText);
    }

    /// <summary>
    /// 取代 Help row 左欄的按鈕顯示字樣，保留右側動作描述不變。
    /// </summary>
    /// <param name="row">原始資料列。</param>
    /// <param name="newButtonText">新的按鈕顯示文字。</param>
    /// <returns>更新後的資料列。</returns>
    private static string ReplaceButtonColumn(string row, string newButtonText)
    {
        int tabIndex = row.IndexOf('\t');

        return tabIndex < 0 ?
            newButtonText :
            $"{newButtonText}{row[tabIndex..]}";
    }

    /// <summary>
    /// 依指定配置產生主畫面的控制器操作說明，確保不同控制器模式的按鍵提示與實際行為一致。
    /// </summary>
    /// <param name="profile">目前使用的 Face 鍵配置描述。</param>
    /// <returns>格式化後的說明文字。</returns>
    private static string BuildMainFormDescription(GamepadFaceButtonProfile profile)
        => string.Format(
            GetResourceString(
                "A11y_MainFormDesc",
                "Press {0} or Start to open the keyboard when the input is empty, or copy text and return when text is entered. Use {1} to backspace and the D-pad to move the cursor or browse history. Press Back to return. Press LB + RB + {2} for quick return. Press LB + RB + {3} to exit. Keyboard: Enter to copy or open the keyboard, Esc to clear, Up/Down for history, Alt + B to return."),
            profile.ConfirmLabel,
            profile.DeleteLabel,
            profile.CancelLabel,
            profile.DeleteLabel);

    /// <summary>
    /// 從資源檔查詢文字，若缺漏則回退到內建預設值。
    /// </summary>
    /// <param name="resourceKey">資源鍵名。</param>
    /// <param name="fallback">缺漏時的預設文字。</param>
    /// <returns>查詢到的文字或預設值。</returns>
    private static string GetResourceString(string resourceKey, string fallback)
    {
        string? value = Strings.ResourceManager.GetString(resourceKey, Strings.Culture);

        return string.IsNullOrWhiteSpace(value) ?
            fallback :
            value;
    }

    /// <summary>
    /// 依穩定識別資訊判斷控制器版型。優先使用廠商代碼與產品家族名稱，避免被 Wireless、Adapter 等傳輸字樣誤導。
    /// </summary>
    /// <param name="deviceIdentity">標準化後的裝置識別資訊。</param>
    /// <param name="mode">解析出的 Face 鍵配置模式。</param>
    /// <returns>若成功辨識則回傳 true。</returns>
    private static bool TryResolveLayoutFromStableIdentity(string deviceIdentity, out AppSettings.GamepadFaceButtonMode mode)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentity))
        {
            mode = AppSettings.GamepadFaceButtonMode.Xbox;
            return false;
        }

        if (ContainsAny(deviceIdentity,
                "VID 054C",
                "SONY",
                "PLAYSTATION",
                "DUALSENSE",
                "DUALSHOCK",
                "PS5",
                "PS4",
                "DS5",
                "DS4"))
        {
            mode = AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm;
            return true;
        }

        if (ContainsAny(deviceIdentity,
                "VID 057E",
                "NINTENDO",
                "SWITCH",
                "JOY CON",
                "JOYCON",
                "PRO CONTROLLER",
                "HAC"))
        {
            mode = AppSettings.GamepadFaceButtonMode.Nintendo;
            return true;
        }

        mode = AppSettings.GamepadFaceButtonMode.Xbox;
        return false;
    }

    /// <summary>
    /// 標準化裝置識別資訊，移除常見的連線/配件雜訊詞，讓比對更接近業界慣用的 VID/PID + 產品家族判斷。
    /// </summary>
    /// <param name="value">原始裝置資訊。</param>
    /// <returns>清理後的可比對字串。</returns>
    private static string NormalizeDeviceIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().ToUpperInvariant();

        foreach (string noise in new[]
        {
            "WIRELESS",
            "BLUETOOTH",
            "ADAPTER",
            "RECEIVER",
            "DONGLE",
            "USB",
            "CONTROLLER",
            "GAMEPAD",
            "FOR WINDOWS",
            "HID COMPLIANT"
        })
        {
            normalized = normalized.Replace(noise, " ", StringComparison.Ordinal);
        }

        foreach (char separator in new[] { '-', '_', '/', '\\', '(', ')', '[', ']', '{', '}', ',', '.', ';', ':' })
        {
            normalized = normalized.Replace(separator, ' ');
        }

        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// 判斷裝置名稱是否包含任一指定關鍵字（不分大小寫）。
    /// </summary>
    /// <param name="text">要比對的裝置名稱。</param>
    /// <param name="keywords">關鍵字集合。</param>
    /// <returns>若包含任一關鍵字則回傳 true。</returns>
    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) &&
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}