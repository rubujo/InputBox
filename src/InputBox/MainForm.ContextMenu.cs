using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Diagnostics;
using System.Media;

namespace InputBox;

// 阻擋設計工具。
partial class DesignerBlocker { };

public partial class MainForm
{
    private const int PhraseMenuPageSizeLarge = 6;
    private const int PhraseMenuPageSizeMedium = 4;
    private const int PhraseMenuPageSizeSmall = 3;
    private const int RecentPhraseLimitLarge = 5;
    private const int RecentPhraseLimitMedium = 3;
    private const int RecentPhraseLimitSmall = 2;

    private int _phraseMenuPage;
    // 僅在分頁按鈕觸發的「下一次」子選單重開時保留目前頁碼。
    // 一般使用者重新開啟子選單時，會回到第 1 頁，符合常見使用習慣。
    private bool _keepPhrasePageOnNextOpen;
    private readonly List<PhraseService.PhraseEntry> _recentPhrases = [];

    /// <summary>
    /// 選單項目中繼資料，支援 A11y 動態描述生成。
    /// </summary>
    /// <param name="Label">標籤文字</param>
    /// <param name="Mnemonic">助記鍵字母</param>
    /// <param name="Min">最小值（選填）</param>
    /// <param name="Max">最大值（選填）</param>
    private sealed record MenuMetadata(
        string Label,
        char Mnemonic,
        decimal? Min = null,
        decimal? Max = null);

    /// <summary>
    /// 初始化右鍵選單
    /// </summary>
    private void InitializeContextMenu()
    {
        _cmsInput ??= new ContextMenuStrip
        {
            AccessibleName = Strings.A11y_ContextMenu_Name,
            AccessibleDescription = Strings.A11y_ContextMenu_Desc
        };

        // 動態加入主題變更重啟提示。
        if (IsThemeUpdatePending)
        {
            ToolStripMenuItem tsmiRestart = new()
            {
                Text = $"{Strings.App_ThemePending_Suffix} {Strings.Menu_ApplyThemeRestart}",
                AccessibleName = Strings.Menu_ApplyThemeRestart
            };
            tsmiRestart.Click += (s, e) =>
            {
                try
                {
                    AskForRestart();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[選單] tsmiRestart.Click 失敗：{ex.Message}");
                }
            };

            _cmsInput.Items.Add(tsmiRestart);
            _cmsInput.Items.Add(new ToolStripSeparator());
        }

        // 隱私模式。
        _tsmiPrivacyMode = new ToolStripMenuItem(ControlExtensions.GetMnemonicText(Strings.Menu_PrivacyMode, 'P'))
        {
            CheckOnClick = true,
            Checked = AppSettings.Current.IsPrivacyMode,
            AccessibleName = Strings.Menu_PrivacyMode,
            AccessibleDescription = Strings.Menu_PrivacyMode_Desc
        };

        _tsmiPrivacyMode.CheckedChanged += (s, e) =>
        {
            try
            {
                AppSettings.Current.IsPrivacyMode = _tsmiPrivacyMode.Checked;
                AppSettings.Save();

                _historyService.IsPrivacyMode = _tsmiPrivacyMode.Checked;

                // 一旦開啟隱私模式，立刻清空先前的歷程記錄，防止誤觸洩漏。
                if (_historyService.IsPrivacyMode)
                {
                    _historyService.Clear();

                    // 清空目前的輸入框。
                    TBInput.Clear();
                }

                // 更新標題快取。
                UpdateTitlePrefix();
                UpdateTitle();

                // 隱私模式狀態變更。
                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                    Strings.A11y_PrivacyMode_On :
                    Strings.A11y_PrivacyMode_Off);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] _tsmiPrivacyMode.CheckedChanged 失敗：{ex.Message}");
            }
        };

        // 允許中斷廣播（WCAG 2.2.4）。
        _tsmiA11yInterrupt = new ToolStripMenuItem(ControlExtensions.GetMnemonicText(Strings.Menu_A11yInterrupt, 'I'))
        {
            CheckOnClick = true,
            Checked = AppSettings.Current.A11yInterruptEnabled,
            AccessibleName = Strings.Menu_A11yInterrupt,
            AccessibleDescription = Strings.Menu_A11yInterrupt_Desc
        };

        _tsmiA11yInterrupt.CheckedChanged += (s, e) =>
        {
            try
            {
                AppSettings.Current.A11yInterruptEnabled = _tsmiA11yInterrupt.Checked;
                AppSettings.Save();

                AnnounceA11y(AppSettings.Current.A11yInterruptEnabled ?
                    Strings.A11y_A11yInterrupt_On :
                    Strings.A11y_A11yInterrupt_Off);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] _tsmiA11yInterrupt.CheckedChanged 失敗：{ex.Message}");
            }
        };

        // 動畫式視覺警示。
        _tsmiAnimatedVisualAlerts = new ToolStripMenuItem(ControlExtensions.GetMnemonicText(Strings.Menu_AnimatedVisualAlerts, 'V'))
        {
            CheckOnClick = true,
            Checked = AppSettings.Current.EnableAnimatedVisualAlerts,
            AccessibleName = Strings.Menu_AnimatedVisualAlerts,
            AccessibleDescription = Strings.Menu_AnimatedVisualAlerts_Desc
        };

        _tsmiAnimatedVisualAlerts.CheckedChanged += (s, e) =>
        {
            try
            {
                AppSettings.Current.EnableAnimatedVisualAlerts = _tsmiAnimatedVisualAlerts.Checked;
                AppSettings.Save();

                AnnounceA11y(AppSettings.Current.EnableAnimatedVisualAlerts ?
                    Strings.A11y_AnimatedVisualAlerts_On :
                    Strings.A11y_AnimatedVisualAlerts_Off);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] _tsmiAnimatedVisualAlerts.CheckedChanged 失敗：{ex.Message}");
            }
        };

        // 快速鍵設定子選單。
        ToolStripMenuItem tsmiHotkeySettings = new(ControlExtensions.GetMnemonicText(Strings.Menu_HotkeySettings, 'T'))
        {
            AccessibleName = Strings.Menu_HotkeySettings,
            AccessibleDescription = Strings.Menu_HotkeySettings_Desc
        };

        // WCAG 2.4.8：進入子選單時宣告層級，提供位置感知。
        tsmiHotkeySettings.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_HotkeySettings));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiHotkeySettings.DropDownOpened 失敗：{ex.Message}");
            }
        };

        // 修飾鍵設定（使用本地函數）。
        // 將 modValue 的型別從 int 改為 User32.KeyModifiers 列舉
        void AddModifierItem(string label, User32.KeyModifiers modValue)
        {
            ToolStripMenuItem item = new(label)
            {
                CheckOnClick = true,
                // 使用 HasFlag讓語意更清晰。
                Checked = AppSettings.Current.HotKeyModifiers.HasFlag(modValue),
                AccessibleName = label,
                // A11y 描述：說明此勾選框的用途。
                AccessibleDescription = string.Format(Strings.A11y_Mod_Toggle_Desc, label)
            };

            item.CheckedChanged += (s, e) =>
            {
                try
                {
                    User32.KeyModifiers oldMods = AppSettings.Current.HotKeyModifiers;

                    if (item.Checked)
                    {
                        // 將該修飾鍵加入組合（位元 OR）。
                        AppSettings.Current.HotKeyModifiers |= modValue;
                    }
                    else
                    {
                        // 將該修飾鍵從組合中移除（位元 AND NOT）。
                        AppSettings.Current.HotKeyModifiers &= ~modValue;
                    }

                    if (!RegisterHotKeyInternal())
                    {
                        // 若註冊失敗，回退設定。
                        AppSettings.Current.HotKeyModifiers = oldMods;

                        item.Checked = oldMods.HasFlag(modValue);

                        // 重新註冊舊的。
                        RegisterHotKeyInternal();
                    }

                    AppSettings.Save();

                    // 更新標題快取。
                    UpdateTitlePrefix();
                    UpdateTitle();

                    // 告知修飾鍵狀態變更。
                    string statusMsg = item.Checked ?
                        string.Format(Strings.A11y_Mod_On, label) :
                        string.Format(Strings.A11y_Mod_Off, label);

                    // 如果目前沒有設定主按鍵，這組快速鍵是不會生效的，需主動提醒。
                    if (AppSettings.Current.HotKeyKey == "None")
                    {
                        statusMsg += $" {Strings.A11y_Mod_Key_Missing}";
                    }

                    AnnounceA11y(statusMsg);
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "右鍵選單處理失敗");

                    Debug.WriteLine($"[選單] 處理失敗：{ex.Message}");
                }
            };

            tsmiHotkeySettings.DropDownItems.Add(item);
        }

        AddModifierItem(Strings.Mod_Ctrl, User32.KeyModifiers.Control);
        AddModifierItem(Strings.Mod_Alt, User32.KeyModifiers.Alt);
        AddModifierItem(Strings.Mod_Shift, User32.KeyModifiers.Shift);
        AddModifierItem(Strings.Mod_Win, User32.KeyModifiers.Win);

        tsmiHotkeySettings.DropDownItems.Add(new ToolStripSeparator());

        // 擷取主要按鍵。
        ToolStripMenuItem tsmiCaptureKey = new(ControlExtensions.GetMnemonicText(Strings.Menu_CaptureKey, 'K'))
        {
            AccessibleName = Strings.Menu_CaptureKey,
            AccessibleDescription = Strings.Menu_CaptureKey_Desc
        };
        tsmiCaptureKey.Click += (s, e) =>
        {
            try
            {
                Interlocked.Exchange(ref _isCapturingHotkey, 1);

                // 標題列提示（統一由 UpdateTitle 處理，確保包含快速鍵資訊）。
                UpdateTitle();

                // 告知進入擷取模式、操作方式及如何取消。
                AnnounceA11y($"{Strings.Msg_PressAnyKey} {Strings.A11y_Capture_Esc_Cancel}");

                // 輸入框視覺強化。
                TBInput.Text = string.Empty;
                TBInput.PlaceholderText = Strings.Msg_PressAnyKey;
                // 暫時唯讀，防止輸入字元。
                TBInput.ReadOnly = true;

                // 更新無障礙名稱與描述。
                // 先快取目前描述，確保退出擷取模式時可對稱還原。
                _tbInputAccessibleDescriptionBeforeCapture = TBInput.AccessibleDescription;
                TBInput.AccessibleName = Strings.Msg_PressAnyKey;
                TBInput.AccessibleDescription = Strings.A11y_Capture_Esc_Cancel;

                // Zero-Jitter：擷取模式不改變 Padding，僅透過顏色提示狀態。
                // 按鈕文字提示與狀態變更。
                BtnCopy.Enabled = false;
                BtnCopy.Text = "...";

                // 邊框顏色變化（兼顧高對比）。
                PInputHost.BackColor = SystemInformation.HighContrast ?
                    SystemColors.HighlightText :
                    Color.Orange;

                // 關閉輸入法。
                TBInput.ImeMode = ImeMode.Disable;

                _cmsInput?.Close();

                // 焦點救援。
                TBInput.Focus();

                // 觸發一次視覺閃爍動畫。
                FlashAlertAsync().SafeFireAndForget();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiCaptureKey.Click 失敗：{ex.Message}");
            }
        };
        tsmiHotkeySettings.DropDownItems.Add(tsmiCaptureKey);

        // 進階設定子選單。
        ToolStripMenuItem tsmiSettings = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings, 'S'))
        {
            AccessibleName = Strings.Menu_Settings,
            AccessibleDescription = Strings.A11y_Menu_Settings_Desc
        };

        // WCAG 2.4.8：進入子選單時宣告層級。
        tsmiSettings.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_Settings));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiSettings.DropDownOpened 失敗：{ex.Message}");
            }
        };

        // 視窗與操作。
        ToolStripMenuItem tsmiWinOps = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Window, 'W'))
        {
            AccessibleName = Strings.Menu_Settings_Window,
            AccessibleDescription = Strings.A11y_Menu_WinOps_Desc
        };

        // WCAG 2.4.8：進入子選單時宣告層級。
        tsmiWinOps.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_Settings_Window));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiWinOps.DropDownOpened 失敗：{ex.Message}");
            }
        };

        // 不透明度。
        ToolStripMenuItem tsmiOpacity = new(ControlExtensions.GetMnemonicText(Strings.Settings_WindowOpacity, 'O'))
        {
            AccessibleName = Strings.Settings_WindowOpacity,
        };

        // WCAG 2.4.8：進入子選單時宣告層級。
        tsmiOpacity.DropDownOpened += (s, e) =>
        {
            try
            {
                tsmiOpacity.AccessibleDescription = string.Format(
                    Strings.A11y_Menu_OpacityDesc,
                    Strings.Settings_WindowOpacity,
                    AppSettings.Current.WindowOpacity);

                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Settings_WindowOpacity));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiOpacity 開啟失敗：{ex.Message}");
            }
        };
        tsmiWinOps.DropDownItems.Add(tsmiOpacity);

        // 設定不透明度數值。
        ToolStripMenuItem tsmiSetOpacity = new(string.Empty)
        {
            AccessibleName = Strings.Settings_WindowOpacity,
            Tag = new MenuMetadata(Strings.Settings_WindowOpacity, 'S', 70, 100)
        };
        tsmiSetOpacity.Click += (s, e) =>
        {
            try
            {
                float? result = AskForFloat(
                    Strings.Settings_WindowOpacity,
                    AppSettings.Current.WindowOpacity * 100,
                    100.0f,
                    70.0f,
                    100.0f,
                    1.0m,
                    0);

                if (result.HasValue)
                {
                    AppSettings.Current.WindowOpacity = result.Value / 100.0f;

                    UpdateOpacity();

                    AppSettings.Save();

                    RefreshMenu();

                    AnnounceA11y(string.Format(Strings.A11y_Opacity_Changed, AppSettings.Current.WindowOpacity));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiSetOpacity.Click 失敗：{ex.Message}");
            }
        };
        tsmiOpacity.DropDownItems.Add(tsmiSetOpacity);

        // 重設不透明度。
        ToolStripMenuItem tsmiResetOpacity = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault,
            AccessibleDescription = Strings.A11y_Btn_SetDefault_Group_Desc
        };
        tsmiResetOpacity.Click += (s, e) =>
        {
            try
            {
                ResetOpacity();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiResetOpacity.Click 失敗：{ex.Message}");
            }
        };
        tsmiOpacity.DropDownItems.Add(tsmiResetOpacity);

        tsmiWinOps.DropDownItems.Add(new ToolStripSeparator());

        /// <summary>
        /// 新增數值設定選單項目
        /// </summary>
        /// <param name="parent">父選單項目</param>
        /// <param name="label">標籤文字</param>
        /// <param name="mnemonic">助記鍵字母</param>
        /// <param name="getter">取得目前值的函式</param>
        /// <param name="setter">設定新值的函式</param>
        /// <param name="defValue">預設值</param>
        /// <param name="min">最小值</param>
        /// <param name="max">最大值</param>
        void AddNumericItem(
            ToolStripMenuItem parent,
            string label,
            char mnemonic,
            Func<int> getter,
            Action<int> setter,
            int defValue,
            int min,
            int max)
        {
            ToolStripMenuItem item = new(string.Empty)
            {
                AccessibleName = label,
                // 將範圍資訊封裝至 Metadata，支援動態 A11y 描述生成。
                Tag = new MenuMetadata(label, mnemonic, min, max),
            };

            item.Click += (s, e) =>
            {
                try
                {
                    int? val = AskForValue(label, getter(), defValue, min, max);

                    if (val.HasValue)
                    {
                        setter(val.Value);

                        AppSettings.Save();

                        RefreshMenu();
                    }

                    // 焦點還原。
                    // 當數值輸入對話框關閉後，將焦點精確還原至原選單項，最佳化螢幕閱讀器導覽流暢度。
                    _lastFocusedMenuItem?.Select();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[選單] {label} 設定失敗：{ex.Message}");
                }
            };

            parent.DropDownItems.Add(item);
        }

        AddNumericItem(
            tsmiWinOps,
            Strings.Settings_WindowRestoreDelay,
            'R',
            () => AppSettings.Current.WindowRestoreDelay,
            v => AppSettings.Current.WindowRestoreDelay = v,
            50, 0, 5000);
        AddNumericItem(
            tsmiWinOps,
            Strings.Settings_ClipboardRetryDelay,
            'C',
            () => AppSettings.Current.ClipboardRetryDelay,
            v => AppSettings.Current.ClipboardRetryDelay = v,
            20, 0, 1000);
        AddNumericItem(
            tsmiWinOps,
            Strings.Settings_TouchKeyboardDismissDelay,
            'T',
            () => AppSettings.Current.TouchKeyboardDismissDelay,
            v => AppSettings.Current.TouchKeyboardDismissDelay = v,
            300, 0, 5000);
        AddNumericItem(
            tsmiWinOps,
            Strings.Settings_WindowSwitchBufferBase,
            'B',
            () => AppSettings.Current.WindowSwitchBufferBase,
            v => AppSettings.Current.WindowSwitchBufferBase = v,
            150, 0, 5000);
        AddNumericItem(
            tsmiWinOps,
            Strings.Settings_InputJitterRange,
            'J',
            () => AppSettings.Current.InputJitterRange,
            v => AppSettings.Current.InputJitterRange = v,
            50, 0, 1000);

        tsmiWinOps.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem tsmiResetWinOps = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault,
            AccessibleDescription = Strings.A11y_Btn_SetDefault_Group_Desc
        };
        tsmiResetWinOps.Click += (s, e) =>
        {
            try
            {
                AppSettings.Current.WindowRestoreDelay = 50;
                AppSettings.Current.ClipboardRetryDelay = 20;
                AppSettings.Current.TouchKeyboardDismissDelay = 300;
                AppSettings.Current.WindowSwitchBufferBase = 150;
                AppSettings.Current.InputJitterRange = 50;
                AppSettings.Save();

                RefreshMenu();

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                // 告知視窗設定已重置。
                AnnounceA11y(Strings.Msg_InputCleared);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiResetWinOps.Click 失敗：{ex.Message}");
            }
        };
        tsmiWinOps.DropDownItems.Add(tsmiResetWinOps);

        tsmiSettings.DropDownItems.Add(tsmiWinOps);

        // 回饋。
        ToolStripMenuItem tsmiFeedback = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Feedback, 'F'))
        {
            AccessibleName = Strings.Menu_Settings_Feedback,
            AccessibleDescription = Strings.A11y_Menu_Feedback_Desc
        };

        // WCAG 2.4.8：進入子選單時宣告層級。
        tsmiFeedback.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_Settings_Feedback));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiFeedback.DropDownOpened 失敗：{ex.Message}");
            }
        };
        ToolStripMenuItem tsmiVibEnable = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Vibration, 'V'))
        {
            CheckOnClick = true,
            Checked = AppSettings.Current.EnableVibration,
            AccessibleName = Strings.Menu_Settings_Vibration,
            AccessibleDescription = Strings.A11y_Menu_VibEnable_Desc
        };
        tsmiVibEnable.CheckedChanged += (s, e) =>
        {
            try
            {
                AppSettings.Current.EnableVibration = tsmiVibEnable.Checked;
                AppSettings.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiVibEnable.CheckedChanged 失敗：{ex.Message}");
            }
        };
        tsmiFeedback.DropDownItems.Add(tsmiVibEnable);

        ToolStripMenuItem tsmiIntensity = new(string.Empty)
        {
            AccessibleName = Strings.Settings_VibrationIntensity,
            Tag = new MenuMetadata(Strings.Settings_VibrationIntensity, 'I', 0, 1)
        };
        tsmiIntensity.Click += (s, e) =>
        {
            try
            {
                float? val = AskForFloat(
                    Strings.Settings_VibrationIntensity,
                    AppSettings.Current.VibrationIntensity,
                    0.7f,
                    0.0f,
                    1.0f,
                    0.05m,
                    2);

                if (val.HasValue)
                {
                    AppSettings.Current.VibrationIntensity = val.Value;

                    // 即時更新靜態震動強度倍率，不需重啟。
                    VibrationPatterns.GlobalIntensityMultiplier = val.Value;

                    AppSettings.Save();

                    RefreshMenu();

                    // 告知強度已更新。
                    AnnounceA11y($"{Strings.Settings_VibrationIntensity}: {val.Value}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiIntensity.Click 失敗：{ex.Message}");
            }
        };
        tsmiFeedback.DropDownItems.Add(tsmiIntensity);

        tsmiFeedback.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem tsmiResetFeedback = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault,
            AccessibleDescription = Strings.A11y_Btn_SetDefault_Group_Desc
        };
        tsmiResetFeedback.Click += (s, e) =>
        {
            try
            {
                AppSettings.Current.EnableVibration = true;
                AppSettings.Current.VibrationIntensity = 0.7f;

                VibrationPatterns.GlobalIntensityMultiplier = 0.7f;

                AppSettings.Save();

                tsmiVibEnable.Checked = true;

                RefreshMenu();

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                // 告知回饋設定已重置。
                AnnounceA11y(Strings.Msg_InputCleared);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiResetFeedback.Click 失敗：{ex.Message}");
            }
        };
        tsmiFeedback.DropDownItems.Add(tsmiResetFeedback);

        tsmiSettings.DropDownItems.Add(tsmiFeedback);

        //　控制器。
        ToolStripMenuItem tsmiGamepad = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Gamepad, 'G'))
        {
            AccessibleName = Strings.Menu_Settings_Gamepad,
            AccessibleDescription = Strings.A11y_Menu_Gamepad_Desc
        };

        // WCAG 2.4.8：進入子選單時宣告層級。
        tsmiGamepad.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_Settings_Gamepad));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiGamepad.DropDownOpened 失敗：{ex.Message}");
            }
        };

        // Provider（需重啟）。
        ToolStripMenuItem tsmiProvider = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Provider, 'P'))
        {
            AccessibleName = Strings.Menu_Settings_Provider,
            AccessibleDescription = Strings.A11y_Menu_Provider_Desc
        };

        // WCAG 2.4.8：進入子選單時宣告層級。
        tsmiProvider.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_Settings_Provider));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiProvider.DropDownOpened 失敗：{ex.Message}");
            }
        };

        void AddProviderItem(AppSettings.GamepadProvider provider)
        {
            ToolStripMenuItem item = new(provider.ToString())
            {
                Checked = AppSettings.Current.GamepadProviderType == provider,
                AccessibleName = provider.ToString(),
                // 為個別 API 提供者加入具體功能描述。
                AccessibleDescription = provider == AppSettings.GamepadProvider.GameInput ?
                    Strings.A11y_Menu_Provider_GameInput_Desc :
                    Strings.A11y_Menu_Provider_XInput_Desc
            };

            item.Click += (s, e) =>
            {
                try
                {
                    if (AppSettings.Current.GamepadProviderType != provider)
                    {
                        AppSettings.Current.GamepadProviderType = provider;
                        AppSettings.Save();

                        AnnounceA11y(Strings.Msg_RestartRequired);

                        AskForRestart();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[選單] {provider} 選取失敗：{ex.Message}");
                }
            };

            tsmiProvider.DropDownItems.Add(item);
        }

        AddProviderItem(AppSettings.GamepadProvider.XInput);
        AddProviderItem(AppSettings.GamepadProvider.GameInput);

        tsmiProvider.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem tsmiResetProvider = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault,
            AccessibleDescription = Strings.A11y_Btn_SetDefault_Group_Desc
        };
        tsmiResetProvider.Click += (s, e) =>
        {
            try
            {
                if (AppSettings.Current.GamepadProviderType != AppSettings.GamepadProvider.XInput)
                {
                    AppSettings.Current.GamepadProviderType = AppSettings.GamepadProvider.XInput;
                    AppSettings.Save();

                    AnnounceA11y(Strings.Msg_RestartRequired);

                    AskForRestart();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiResetProvider.Click 失敗：{ex.Message}");
            }
        };
        tsmiProvider.DropDownItems.Add(tsmiResetProvider);

        tsmiGamepad.DropDownItems.Add(tsmiProvider);
        tsmiGamepad.DropDownItems.Add(new ToolStripSeparator());

        // Deadzone & Repeat。
        AddNumericItem(
            tsmiGamepad,
            Strings.Settings_ThumbDeadzoneEnter,
            'E',
            () => AppSettings.Current.ThumbDeadzoneEnter,
            v =>
            {
                AppSettings.Current.ThumbDeadzoneEnter = v;
            },
            7849,
            0,
            30000);
        AddNumericItem(
            tsmiGamepad,
            Strings.Settings_ThumbDeadzoneExit,
            'Q',
            () => AppSettings.Current.ThumbDeadzoneExit,
            v =>
            {
                AppSettings.Current.ThumbDeadzoneExit = v;
            },
            2500,
            0,
            30000);
        AddNumericItem(
            tsmiGamepad,
            Strings.Settings_RepeatDelay,
            'D',
            () => AppSettings.Current.RepeatInitialDelayFrames,
            v =>
            {
                AppSettings.Current.RepeatInitialDelayFrames = v;
            },
            30,
            1,
            300);
        AddNumericItem(
            tsmiGamepad,
            Strings.Settings_RepeatSpeed,
            'S',
            () => AppSettings.Current.RepeatIntervalFrames,
            v =>
            {
                AppSettings.Current.RepeatIntervalFrames = v;
            },
            5,
            1,
            100);

        tsmiGamepad.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem tsmiResetGamepad = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault,
            AccessibleDescription = Strings.A11y_Btn_SetDefault_Group_Desc
        };
        tsmiResetGamepad.Click += (s, e) =>
        {
            try
            {
                AppSettings.Current.ThumbDeadzoneEnter = 7849;
                AppSettings.Current.ThumbDeadzoneExit = 2500;
                AppSettings.Current.RepeatInitialDelayFrames = 30;
                AppSettings.Current.RepeatIntervalFrames = 5;
                AppSettings.Save();

                RefreshMenu();

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                // 告知控制器設定已重置。
                AnnounceA11y(Strings.Msg_InputCleared);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiResetGamepad.Click 失敗：{ex.Message}");
            }
        };
        tsmiGamepad.DropDownItems.Add(tsmiResetGamepad);

        tsmiSettings.DropDownItems.Add(tsmiGamepad);
        tsmiSettings.DropDownItems.Add(new ToolStripSeparator());

        // 歷程容量（需重啟）。
        ToolStripMenuItem tsmiCap = new(string.Empty)
        {
            AccessibleName = Strings.Settings_HistoryCapacity,
            Tag = new MenuMetadata(Strings.Settings_HistoryCapacity, 'H', 1, 1000)
        };
        tsmiCap.Click += (s, e) =>
        {
            try
            {
                int? val = AskForValue(Strings.Settings_HistoryCapacity, AppSettings.Current.HistoryCapacity, 100, 1, 1000);

                if (val.HasValue &&
                    val != AppSettings.Current.HistoryCapacity)
                {
                    AppSettings.Current.HistoryCapacity = val.Value;
                    AppSettings.Save();

                    RefreshMenu();

                    AskForRestart();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiCap.Click 失敗：{ex.Message}");
            }
        };
        tsmiSettings.DropDownItems.Add(tsmiCap);

        tsmiSettings.DropDownItems.Add(new ToolStripSeparator());

        // 開啟資料夾。
        ToolStripMenuItem tsmiOpenDataFolder = new(ControlExtensions.GetMnemonicText(Strings.Menu_OpenDataFolder, 'O'))
        {
            AccessibleName = Strings.Menu_OpenDataFolder,
            AccessibleDescription = Strings.Menu_OpenDataFolder_Desc
        };
        tsmiOpenDataFolder.Click += (s, e) =>
        {
            try
            {
                if (Directory.Exists(AppSettings.ConfigDirectory))
                {
                    Process.Start(new ProcessStartInfo(AppSettings.ConfigDirectory)
                    {
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiOpenDataFolder.Click 失敗：{ex.Message}");
            }
        };
        tsmiSettings.DropDownItems.Add(tsmiOpenDataFolder);

        // 清除歷程。
        ToolStripMenuItem tsmiClearHistory = new(ControlExtensions.GetMnemonicText(Strings.Menu_ClearHistory, 'C'))
        {
            AccessibleName = Strings.Menu_ClearHistory,
            AccessibleDescription = Strings.Menu_ClearHistory_Desc
        };
        tsmiClearHistory.Click += (s, e) =>
        {
            try
            {
                _historyService?.Clear();

                // 清除後主動將焦點拉回輸入框，確保使用者能直接開始輸入。
                TBInput.Focus();

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                AnnounceA11y(Strings.Msg_InputCleared);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiClearHistory.Click 失敗：{ex.Message}");
            }
        };

        // 離開。
        ToolStripMenuItem tsmiExit = new(ControlExtensions.GetMnemonicText(Strings.Menu_Exit, 'X'))
        {
            AccessibleName = Strings.Menu_Exit,
            AccessibleDescription = Strings.A11y_Menu_Exit_Desc
        };
        tsmiExit.Click += (s, e) =>
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiExit.Click 失敗：{ex.Message}");
            }
        };

        // 說明（WCAG 3.3.5）。
        ToolStripMenuItem tsmiHelp = new(ControlExtensions.GetMnemonicText(Strings.Menu_Help, 'H'))
        {
            AccessibleName = Strings.Menu_Help,
            AccessibleDescription = Strings.Menu_Help_Desc
        };
        tsmiHelp.Click += (s, e) =>
        {
            try
            {
                ShowHelpDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiHelp.Click 失敗：{ex.Message}");
            }
        };

        // 使用共享快取取得選單字型。
        _cmsInput.Font = GetSharedA11yFont(DeviceDpi);

        // 片語子選單。
        _tsmiPhrases = new ToolStripMenuItem(ControlExtensions.GetMnemonicText(Strings.Menu_Phrases, 'F'))
        {
            AccessibleName = Strings.Menu_Phrases
        };

        // 新增佔位項確保 HasDropDownItems 為 true，使 WinForms 將此項目視為子選單父項。
        // 沒有此佔位項時，控制器因 HasDropDownItems == false 而不會呼叫 ShowDropDown()，
        // 滑鼠也不會顯示子選單箭頭。DropDownOpening 會在展開前清除並重建所有項目。
        _tsmiPhrases.DropDownItems.Add(new ToolStripMenuItem(Strings.Menu_PhraseEmpty) { Enabled = false });

        _tsmiPhrases.DropDownOpening += (s, e) =>
        {
            try
            {
                if (_keepPhrasePageOnNextOpen)
                {
                    // 分頁按鈕觸發的重開：沿用目前頁碼，並在本次後自動清除旗標。
                    _keepPhrasePageOnNextOpen = false;
                }
                else
                {
                    // 一般開啟片語子選單：重置回第 1 頁（index = 0）。
                    _phraseMenuPage = 0;
                }

                RebuildPhraseMenuItems();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] _tsmiPhrases.DropDownOpening 失敗：{ex.Message}");
            }
        };

        _tsmiPhrases.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnounceA11y(string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_Phrases));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] _tsmiPhrases.DropDownOpened 失敗：{ex.Message}");
            }
        };

        _cmsInput.Items.Add(_tsmiPrivacyMode);
        _cmsInput.Items.Add(_tsmiA11yInterrupt);
        _cmsInput.Items.Add(_tsmiAnimatedVisualAlerts);
        _cmsInput.Items.Add(new ToolStripSeparator());
        _cmsInput.Items.Add(_tsmiPhrases);
        _cmsInput.Items.Add(new ToolStripSeparator());
        _cmsInput.Items.Add(tsmiHotkeySettings);
        _cmsInput.Items.Add(tsmiSettings);
        _cmsInput.Items.Add(new ToolStripSeparator());
        _cmsInput.Items.Add(tsmiClearHistory);
        _cmsInput.Items.Add(new ToolStripSeparator());
        _cmsInput.Items.Add(tsmiHelp);
        _cmsInput.Items.Add(new ToolStripSeparator());
        _cmsInput.Items.Add(tsmiExit);

        // 綁定選單至容器控制項，確保 TBInput 能保留其原始的 Windows 右鍵選單（剪下、複製、貼上）。
        PInputHost.ContextMenuStrip = _cmsInput;
        TLPHost.ContextMenuStrip = _cmsInput;
    }

    /// <summary>
    /// 重新整理整個右鍵選單的標籤文字與 A11y 描述。
    /// </summary>
    public void RefreshMenu()
    {
        this.SafeInvoke(() =>
        {
            if (_cmsInput != null)
            {
                // 在重新整理時，若有待處理變更則動態注入重啟選項。
                if (IsThemeUpdatePending &&
                    !_cmsInput.Items.Cast<ToolStripItem>().Any(n => n.AccessibleName == Strings.Menu_ApplyThemeRestart))
                {
                    ToolStripMenuItem tsmiRestart = new()
                    {
                        Text = $"{Strings.App_ThemePending_Suffix} {Strings.Menu_ApplyThemeRestart}",
                        AccessibleName = Strings.Menu_ApplyThemeRestart
                    };
                    tsmiRestart.Click += (s, e) => AskForRestart();

                    // 插入選單最前端。
                    _cmsInput.Items.Insert(0, tsmiRestart);
                    _cmsInput.Items.Insert(1, new ToolStripSeparator());
                }


                foreach (ToolStripItem item in _cmsInput.Items)
                {
                    if (item is ToolStripMenuItem tsmi)
                    {
                        RefreshMenuText(tsmi);
                    }
                }
            }
        });
    }

    /// <summary>
    /// 遞迴更新選單項目標籤。
    /// </summary>
    /// <param name="parent">父選單項</param>
    private static void RefreshMenuText(ToolStripMenuItem parent)
    {
        foreach (ToolStripItem item in parent.DropDownItems)
        {
            if (item is ToolStripMenuItem mi)
            {
                char mnemonic = ' ';

                // 使用 Tag 作為穩定的標籤識別碼。
                string? label;

                if (mi.Tag is MenuMetadata meta)
                {
                    label = meta.Label;

                    mnemonic = meta.Mnemonic;
                }
                else if (mi.Tag is KeyValuePair<string, char> kvp)
                {
                    label = kvp.Key;

                    mnemonic = kvp.Value;
                }
                else
                {
                    label = mi.Tag as string ??
                        mi.AccessibleName;
                }

                if (string.IsNullOrEmpty(label))
                {
                    // 遞迴處理子項並跳過。
                    RefreshMenuText(mi);

                    continue;
                }

                string? fullText = null;

                // 根據標籤名稱從 AppSettings 讀取最新值並更新文字。
                if (label == Strings.Settings_WindowRestoreDelay)
                {
                    fullText = $"{label}: {AppSettings.Current.WindowRestoreDelay}";
                }
                else if (label == Strings.Settings_ClipboardRetryDelay)
                {
                    fullText = $"{label}: {AppSettings.Current.ClipboardRetryDelay}";
                }
                else if (label == Strings.Settings_TouchKeyboardDismissDelay)
                {
                    fullText = $"{label}: {AppSettings.Current.TouchKeyboardDismissDelay}";
                }
                else if (label == Strings.Settings_WindowSwitchBufferBase)
                {
                    fullText = $"{label}: {AppSettings.Current.WindowSwitchBufferBase}";
                }
                else if (label == Strings.Settings_InputJitterRange)
                {
                    fullText = $"{label}: {AppSettings.Current.InputJitterRange}";
                }
                else if (label == Strings.Settings_ThumbDeadzoneEnter)
                {
                    fullText = $"{label}: {AppSettings.Current.ThumbDeadzoneEnter}";
                }
                else if (label == Strings.Settings_ThumbDeadzoneExit)
                {
                    fullText = $"{label}: {AppSettings.Current.ThumbDeadzoneExit}";
                }
                else if (label == Strings.Settings_RepeatDelay)
                {
                    fullText = $"{label}: {AppSettings.Current.RepeatInitialDelayFrames}";
                }
                else if (label == Strings.Settings_RepeatSpeed)
                {
                    fullText = $"{label}: {AppSettings.Current.RepeatIntervalFrames}";
                }
                else if (label == Strings.Settings_HistoryCapacity)
                {
                    fullText = string.Format(Strings.Menu_Settings_HistoryCapacity, AppSettings.Current.HistoryCapacity);
                }
                else if (label == Strings.Settings_VibrationIntensity)
                {
                    // 格式化為小數點後一位（如 1.0），增進視覺一致性。
                    fullText = $"{label}: {AppSettings.Current.VibrationIntensity:F2}";
                }
                else if (label == Strings.Settings_WindowOpacity)
                {
                    fullText = $"{label}: {AppSettings.Current.WindowOpacity:P0}";

                    mi.AccessibleDescription = string.Format(
                        Strings.A11y_Menu_OpacityDesc,
                        label,
                        AppSettings.Current.WindowOpacity);
                }

                if (fullText != null)
                {
                    // 關鍵修正：套用 GetMnemonicText 以確保控制器按鍵提示始終顯示於末尾。
                    mi.Text = mnemonic != ' ' ?
                        ControlExtensions.GetMnemonicText(fullText, mnemonic) :
                        fullText;

                    // 同步更新無障礙名稱，確保控制器導覽時能播報目前數值。
                    mi.AccessibleName = fullText;

                    // 針對可勾選項（隱私模式、震動等），附加狀態文字以提升播報穩定性。
                    if (mi.CheckOnClick)
                    {
                        mi.AccessibleName += $" ({(mi.Checked ? Strings.A11y_Checked : Strings.A11y_Unchecked)})";
                    }

                    // 如果具備範圍資訊，動態生成詳細描述。
                    if (mi.Tag is MenuMetadata { Min: not null, Max: not null } rangeMeta)
                    {
                        string currentStr = label == Strings.Settings_VibrationIntensity ?
                            AppSettings.Current.VibrationIntensity.ToString("F2") :
                            (label == Strings.Settings_WindowOpacity ?
                                AppSettings.Current.WindowOpacity.ToString("P0") :
                                fullText.Split(':').Last().Trim());

                        mi.AccessibleDescription = string.Format(
                            Strings.A11y_Menu_NumericDesc,
                            label,
                            currentStr,
                            rangeMeta.Min,
                            rangeMeta.Max);

                        // 針對需重啟的項目，附加 A11y 警告後綴。
                        if (label == Strings.Settings_HistoryCapacity ||
                            label == Strings.Menu_Settings_Provider)
                        {
                            mi.AccessibleDescription += Strings.A11y_Settings_RestartRequired;
                        }
                    }
                }

                // 遞迴處理子項。
                RefreshMenuText(mi);
            }
        }
    }

    /// <summary>
    /// 在輸入框下方開啟右鍵選單，並自動選取第一個有效項目
    /// </summary>
    private void ShowContextMenuAtInput()
    {
        if (IsDisposed ||
            TBInput == null ||
            TBInput.IsDisposed ||
            _cmsInput == null)
        {
            return;
        }

        if (!_cmsInput.Visible)
        {
            // 在文字方塊下方開啟選單。
            _cmsInput.Show(this, new Point(TBInput.Left, TBInput.Bottom));

            // 選取第一個有效的項目。
            foreach (ToolStripItem item in _cmsInput.Items)
            {
                if (item.Enabled &&
                    item.Visible &&
                    item is not ToolStripSeparator)
                {
                    item.Select();

                    // 播報首個項目的名稱與描述。
                    // 優先使用 AccessibleName 以獲得包含狀態的完整標籤。
                    string name = item.AccessibleName ??
                            item.Text ??
                            string.Empty,
                        desc = item.AccessibleDescription ??
                        string.Empty;

                    string announcement = string.IsNullOrEmpty(desc) ?
                        name :
                        $"{name}. {desc}";

                    if (!string.IsNullOrEmpty(announcement))
                    {
                        AnnounceA11y(announcement, interrupt: true);
                    }

                    break;
                }
            }

            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
        }
    }

    /// <summary>
    /// 重建片語子選單項目
    /// </summary>
    private void RebuildPhraseMenuItems()
    {
        if (_tsmiPhrases == null)
        {
            return;
        }

        _tsmiPhrases.DropDownItems.Clear();

        IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;
        int recentLimit = GetRecentPhraseDisplayLimit();
        int pageSize = GetPhraseMenuPageSize();

        if (_tsmiPhrases.DropDown is ToolStripDropDownMenu dropDownMenu)
        {
            Rectangle workArea = Screen.GetWorkingArea(this);
            dropDownMenu.MaximumSize = new Size(0, (int)(workArea.Height * 0.55f));
        }

        // 同步清理已不存在於主片語清單的最近使用快取。
        _recentPhrases.RemoveAll(r => !phrases.Any(p => p.Name == r.Name && p.Content == r.Content));

        if (_recentPhrases.Count > 0)
        {
            foreach (PhraseService.PhraseEntry recent in _recentPhrases.Take(recentLimit))
            {
                PhraseService.PhraseEntry snapshot = recent;

                ToolStripMenuItem recentItem = new($"★ {snapshot.Name}")
                {
                    AccessibleName = snapshot.Name,
                    AccessibleDescription = snapshot.Content.Length > 50
                        ? $"{snapshot.Content[..50]}…"
                        : snapshot.Content
                };
                recentItem.Click += (s, e) =>
                {
                    try
                    {
                        // 插入後維持原生右鍵選單行為：選單自動關閉，避免多餘一步返回。
                        InsertPhraseContent(snapshot);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[選單] 最近片語插入失敗：{ex.Message}");
                    }
                };


                _tsmiPhrases.DropDownItems.Add(recentItem);
            }

            _tsmiPhrases.DropDownItems.Add(new ToolStripSeparator());
        }

        if (phrases.Count == 0)
        {
            ToolStripMenuItem emptyItem = new(Strings.Menu_PhraseEmpty)
            {
                Enabled = false,
                AccessibleName = Strings.Menu_PhraseEmpty
            };

            _tsmiPhrases.DropDownItems.Add(emptyItem);
        }
        else
        {
            int totalPages = (phrases.Count + pageSize - 1) / pageSize;

            _phraseMenuPage = Math.Clamp(_phraseMenuPage, 0, Math.Max(0, totalPages - 1));

            if (totalPages > 1)
            {
                ToolStripMenuItem prevPage = new("◀")
                {
                    Enabled = _phraseMenuPage > 0,
                    AccessibleName = Strings.Phrase_A11y_Page_Previous
                };
                prevPage.Click += (s, e) =>
                {
                    if (_phraseMenuPage <= 0)
                    {
                        return;
                    }

                    _phraseMenuPage--;
                    _keepPhrasePageOnNextOpen = true;
                    ReopenPhraseSubMenuAndSelectFirst();
                };

                ToolStripMenuItem pageInfo = new($"{_phraseMenuPage + 1}/{totalPages}")
                {
                    Enabled = false,
                    AccessibleName = string.Format(
                        Strings.Phrase_A11y_Page_Info,
                        _phraseMenuPage + 1,
                        totalPages)
                };

                ToolStripMenuItem nextPage = new("▶")
                {
                    Enabled = _phraseMenuPage < totalPages - 1,
                    AccessibleName = Strings.Phrase_A11y_Page_Next
                };
                nextPage.Click += (s, e) =>
                {
                    if (_phraseMenuPage >= totalPages - 1)
                    {
                        return;
                    }

                    _phraseMenuPage++;
                    _keepPhrasePageOnNextOpen = true;
                    ReopenPhraseSubMenuAndSelectFirst();
                };

                _tsmiPhrases.DropDownItems.Add(prevPage);
                _tsmiPhrases.DropDownItems.Add(pageInfo);
                _tsmiPhrases.DropDownItems.Add(nextPage);
                _tsmiPhrases.DropDownItems.Add(new ToolStripSeparator());
            }

            int start = _phraseMenuPage * pageSize;
            int endExclusive = Math.Min(start + pageSize, phrases.Count);

            for (int i = start; i < endExclusive; i++)
            {
                PhraseService.PhraseEntry entry = phrases[i];

                ToolStripMenuItem phraseItem = new(entry.Name)
                {
                    AccessibleName = entry.Name,
                    AccessibleDescription = entry.Content.Length > 50
                        ? $"{entry.Content[..50]}…"
                        : entry.Content,
                    Tag = i
                };
                phraseItem.Click += (s, e) =>
                {
                    try
                    {
                        if (s is ToolStripMenuItem mi && mi.Tag is int idx)
                        {
                            IReadOnlyList<PhraseService.PhraseEntry> current = _phraseService.Phrases;

                            if (idx >= 0 && idx < current.Count)
                            {
                                // 插入後不重開片語子選單，讓焦點回到輸入流程。
                                InsertPhraseContent(current[idx]);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[選單] 片語插入失敗：{ex.Message}");
                    }
                };

                _tsmiPhrases.DropDownItems.Add(phraseItem);
            }
        }

        _tsmiPhrases.DropDownItems.Add(new ToolStripSeparator());

        // 管理片語選項。
        ToolStripMenuItem tsmiManage = new(ControlExtensions.GetMnemonicText(Strings.Menu_ManagePhrases, 'M'))
        {
            AccessibleName = Strings.Menu_ManagePhrases
        };
        tsmiManage.Click += (s, e) =>
        {
            try
            {
                ShowPhraseManagerDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiManage.Click 失敗：{ex.Message}");
            }
        };

        _tsmiPhrases.DropDownItems.Add(tsmiManage);
    }

    /// <summary>
    /// 顯示片語管理對話框
    /// </summary>
    private void ShowPhraseManagerDialog()
    {
        try
        {
            using PhraseManagerDialog dialog = new(_phraseService)
            {
                GamepadController = _gamepadController
            };

            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(
                Left + Width + 8,
                Top);

            DialogResult result = dialog.ShowDialog(this);

            // 如果使用者選取了要插入的片語，則插入至輸入框。
            if (result == DialogResult.OK &&
                !string.IsNullOrEmpty(dialog.SelectedPhraseContent))
            {
                InsertPhraseContent(new PhraseService.PhraseEntry(
                    string.Empty,
                    dialog.SelectedPhraseContent));
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "[片語] ShowPhraseManagerDialog 失敗");

            Debug.WriteLine($"[片語] ShowPhraseManagerDialog 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 將片語內容插入文字方塊
    /// </summary>
    /// <param name="entry">片語項目</param>
    private void InsertPhraseContent(PhraseService.PhraseEntry entry)
    {
        if (TBInput == null || TBInput.IsDisposed)
        {
            return;
        }

        int selectionStart = TBInput.SelectionStart;

        // 若有選取的文字，取代之；否則在游標處插入。
        if (TBInput.SelectionLength > 0)
        {
            TBInput.SelectedText = entry.Content;
        }
        else
        {
            string text = TBInput.Text;

            TBInput.Text = text.Insert(selectionStart, entry.Content);
            TBInput.SelectionStart = selectionStart + entry.Content.Length;
        }

        TBInput.Focus();

        if (!string.IsNullOrEmpty(entry.Name))
        {
            AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                Strings.Phrase_A11y_Inserted_PrivacySafe :
                string.Format(Strings.Phrase_A11y_Inserted, entry.Name));
        }

        RegisterRecentPhrase(entry);
    }

    private void RegisterRecentPhrase(PhraseService.PhraseEntry entry)
    {
        PhraseService.PhraseEntry normalized = entry;

        // 從片語管理對話框插入時名稱可能為空，嘗試回查正式名稱。
        if (string.IsNullOrEmpty(normalized.Name) && !string.IsNullOrEmpty(normalized.Content))
        {
            IReadOnlyList<PhraseService.PhraseEntry> phrases = _phraseService.Phrases;

            PhraseService.PhraseEntry? resolved = phrases.FirstOrDefault(p => p.Content == normalized.Content);

            if (resolved != null)
            {
                normalized = resolved;
            }
        }

        if (string.IsNullOrEmpty(normalized.Name) || string.IsNullOrEmpty(normalized.Content))
        {
            return;
        }

        _recentPhrases.RemoveAll(p => p.Name == normalized.Name && p.Content == normalized.Content);
        _recentPhrases.Insert(0, normalized);

        int maxRecent = RecentPhraseLimitLarge;

        if (_recentPhrases.Count > maxRecent)
        {
            _recentPhrases.RemoveRange(maxRecent, _recentPhrases.Count - maxRecent);
        }
    }

    private int GetPhraseMenuPageSize()
    {
        int screenHeight = Screen.GetWorkingArea(this).Height;

        if (screenHeight <= 900)
        {
            return PhraseMenuPageSizeSmall;
        }

        if (screenHeight <= 1200)
        {
            return PhraseMenuPageSizeMedium;
        }

        return PhraseMenuPageSizeLarge;
    }

    private int GetRecentPhraseDisplayLimit()
    {
        int screenHeight = Screen.GetWorkingArea(this).Height;

        if (screenHeight <= 900)
        {
            return RecentPhraseLimitSmall;
        }

        if (screenHeight <= 1200)
        {
            return RecentPhraseLimitMedium;
        }

        return RecentPhraseLimitLarge;
    }

    /// <summary>
    /// 選取片語子選單中第一個片語項目（有整數 Tag 的項目）
    /// </summary>
    private void SelectFirstPhraseInDropDown()
    {
        if (_tsmiPhrases?.DropDown is not ToolStripDropDown dropDown)
        {
            return;
        }

        foreach (ToolStripItem item in dropDown.Items)
        {
            if (item.Tag is int &&
                item.Enabled &&
                item.Visible)
            {
                item.Select();

                return;
            }
        }
    }

    /// <summary>
    /// 重新開啟片語子選單並自動選取第一個片語，用於分頁換頁後保持導覽焦點
    /// </summary>
    private void ReopenPhraseSubMenuAndSelectFirst()
    {
        this.SafeBeginInvoke(() =>
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                ShowContextMenuAtInput();
                _tsmiPhrases?.ShowDropDown();
                SelectFirstPhraseInDropDown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] ReopenPhraseSubMenuAndSelectFirst 失敗：{ex.Message}");
            }
        });
    }

}