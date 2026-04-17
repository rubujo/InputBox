using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
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
    /// <summary>
    /// 大尺寸畫面時，片語選單單頁顯示的筆數。
    /// </summary>
    private const int PhraseMenuPageSizeLarge = 6;

    /// <summary>
    /// 中尺寸畫面時，片語選單單頁顯示的筆數。
    /// </summary>
    private const int PhraseMenuPageSizeMedium = 4;

    /// <summary>
    /// 小尺寸畫面時，片語選單單頁顯示的筆數。
    /// </summary>
    private const int PhraseMenuPageSizeSmall = 3;

    /// <summary>
    /// 大尺寸畫面時，最近使用片語的最大顯示筆數。
    /// </summary>
    private const int RecentPhraseLimitLarge = 5;

    /// <summary>
    /// 中尺寸畫面時，最近使用片語的最大顯示筆數。
    /// </summary>
    private const int RecentPhraseLimitMedium = 3;

    /// <summary>
    /// 小尺寸畫面時，最近使用片語的最大顯示筆數。
    /// </summary>
    private const int RecentPhraseLimitSmall = 2;

    /// <summary>
    /// 目前片語子選單所在的頁碼索引。
    /// </summary>
    private int _phraseMenuPage;

    /// <summary>
    /// 指示片語子選單下次重開時是否保留目前頁碼。
    /// </summary>
    private bool _keepPhrasePageOnNextOpen;

    /// <summary>
    /// 片語插入流程處理器（含最近使用片語管理）。
    /// </summary>
    private PhraseInsertionHandler? _phraseInsertionHandler;

    /// <summary>
    /// 選單項目中繼資料，支援 A11y 動態描述生成。
    /// </summary>
    /// <param name="Label">標籤文字</param>
    /// <param name="Mnemonic">助記鍵字母</param>
    /// <param name="Min">最小值（選填）</param>
    /// <param name="Max">最大值（選填）</param>
    /// <param name="Hint">補充用途說明（選填）。</param>
    private sealed record MenuMetadata(
        string Label,
        char Mnemonic,
        decimal? Min = null,
        decimal? Max = null,
        string? Hint = null);

    /// <summary>
    /// 初始化右鍵選單
    /// </summary>
    private void InitializeContextMenu()
    {
        _phraseInsertionHandler ??= new PhraseInsertionHandler(
            TBInput,
            _phraseService,
            AnnounceA11y,
            RecentPhraseLimitLarge);

        _cmsInput = ContextMenuBuilder.EnsureRoot(
            _cmsInput,
            Strings.A11y_ContextMenu_Name,
            Strings.A11y_ContextMenu_Desc);

        ContextMenuBuilder.EnsureRestartItem(
            _cmsInput,
            IsRestartUpdatePending,
            Strings.App_ThemePending_Suffix,
            RestartMenuLabel,
            () =>
            {
                try
                {
                    AskForRestart(RestartRequestSource.ManualMenu);
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "tsmiRestart.Click 失敗");

                    Debug.WriteLine($"[選單] tsmiRestart.Click 失敗：{ex.Message}");
                }
            },
            RestartMenuAccessibleDescription);

        // 隱私模式。
        _tsmiPrivacyMode = new ToolStripMenuItem(ControlExtensions.GetMnemonicText(Strings.Menu_PrivacyMode, 'P'))
        {
            Name = "TsmiPrivacyMode",
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
                LoggerService.LogException(ex, "隱私模式設定變更失敗");

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
                LoggerService.LogException(ex, "廣播中斷設定變更失敗");

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
                LoggerService.LogException(ex, "視覺警示設定變更失敗");

                Debug.WriteLine($"[選單] _tsmiAnimatedVisualAlerts.CheckedChanged 失敗：{ex.Message}");
            }
        };

        // 返回時最小化。
        _tsmiMinimizeOnReturn = new ToolStripMenuItem(ControlExtensions.GetMnemonicText(Strings.Menu_MinimizeOnReturn, 'M'))
        {
            CheckOnClick = true,
            Checked = AppSettings.Current.MinimizeOnReturn,
            AccessibleName = Strings.Menu_MinimizeOnReturn,
            AccessibleDescription = Strings.Menu_MinimizeOnReturn_Desc
        };

        _tsmiMinimizeOnReturn.CheckedChanged += (s, e) =>
        {
            try
            {
                if (_tsmiMinimizeOnReturn.Checked)
                {
                    // 使用者嘗試啟用：先暫停 CheckOnClick 效果，等待使用者確認後再決定。
                    DialogResult result = GamepadMessageBox.Show(
                        this,
                        Strings.Msg_MinimizeOnReturn_Confirm,
                        Strings.Msg_MinimizeOnReturn_Confirm_Title,
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2,
                        _gamepadController);

                    if (result != DialogResult.OK)
                    {
                        // 使用者取消：復原勾選狀態，不儲存。
                        _tsmiMinimizeOnReturn.Checked = false;

                        return;
                    }
                }

                AppSettings.Current.MinimizeOnReturn = _tsmiMinimizeOnReturn.Checked;
                AppSettings.Save();

                AnnounceA11y(AppSettings.Current.MinimizeOnReturn ?
                    Strings.A11y_MinimizeOnReturn_On :
                    Strings.A11y_MinimizeOnReturn_Off);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "返回時最小化設定變更失敗");

                Debug.WriteLine($"[選單] _tsmiMinimizeOnReturn.CheckedChanged 失敗：{ex.Message}");
            }
        };

        // 快速鍵設定子選單。
        // 提供修飾鍵與主按鍵擷取等快速鍵相關設定入口。
        ToolStripMenuItem tsmiHotkeySettings = new(ControlExtensions.GetMnemonicText(Strings.Menu_HotkeySettings, 'T'))
        {
            Name = "TsmiHotkeySettings",
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

        /// <summary>
        /// 新增快速鍵修飾鍵切換項目。
        /// </summary>
        /// <param name="label">顯示於選單中的修飾鍵名稱。</param>
        /// <param name="modValue">對應的修飾鍵旗標值。</param>
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
        // 進入快速鍵擷取模式，等待使用者按下新的主按鍵。
        ToolStripMenuItem tsmiCaptureKey = new(ControlExtensions.GetMnemonicText(Strings.Menu_CaptureKey, 'K'))
        {
            AccessibleName = Strings.Menu_CaptureKey,
            AccessibleDescription = Strings.Menu_CaptureKey_Desc
        };
        tsmiCaptureKey.Click += (s, e) =>
        {
            try
            {
                _inputState.BeginHotkeyCapture();

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
                BtnCopy.Text = Strings.Msg_PressAnyKey;

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
                LoggerService.LogException(ex, "擷取主要按鍵失敗");

                Debug.WriteLine($"[選單] tsmiCaptureKey.Click 失敗：{ex.Message}");
            }
        };
        tsmiHotkeySettings.DropDownItems.Add(tsmiCaptureKey);

        // 進階設定子選單。
        // 匯整視窗、回饋、遊戲控制器與資料夾等進階功能入口。
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
        // 包含視窗還原、剪貼簿重試與切換緩衝等系統互動參數。
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
            Tag = new KeyValuePair<string, char>(Strings.Settings_WindowOpacity, 'O')
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
        ToolStripMenuItem tsmiSetOpacity = new(ControlExtensions.GetMnemonicText(Strings.Menu_Opacity_Adjust, 'S'))
        {
            AccessibleName = Strings.Menu_Opacity_Adjust,
        };
        tsmiSetOpacity.Click += (s, e) =>
        {
            try
            {
                float? result = AskForFloat(
                    Strings.Settings_WindowOpacity,
                    AppSettings.Current.WindowOpacity * 100,
                    100.0f,
                    10.0f,
                    100.0f,
                    1.0m,
                    0,
                    confirmBeforeClose: (value) =>
                    {
                        // 低於 50% 時，在 Dialog 關閉前顯示知情警告。
                        if (value < 50.0f)
                        {
                            DialogResult confirm = GamepadMessageBox.Show(
                                this,
                                Strings.Msg_LowOpacity_Warn,
                                Strings.Msg_LowOpacity_Warn_Title,
                                MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Warning,
                                MessageBoxDefaultButton.Button2,
                                _gamepadController);

                            return confirm == DialogResult.OK;
                        }

                        return true;
                    });

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
                LoggerService.LogException(ex, "視窗不透明度設定失敗");

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
                LoggerService.LogException(ex, "重設不透明度失敗");

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
        /// <param name="a11yHint">選填的用途說明，供無障礙播報補充。</param>
        void AddNumericItem(
            ToolStripMenuItem parent,
            string label,
            char mnemonic,
            Func<int> getter,
            Action<int> setter,
            int defValue,
            int min,
            int max,
            string? a11yHint = null)
        {
            ToolStripMenuItem item = new(string.Empty)
            {
                AccessibleName = label,
                // 將範圍資訊與補充說明封裝至 Metadata，支援動態 A11y 描述生成。
                Tag = new MenuMetadata(label, mnemonic, min, max, a11yHint),
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
                    LoggerService.LogException(ex, $"數值設定 [{label}] 失敗");

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
                AppSettings.Save();

                RefreshMenu();

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                // 告知視窗設定已重置。
                AnnounceA11y(Strings.Msg_InputCleared);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "重設視窗操作設定失敗");

                Debug.WriteLine($"[選單] tsmiResetWinOps.Click 失敗：{ex.Message}");
            }
        };
        tsmiWinOps.DropDownItems.Add(tsmiResetWinOps);

        tsmiSettings.DropDownItems.Add(tsmiWinOps);

        // 回饋。
        // 集中管理震動開關與強度等回饋設定。
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

#if DEBUG
                LoggerService.LogInfo($"VibrationDiag source=Settings stage=toggle enableVibration={AppSettings.Current.EnableVibration}");
#endif
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "震動啟用設定變更失敗");

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

#if DEBUG
                    LoggerService.LogInfo($"VibrationDiag source=Settings stage=intensity vibrationIntensity={AppSettings.Current.VibrationIntensity:F2}");
#endif

                    RefreshMenu();

                    // 告知強度已更新。
                    AnnounceA11y($"{Strings.Settings_VibrationIntensity}: {val.Value}");
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "震動強度設定失敗");

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
                LoggerService.LogException(ex, "重設回饋設定失敗");

                Debug.WriteLine($"[選單] tsmiResetFeedback.Click 失敗：{ex.Message}");
            }
        };
        tsmiFeedback.DropDownItems.Add(tsmiResetFeedback);

        tsmiSettings.DropDownItems.Add(tsmiFeedback);

        //　控制器。
        // 提供遊戲控制器輸入 API、死區、重複輸入與校正狀態重設等設定。
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

        /// <summary>
        /// 新增遊戲控制器輸入 API 的選項項目。
        /// </summary>
        /// <param name="provider">要建立的輸入 API 類型。</param>
        void AddProviderItem(AppSettings.GamepadProvider provider)
        {
            char mnemonic = provider == AppSettings.GamepadProvider.GameInput ? 'G' : 'I';
            ToolStripMenuItem item = new(ControlExtensions.GetMnemonicText(provider.ToString(), mnemonic))
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

                        RefreshMenu();
                    }

                    if (IsRestartUpdatePending)
                    {
                        AnnounceA11y(Strings.Msg_RestartRequired);

                        AskForRestart();
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, $"GameInput Provider [{provider}] 選取失敗");

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

                    RefreshMenu();
                }

                if (IsRestartUpdatePending)
                {
                    AnnounceA11y(Strings.Msg_RestartRequired);

                    AskForRestart();
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "重設 GameInput Provider 失敗");

                Debug.WriteLine($"[選單] tsmiResetProvider.Click 失敗：{ex.Message}");
            }
        };
        tsmiProvider.DropDownItems.Add(tsmiResetProvider);

        // Face 鍵配置子選單會同時顯示選單標題與目前生效的配置狀態。
        string faceLayoutTitle = GamepadFaceButtonProfile.GetLayoutMenuTitleWithStatus();
        string faceLayoutStatus = GamepadFaceButtonProfile.GetActiveLayoutStatusSummary();
        ToolStripMenuItem tsmiFaceLayout = new(ControlExtensions.GetMnemonicText(faceLayoutTitle, 'L'))
        {
            Tag = new KeyValuePair<string, char>(Strings.Settings_GamepadFaceButtonLayout, 'L'),
            AccessibleName = faceLayoutTitle,
            AccessibleDescription = $"{Strings.A11y_Menu_Gamepad_Desc} {faceLayoutStatus}"
        };

        ToolStripMenuItem tsmiFaceLayoutStatus = new(faceLayoutStatus)
        {
            Tag = "GamepadFaceLayoutCurrentStatus",
            Enabled = false,
            AccessibleName = faceLayoutStatus,
            AccessibleDescription = faceLayoutStatus
        };
        tsmiFaceLayout.DropDownItems.Add(tsmiFaceLayoutStatus);
        tsmiFaceLayout.DropDownItems.Add(new ToolStripSeparator());

        // 依序建立可供使用者選取的 Face 鍵配置選項，並同步處理狀態勾選與切換後的 UI 更新。
        // 勾選狀態以「目前實際生效的配置」為準，避免 Auto 長期勾選卻與使用者眼前的控制器對應不一致。
        AppSettings.GamepadFaceButtonMode checkedLayoutMode = GamepadFaceButtonProfile.GetActiveMenuCheckedMode();

        void AddFaceLayoutItem(AppSettings.GamepadFaceButtonMode mode)
        {
            char mnemonic = mode switch
            {
                AppSettings.GamepadFaceButtonMode.Auto => 'A',
                AppSettings.GamepadFaceButtonMode.Xbox => 'X',
                AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm => 'P',
                AppSettings.GamepadFaceButtonMode.PlayStationTraditional => 'O',
                AppSettings.GamepadFaceButtonMode.Nintendo => 'N',
                _ => 'L',
            };

            string label = GamepadFaceButtonProfile.GetFriendlyModeName(mode);
            ToolStripMenuItem item = new(ControlExtensions.GetMnemonicText(label, mnemonic))
            {
                Tag = mode,
                CheckOnClick = true,
                Checked = checkedLayoutMode == mode,
                AccessibleName = label,
                AccessibleDescription = Strings.A11y_Menu_Gamepad_Desc
            };

            item.Click += (s, e) =>
            {
                try
                {
                    if (AppSettings.Current.GamepadFaceButtonModeType != mode)
                    {
                        AppSettings.Current.GamepadFaceButtonModeType = mode;
                        AppSettings.Save();

                        ApplyCurrentGamepadFaceButtonMode(announceProfileChange: true);
                    }

                    RefreshMenu();
                }
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, $"Face Button Layout [{mode}] 選取失敗");
                    Debug.WriteLine($"[選單] Face Button Layout {mode} 選取失敗：{ex.Message}");
                }
            };

            tsmiFaceLayout.DropDownItems.Add(item);
        }

        AddFaceLayoutItem(AppSettings.GamepadFaceButtonMode.Auto);
        AddFaceLayoutItem(AppSettings.GamepadFaceButtonMode.Xbox);
        AddFaceLayoutItem(AppSettings.GamepadFaceButtonMode.PlayStationCrossConfirm);
        AddFaceLayoutItem(AppSettings.GamepadFaceButtonMode.PlayStationTraditional);
        AddFaceLayoutItem(AppSettings.GamepadFaceButtonMode.Nintendo);

        string identifyControllerLabel = GetLocalizedString("Menu_Gamepad_IdentifyController", "Identify Controller");
        string identifyControllerDescription = GetLocalizedString(
            "Menu_Gamepad_IdentifyController_Desc",
            "Play a distinct vibration on the active controller so you can find it quickly.");
        ToolStripMenuItem tsmiIdentifyController = new(ControlExtensions.GetMnemonicText(identifyControllerLabel, 'I'))
        {
            AccessibleName = identifyControllerLabel,
            AccessibleDescription = identifyControllerDescription
        };
        tsmiIdentifyController.Click += (s, e) =>
        {
            try
            {
                IdentifyCurrentControllerAsync().SafeFireAndForget();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "識別目前控制器失敗");
                Debug.WriteLine($"[選單] tsmiIdentifyController.Click 失敗：{ex.Message}");
            }
        };

        tsmiGamepad.DropDownItems.Add(tsmiProvider);
        tsmiGamepad.DropDownItems.Add(tsmiFaceLayout);
        tsmiGamepad.DropDownItems.Add(tsmiIdentifyController);
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
            30000,
            Strings.A11y_Menu_Gamepad_DeadzoneEnter_Hint);
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
            30000,
            Strings.A11y_Menu_Gamepad_DeadzoneExit_Hint);
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
            300,
            Strings.A11y_Menu_Gamepad_RepeatDelay_Hint);
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
            100,
            Strings.A11y_Menu_Gamepad_RepeatSpeed_Hint);

        tsmiGamepad.DropDownItems.Add(new ToolStripSeparator());

        // 重設執行期校正狀態，不影響已儲存的遊戲控制器設定。
        ToolStripMenuItem tsmiResetCalibration = new(ControlExtensions.GetMnemonicText(Strings.Menu_Gamepad_ResetCalibration, 'C'))
        {
            AccessibleName = Strings.Menu_Gamepad_ResetCalibration,
            AccessibleDescription = Strings.Menu_Gamepad_ResetCalibration_Desc
        };
        tsmiResetCalibration.Click += (s, e) =>
        {
            try
            {
                _gamepadController?.ResetCalibration();

                FeedbackService.PlaySound(SystemSounds.Asterisk);
                AnnounceA11y(Strings.A11y_Gamepad_CalibrationReset);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "重設目前遊戲控制器校正狀態失敗");

                Debug.WriteLine($"[選單] tsmiResetCalibration.Click 失敗：{ex.Message}");
            }
        };
        tsmiGamepad.DropDownItems.Add(tsmiResetCalibration);

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
                LoggerService.LogException(ex, "重設控制器設定失敗");

                Debug.WriteLine($"[選單] tsmiResetGamepad.Click 失敗：{ex.Message}");
            }
        };
        tsmiGamepad.DropDownItems.Add(tsmiResetGamepad);

        tsmiSettings.DropDownItems.Add(tsmiGamepad);
        tsmiSettings.DropDownItems.Add(new ToolStripSeparator());

        // 歷程容量（需重啟）。
        // 控制記憶體中保留的輸入歷程筆數上限。
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
                LoggerService.LogException(ex, "歷程容量設定失敗");

                Debug.WriteLine($"[選單] tsmiCap.Click 失敗：{ex.Message}");
            }
        };
        tsmiSettings.DropDownItems.Add(tsmiCap);

        tsmiSettings.DropDownItems.Add(new ToolStripSeparator());

        // 開啟資料夾。
        // 開啟應用程式設定與資料檔所在的資料夾。
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
                else
                {
                    AnnounceA11y(Strings.Msg_FolderNotFound);

                    GamepadMessageBox.Show(
                        this,
                        Strings.Msg_FolderNotFound,
                        Strings.Wrn_Title,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning,
                        gamepad: _gamepadController);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiOpenDataFolder.Click 失敗：{ex.Message}");
            }
        };
        tsmiSettings.DropDownItems.Add(tsmiOpenDataFolder);

        // 開啟日誌資料夾。
        // 開啟本機例外與診斷紀錄所在的日誌目錄。
        ToolStripMenuItem tsmiOpenLogFolder = new(ControlExtensions.GetMnemonicText(Strings.Menu_OpenLogFolder, 'L'))
        {
            AccessibleName = Strings.Menu_OpenLogFolder,
            AccessibleDescription = Strings.Menu_OpenLogFolder_Desc
        };
        tsmiOpenLogFolder.Click += (s, e) =>
        {
            try
            {
                if (Directory.Exists(LoggerService.LogDirectory))
                {
                    Process.Start(new ProcessStartInfo(LoggerService.LogDirectory)
                    {
                        UseShellExecute = true
                    });
                }
                else
                {
                    AnnounceA11y(Strings.Msg_FolderNotFound);

                    GamepadMessageBox.Show(
                        this,
                        Strings.Msg_FolderNotFound,
                        Strings.Wrn_Title,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning,
                        gamepad: _gamepadController);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] tsmiOpenLogFolder.Click 失敗：{ex.Message}");
            }
        };
        tsmiSettings.DropDownItems.Add(tsmiOpenLogFolder);

        // 清除歷程。
        // 清空目前只保存在記憶體中的輸入歷程資料。
        ToolStripMenuItem tsmiClearHistory = new(ControlExtensions.GetMnemonicText(Strings.Menu_ClearHistory, 'C'))
        {
            Name = "TsmiClearHistory",
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
        // 關閉主視窗並結束整個應用程式流程。
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
        // 顯示鍵盤與遊戲控制器操作對照的說明對話框。
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
            Name = "TsmiPhrases",
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
                LoggerService.LogException(ex, "片語子選單重建失敗");

                Debug.WriteLine($"[選單] _tsmiPhrases.DropDownOpening 失敗：{ex.Message}");
            }
        };

        _tsmiPhrases.DropDownOpened += (s, e) =>
        {
            try
            {
                AnnouncePhraseSubMenuOpened();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[選單] _tsmiPhrases.DropDownOpened 失敗：{ex.Message}");
            }
        };

        _cmsInput.Items.Add(_tsmiPrivacyMode);
        _cmsInput.Items.Add(_tsmiA11yInterrupt);
        _cmsInput.Items.Add(_tsmiAnimatedVisualAlerts);
        _cmsInput.Items.Add(_tsmiMinimizeOnReturn);
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
            // 選單與標題列共用同一組待重啟狀態來源；重新整理選單時必須同步刷新標題提示。
            UpdateTitlePrefix();
            UpdateTitle();

            if (_cmsInput != null)
            {
                // 在重新整理時，若有待處理變更則動態注入重啟選項。
                ContextMenuBuilder.EnsureRestartItem(
                    _cmsInput,
                    IsRestartUpdatePending,
                    Strings.App_ThemePending_Suffix,
                    RestartMenuLabel,
                    () => AskForRestart(RestartRequestSource.ManualMenu),
                    RestartMenuAccessibleDescription);


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
    /// 遞迴更新選單項目標籤
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
                string? currentValueText = null;

                if (mi.Tag is AppSettings.GamepadFaceButtonMode faceLayoutMode)
                {
                    fullText = GamepadFaceButtonProfile.GetFriendlyModeName(faceLayoutMode);
                    mi.Checked = GamepadFaceButtonProfile.GetActiveMenuCheckedMode() == faceLayoutMode;
                }
                else if (label == Strings.Settings_WindowRestoreDelay)
                {
                    currentValueText = AppSettings.Current.WindowRestoreDelay.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_ClipboardRetryDelay)
                {
                    currentValueText = AppSettings.Current.ClipboardRetryDelay.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_TouchKeyboardDismissDelay)
                {
                    currentValueText = AppSettings.Current.TouchKeyboardDismissDelay.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_WindowSwitchBufferBase)
                {
                    currentValueText = AppSettings.Current.WindowSwitchBufferBase.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_ThumbDeadzoneEnter)
                {
                    currentValueText = AppSettings.Current.ThumbDeadzoneEnter.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_ThumbDeadzoneExit)
                {
                    currentValueText = AppSettings.Current.ThumbDeadzoneExit.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_RepeatDelay)
                {
                    currentValueText = AppSettings.Current.RepeatInitialDelayFrames.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_RepeatSpeed)
                {
                    currentValueText = AppSettings.Current.RepeatIntervalFrames.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_HistoryCapacity)
                {
                    currentValueText = AppSettings.Current.HistoryCapacity.ToString();
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_VibrationIntensity)
                {
                    // 格式化為小數點後兩位（如 1.00），增進視覺一致性。
                    currentValueText = AppSettings.Current.VibrationIntensity.ToString("F2");
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);
                }
                else if (label == Strings.Settings_WindowOpacity)
                {
                    currentValueText = AppSettings.Current.WindowOpacity.ToString("P0");
                    fullText = ControlExtensions.GetLabelValueText(label, currentValueText);

                    mi.AccessibleDescription = string.Format(
                        Strings.A11y_Menu_OpacityDesc,
                        label,
                        AppSettings.Current.WindowOpacity);
                }
                else if (label == Strings.Settings_GamepadFaceButtonLayout)
                {
                    fullText = GamepadFaceButtonProfile.GetLayoutMenuTitleWithStatus();
                    mi.AccessibleDescription = $"{Strings.A11y_Menu_Gamepad_Desc} {GamepadFaceButtonProfile.GetActiveLayoutStatusSummary()}";
                }
                else if (label == "GamepadFaceLayoutCurrentStatus")
                {
                    fullText = GamepadFaceButtonProfile.GetActiveLayoutStatusSummary();
                    mi.AccessibleDescription = fullText;
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
                        string currentStr = currentValueText ??
                            (label == Strings.Settings_VibrationIntensity ?
                                AppSettings.Current.VibrationIntensity.ToString("F2") :
                                (label == Strings.Settings_WindowOpacity ?
                                    AppSettings.Current.WindowOpacity.ToString("P0") :
                                    string.Empty));

                        mi.AccessibleDescription = string.Format(
                            Strings.A11y_Menu_NumericDesc,
                            label,
                            currentStr,
                            rangeMeta.Min,
                            rangeMeta.Max);

                        if (!string.IsNullOrWhiteSpace(rangeMeta.Hint))
                        {
                            mi.AccessibleDescription += " " + rangeMeta.Hint;
                        }

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

            if (ContextMenuBuilder.TrySelectFirstVisibleItem(
                _cmsInput,
                Strings.A11y_Checked,
                Strings.A11y_Unchecked,
                out string announcement))
            {
                AnnounceA11y(announcement, interrupt: true);
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

        int recentLimit = GetRecentPhraseDisplayLimit(),
            pageSize = GetPhraseMenuPageSize();

        if (_tsmiPhrases.DropDown is ToolStripDropDownMenu dropDownMenu)
        {
            Rectangle workArea = Screen.GetWorkingArea(this);

            dropDownMenu.MaximumSize = new Size(0, (int)(workArea.Height * 0.55f));
        }

        // 同步清理已不存在於主片語清單的最近使用快取。
        _phraseInsertionHandler?.PruneRecent(phrases);

        IReadOnlyList<PhraseService.PhraseEntry> recentPhrases = _phraseInsertionHandler?.RecentPhrases ?? [];

        if (recentPhrases.Count > 0)
        {
            foreach (PhraseService.PhraseEntry recent in recentPhrases.Take(recentLimit))
            {
                PhraseService.PhraseEntry snapshot = recent;

                ToolStripMenuItem recentItem = new($"★ {snapshot.Name}")
                {
                    AccessibleName = snapshot.Name,
                    AccessibleDescription = snapshot.Content.Length > 50 ?
                        $"{snapshot.Content[..50]}…" :
                        snapshot.Content
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
                        LoggerService.LogException(ex, "最近片語插入失敗");

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
                    _ = TryNavigatePhraseMenuPage(-1);
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
                    _ = TryNavigatePhraseMenuPage(1);
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
                        LoggerService.LogException(ex, "片語插入失敗");

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
                LoggerService.LogException(ex, "開啟片語管理對話框失敗");

                Debug.WriteLine($"[選單] tsmiManage.Click 失敗：{ex.Message}");
            }
        };

        _tsmiPhrases.DropDownItems.Add(tsmiManage);

        // 匯出片語。
        ToolStripMenuItem tsmiExport = new(ControlExtensions.GetMnemonicText(Strings.Menu_ExportPhrases, 'E'))
        {
            AccessibleName = Strings.Menu_ExportPhrases
        };
        tsmiExport.Click += (s, e) =>
        {
            try
            {
                ExportPhrases();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "匯出片語失敗");

                Debug.WriteLine($"[選單] tsmiExport.Click 失敗：{ex.Message}");
            }
        };
        _tsmiPhrases.DropDownItems.Add(tsmiExport);

        // 匯入片語。
        ToolStripMenuItem tsmiImport = new(ControlExtensions.GetMnemonicText(Strings.Menu_ImportPhrases, 'I'))
        {
            AccessibleName = Strings.Menu_ImportPhrases
        };
        tsmiImport.Click += (s, e) =>
        {
            try
            {
                ImportPhrases();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "匯入片語失敗");

                Debug.WriteLine($"[選單] tsmiImport.Click 失敗：{ex.Message}");
            }
        };
        _tsmiPhrases.DropDownItems.Add(tsmiImport);
    }

    /// <summary>
    /// 匯出片語至使用者選定的路徑
    /// </summary>
    private void ExportPhrases()
    {
        if (_phraseService.Count == 0)
        {
            GamepadMessageBox.Show(
                this,
                Strings.Msg_ExportPhrases_Empty,
                Strings.Menu_ExportPhrases,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                _gamepadController);

            return;
        }

        using SaveFileDialog dlg = new()
        {
            Title = Strings.Menu_ExportPhrases,
            Filter = "JSON|*.json",
            FileName = "phrases.json",
            DefaultExt = "json",
            OverwritePrompt = true
        };

        if (ShowFileDialogWithGamepadGuard(dlg) != DialogResult.OK)
        {
            return;
        }

        PhraseService.ExportOutcome result = _phraseService.ExportToFile(dlg.FileName);

        if (result.Success)
        {
            AnnounceA11y(string.Format(Strings.A11y_Phrases_Exported, result.Exported));

            GamepadMessageBox.Show(
                this,
                string.Format(Strings.Msg_ExportPhrases_Success, result.Exported),
                Strings.Menu_ExportPhrases,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                _gamepadController);
        }
        else
        {
            GamepadMessageBox.Show(
                this,
                Strings.Msg_ExportPhrases_Error,
                Strings.Err_Title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1,
                _gamepadController);
        }
    }

    /// <summary>
    /// 以遊戲控制器保護模式顯示原生檔案對話框。
    /// <para>在顯示期間會暫停輪詢並於關閉後以乾淨狀態恢復，避免方向輸入殘留到主視窗。</para>
    /// </summary>
    /// <param name="dialog">要顯示的原生對話框。</param>
    /// <returns>對話框結果。</returns>
    private DialogResult ShowFileDialogWithGamepadGuard(CommonDialog dialog)
    {
        _gamepadController?.Pause();

        try
        {
            return dialog.ShowDialog(this);
        }
        finally
        {
            if (!IsDisposed)
            {
                _gamepadController?.Resume();
            }
        }
    }

    /// <summary>
    /// 從使用者選定的路徑匯入片語
    /// </summary>
    private void ImportPhrases()
    {
        using OpenFileDialog dlg = new()
        {
            Title = Strings.Menu_ImportPhrases,
            Filter = "JSON|*.json",
            DefaultExt = "json"
        };

        if (ShowFileDialogWithGamepadGuard(dlg) != DialogResult.OK)
        {
            return;
        }

        int currentCount = _phraseService.Count;

        if (currentCount > 0)
        {
            DialogResult confirm = GamepadMessageBox.Show(
                this,
                string.Format(Strings.Msg_ImportPhrases_Confirm, currentCount),
                Strings.Menu_ImportPhrases,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2,
                _gamepadController);

            if (confirm != DialogResult.OK)
            {
                return;
            }
        }

        PhraseService.ImportOutcome result = _phraseService.ImportFromFile(dlg.FileName);

        if (result.Success)
        {
            AnnounceA11y(string.Format(Strings.A11y_Phrases_Imported, result.Imported));

            GamepadMessageBox.Show(
                this,
                string.Format(Strings.Msg_ImportPhrases_Success, result.Imported),
                Strings.Menu_ImportPhrases,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                _gamepadController);
        }
        else
        {
            string errorMsg = result.Error == PhraseService.ImportError.PersistenceFailed
                ? Strings.Msg_ImportPhrases_Error_Persist
                : Strings.Msg_ImportPhrases_Error;

            GamepadMessageBox.Show(
                this,
                errorMsg,
                Strings.Err_Title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1,
                _gamepadController);
        }
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
        if (_phraseInsertionHandler == null)
        {
            _phraseInsertionHandler = new PhraseInsertionHandler(
                TBInput,
                _phraseService,
                AnnounceA11y,
                RecentPhraseLimitLarge);

            if (_phraseInsertionHandler == null)
            {
                return;
            }
        }

        _phraseInsertionHandler.InsertPhraseContent(entry);
    }

    /// <summary>
    /// 取得片語子選單的分頁大小，根據螢幕高度調整以適應不同解析度的顯示空間
    /// </summary>
    /// <returns>分頁大小</returns>
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

    /// <summary>
    /// 取得最近使用片語的顯示限制，根據螢幕高度調整以適應不同解析度的顯示空間
    /// </summary>
    /// <returns>顯示限制</returns>
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
    /// 取得片語子選單的總頁數。
    /// </summary>
    private int GetPhraseMenuTotalPages()
    {
        int phraseCount = _phraseService.Phrases.Count;
        int pageSize = Math.Max(1, GetPhraseMenuPageSize());

        return phraseCount <= 0 ?
            0 :
            (phraseCount + pageSize - 1) / pageSize;
    }

    /// <summary>
    /// 播報目前片語子選單的頁碼與快捷操作提示。
    /// </summary>
    private void AnnouncePhraseSubMenuOpened()
    {
        string message = string.Format(Strings.A11y_Menu_Submenu_Enter, Strings.Menu_Phrases);
        int totalPages = GetPhraseMenuTotalPages();

        if (totalPages > 1)
        {
            message = $"{message} {string.Format(Strings.Phrase_A11y_Page_Info, _phraseMenuPage + 1, totalPages)} {Strings.Phrase_A11y_Page_Shortcuts}";
        }

        AnnounceA11y(message, interrupt: true);
    }

    /// <summary>
    /// 嘗試切換片語子選單頁碼。
    /// </summary>
    /// <param name="delta">頁碼增量（-1 = 上一頁，+1 = 下一頁）。</param>
    /// <returns>若片語子選單已接收此操作則回傳 true。</returns>
    private bool TryNavigatePhraseMenuPage(int delta)
    {
        if (_tsmiPhrases?.DropDown.Visible != true)
        {
            return false;
        }

        return TrySetPhraseMenuPage(_phraseMenuPage + delta);
    }

    /// <summary>
    /// 嘗試將片語子選單跳到首頁或末頁。
    /// </summary>
    /// <param name="lastPage">true = 末頁；false = 首頁。</param>
    /// <returns>若片語子選單已接收此操作則回傳 true。</returns>
    private bool TryJumpPhraseMenuToBoundary(bool lastPage)
    {
        if (_tsmiPhrases?.DropDown.Visible != true)
        {
            return false;
        }

        int totalPages = GetPhraseMenuTotalPages();
        int targetPage = lastPage && totalPages > 0 ?
            totalPages - 1 :
            0;

        return TrySetPhraseMenuPage(targetPage);
    }

    /// <summary>
    /// 依指定目標頁碼更新片語子選單，並提供震動與無障礙回饋。
    /// </summary>
    /// <param name="targetPage">目標頁碼索引（0-based）。</param>
    /// <returns>若片語子選單已接收此操作則回傳 true。</returns>
    private bool TrySetPhraseMenuPage(int targetPage)
    {
        int totalPages = GetPhraseMenuTotalPages();

        if (totalPages <= 1)
        {
            if (ShouldThrottleRepeatedBoundaryFeedback("PhraseSinglePage"))
            {
                return true;
            }

            FeedbackService.PlaySound(SystemSounds.Beep);
            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

            if (totalPages == 1)
            {
                AnnounceA11y(string.Format(Strings.Phrase_A11y_Page_Info, 1, 1), interrupt: true);
            }

            return true;
        }

        int clampedPage = Math.Clamp(targetPage, 0, totalPages - 1);

        if (clampedPage == _phraseMenuPage)
        {
            int boundaryDirection = targetPage < _phraseMenuPage ? -1 : 1;
            string boundaryKey = boundaryDirection < 0 ? "PhrasePreviousBoundary" : "PhraseNextBoundary";

            if (ShouldThrottleRepeatedBoundaryFeedback(boundaryKey))
            {
                return true;
            }

            FeedbackService.PlaySound(SystemSounds.Beep);
            VibrateNavigationAsync(VibrationSemantic.Boundary, boundaryDirection, VibrationContext.PhraseMenu).SafeFireAndForget();
            AnnounceA11y(string.Format(Strings.Phrase_A11y_Page_Info, _phraseMenuPage + 1, totalPages), interrupt: true);

            return true;
        }

        int direction = clampedPage < _phraseMenuPage ? -1 : 1;

        _phraseMenuPage = clampedPage;
        _keepPhrasePageOnNextOpen = true;

        AnnounceA11y(string.Format(Strings.Phrase_A11y_Page_Info, _phraseMenuPage + 1, totalPages), interrupt: true);
        VibrateNavigationAsync(VibrationSemantic.PageSwitch, direction, VibrationContext.PhraseMenu).SafeFireAndForget();
        ReopenPhraseSubMenuAndSelectFirst();

        return true;
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
                if (IsDisposed ||
                !IsHandleCreated)
                {
                    return;
                }

                ShowContextMenuAtInput();
                _tsmiPhrases?.ShowDropDown();
                SelectFirstPhraseInDropDown();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "ReopenPhraseSubMenuAndSelectFirst 失敗");

                Debug.WriteLine($"[選單] ReopenPhraseSubMenuAndSelectFirst 失敗：{ex.Message}");
            }
        });
    }
}