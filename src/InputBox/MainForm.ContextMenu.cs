using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Media;

namespace InputBox;

// 阻擋設計工具。
partial class DesignerBlocker { };

public partial class MainForm
{
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
        _cmsInput ??= new ContextMenuStrip();

        // 動態加入主題變更重啟提示。
        if (_isThemeUpdatePending)
        {
            ToolStripMenuItem tsmiRestart = new()
            {
                Text = $"{Strings.App_ThemePending_Suffix} {Strings.Menu_ApplyThemeRestart}",
                AccessibleName = Strings.Menu_ApplyThemeRestart
            };
            tsmiRestart.Click += (s, e) => AskForRestart();

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
        };

        // 快速鍵設定子選單。
        ToolStripMenuItem tsmiHotkeySettings = new(ControlExtensions.GetMnemonicText(Strings.Menu_HotkeySettings, 'H'))
        {
            AccessibleName = Strings.Menu_HotkeySettings,
            AccessibleDescription = Strings.Menu_HotkeySettings_Desc
        };

        // 修飾鍵設定（使用本地函數）。
        // 將 modValue 的型別從 int 改為 User32.KeyModifiers 列舉
        void AddModifierItem(string label, User32.KeyModifiers modValue)
        {
            ToolStripMenuItem item = new(label)
            {
                CheckOnClick = true,
                // 使用 HasFlag 讓語意更清晰。
                Checked = AppSettings.Current.HotKeyModifiers.HasFlag(modValue),
                AccessibleName = label,
                // A11y 描述：說明此勾選框的用途。
                AccessibleDescription = string.Format(Strings.A11y_Mod_Toggle_Desc, label)
            };

            item.CheckedChanged += (s, e) =>
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
            TBInput.AccessibleName = Strings.Msg_PressAnyKey;
            TBInput.AccessibleDescription = Strings.A11y_Capture_Esc_Cancel;

            // 形狀變化。加粗邊框 4 像素（非顏色提示）。
            PInputHost.Padding = new Padding(7);

            // 按鈕文字提示與狀態變更。
            BtnCopy.Enabled = false;
            BtnCopy.Text = "...";

            // 3. 邊框顏色變化（兼顧高對比）。
            PInputHost.BackColor = SystemInformation.HighContrast ?
                SystemColors.HighlightText :
                Color.Orange;

            // 關閉輸入法。
            TBInput.ImeMode = ImeMode.Disable;

            // 暫時移除選單，防止在擷取模式下開啟選單導致衝突。
            TBInput.ContextMenuStrip = null;

            _cmsInput?.Close();

            // 焦點救援。
            TBInput.Focus();

            // 觸發一次視覺閃爍動畫。
            FlashAlertAsync().SafeFireAndForget();
        };
        tsmiHotkeySettings.DropDownItems.Add(tsmiCaptureKey);

        // 進階設定子選單。
        ToolStripMenuItem tsmiSettings = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings, 'S'))
        {
            AccessibleName = Strings.Menu_Settings,
            AccessibleDescription = Strings.A11y_Menu_Settings_Desc
        };

        // 視窗與操作。
        ToolStripMenuItem tsmiWinOps = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Window, 'W'))
        {
            AccessibleName = Strings.Menu_Settings_Window,
            AccessibleDescription = Strings.A11y_Menu_WinOps_Desc
        };

        // 不透明度。
        ToolStripMenuItem tsmiOpacity = new(ControlExtensions.GetMnemonicText(Strings.Settings_WindowOpacity, 'O'))
        {
            AccessibleName = "OpacityGroup",
        };
        tsmiOpacity.DropDownOpening += (s, e) =>
        {
            tsmiOpacity.AccessibleDescription = string.Format(
                Strings.A11y_Menu_OpacityDesc,
                Strings.Settings_WindowOpacity,
                AppSettings.Current.WindowOpacity);
        };
        tsmiWinOps.DropDownItems.Add(tsmiOpacity);

        // 設定不透明度數值。
        ToolStripMenuItem tsmiSetOpacity = new(string.Empty)
        {
            AccessibleName = Strings.Settings_WindowOpacity,
            Tag = new MenuMetadata(Strings.Settings_WindowOpacity, 'S', 50, 100)
        };
        tsmiSetOpacity.Click += (s, e) =>
        {
            float? result = AskForFloat(
                Strings.Settings_WindowOpacity,
                AppSettings.Current.WindowOpacity * 100,
                100.0f,
                50.0f,
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
        };
        tsmiOpacity.DropDownItems.Add(tsmiSetOpacity);

        // 重設不透明度。
        ToolStripMenuItem tsmiResetOpacity = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault
        };
        tsmiResetOpacity.Click += (s, e) =>
        {
            ResetOpacity();
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
                int? val = AskForValue(label, getter(), defValue, min, max);

                if (val.HasValue)
                {
                    setter(val.Value);

                    AppSettings.Save();

                    RefreshMenu();
                }

                // 焦點還原。
                // 當數值輸入對話框關閉後，將焦點精確還原至原選單項，優化螢幕閱讀器導覽流暢度。
                _lastFocusedMenuItem?.Select();
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
            AccessibleName = Strings.Btn_SetDefault
        };
        tsmiResetWinOps.Click += (s, e) =>
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
        };
        tsmiWinOps.DropDownItems.Add(tsmiResetWinOps);

        tsmiSettings.DropDownItems.Add(tsmiWinOps);

        // 回饋。
        ToolStripMenuItem tsmiFeedback = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Feedback, 'F'))
        {
            AccessibleName = Strings.Menu_Settings_Feedback,
            AccessibleDescription = Strings.A11y_Menu_Feedback_Desc
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
            AppSettings.Current.EnableVibration = tsmiVibEnable.Checked;
            AppSettings.Save();
        };
        tsmiFeedback.DropDownItems.Add(tsmiVibEnable);

        ToolStripMenuItem tsmiIntensity = new(string.Empty)
        {
            AccessibleName = Strings.Settings_VibrationIntensity,
            Tag = new MenuMetadata(Strings.Settings_VibrationIntensity, 'I', 0, 1)
        };
        tsmiIntensity.Click += (s, e) =>
        {
            float? val = AskForFloat(
                Strings.Settings_VibrationIntensity,
                AppSettings.Current.VibrationIntensity,
                1.0f,
                0.0f,
                1.0f,
                0.1m,
                1);

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
        };
        tsmiFeedback.DropDownItems.Add(tsmiIntensity);

        tsmiFeedback.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem tsmiResetFeedback = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault
        };
        tsmiResetFeedback.Click += (s, e) =>
        {
            AppSettings.Current.EnableVibration = true;
            AppSettings.Current.VibrationIntensity = 1.0f;

            VibrationPatterns.GlobalIntensityMultiplier = 1.0f;

            AppSettings.Save();

            tsmiVibEnable.Checked = true;

            RefreshMenu();

            FeedbackService.PlaySound(SystemSounds.Asterisk);

            // 告知回饋設定已重置。
            AnnounceA11y(Strings.Msg_InputCleared);
        };
        tsmiFeedback.DropDownItems.Add(tsmiResetFeedback);

        tsmiSettings.DropDownItems.Add(tsmiFeedback);

        //　控制器。
        ToolStripMenuItem tsmiGamepad = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Gamepad, 'G'))
        {
            AccessibleName = Strings.Menu_Settings_Gamepad,
            AccessibleDescription = Strings.A11y_Menu_Gamepad_Desc
        };

        // Provider（需重啟）。
        ToolStripMenuItem tsmiProvider = new(ControlExtensions.GetMnemonicText(Strings.Menu_Settings_Provider, 'P'))
        {
            AccessibleName = Strings.Menu_Settings_Provider,
            AccessibleDescription = Strings.A11y_Menu_Provider_Desc
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
                if (AppSettings.Current.GamepadProviderType != provider)
                {
                    AppSettings.Current.GamepadProviderType = provider;
                    AppSettings.Save();

                    AnnounceA11y(Strings.Msg_RestartRequired);

                    AskForRestart();
                }
            };

            tsmiProvider.DropDownItems.Add(item);
        }

        AddProviderItem(AppSettings.GamepadProvider.XInput);
        AddProviderItem(AppSettings.GamepadProvider.GameInput);

        tsmiProvider.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem tsmiResetProvider = new(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'))
        {
            AccessibleName = Strings.Btn_SetDefault
        };
        tsmiResetProvider.Click += (s, e) =>
        {
            if (AppSettings.Current.GamepadProviderType != AppSettings.GamepadProvider.XInput)
            {
                AppSettings.Current.GamepadProviderType = AppSettings.GamepadProvider.XInput;
                AppSettings.Save();

                AnnounceA11y(Strings.Msg_RestartRequired);

                AskForRestart();
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
            AccessibleName = Strings.Btn_SetDefault
        };
        tsmiResetGamepad.Click += (s, e) =>
        {
            AppSettings.Current.ThumbDeadzoneEnter = 7849;
            AppSettings.Current.ThumbDeadzoneExit = 2500;
            AppSettings.Current.RepeatInitialDelayFrames = 30;
            AppSettings.Current.RepeatIntervalFrames = 5;
            AppSettings.Save();

            RefreshMenu();

            FeedbackService.PlaySound(SystemSounds.Asterisk);

            // 告知手把設定已重置。
            AnnounceA11y(Strings.Msg_InputCleared);
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
            int? val = AskForValue(Strings.Settings_HistoryCapacity, AppSettings.Current.HistoryCapacity, 100, 1, 1000);

            if (val.HasValue &&
                val != AppSettings.Current.HistoryCapacity)
            {
                AppSettings.Current.HistoryCapacity = val.Value;
                AppSettings.Save();

                RefreshMenu();

                AskForRestart();
            }
        };
        tsmiSettings.DropDownItems.Add(tsmiCap);

        // 清除歷程。
        ToolStripMenuItem tsmiClearHistory = new(ControlExtensions.GetMnemonicText(Strings.Menu_ClearHistory, 'C'))
        {
            AccessibleName = Strings.Menu_ClearHistory,
            AccessibleDescription = Strings.Menu_ClearHistory_Desc
        };
        tsmiClearHistory.Click += (s, e) =>
        {
            _historyService?.Clear();

            // 清除後主動將焦點拉回輸入框，確保使用者能直接開始輸入。
            TBInput.Focus();

            FeedbackService.PlaySound(SystemSounds.Asterisk);

            AnnounceA11y(Strings.Msg_InputCleared);
        };

        // 離開。
        ToolStripMenuItem tsmiExit = new(ControlExtensions.GetMnemonicText(Strings.Menu_Exit, 'X'))
        {
            AccessibleName = Strings.Menu_Exit,
            AccessibleDescription = Strings.A11y_Menu_Exit_Desc
        };
        tsmiExit.Click += (s, e) => Close();

        _cmsInput = new ContextMenuStrip();

        // 根據目前視窗的 DPI 縮放係數來放大字體。
        // 使用浮點數以確保計算精準。
        float scale = DeviceDpi / 96.0f;

        // 安全取得基準字型，若系統未能提供則退避至預設字型。
        Font baseFont = SystemFonts.MessageBoxFont ?? DefaultFont;

        // 使用安全取得的 baseFont 來建立新字型。
        _cmsInput.Font = new Font(baseFont.FontFamily, 11f * scale, FontStyle.Regular);

        _cmsInput.Items.Add(_tsmiPrivacyMode);
        _cmsInput.Items.Add(tsmiHotkeySettings);
        _cmsInput.Items.Add(tsmiSettings);
        _cmsInput.Items.Add(new ToolStripSeparator());
        _cmsInput.Items.Add(tsmiClearHistory);
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
                if (_isThemeUpdatePending &&
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
                    fullText = $"{label}: {AppSettings.Current.VibrationIntensity:F1}";
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
                    // 關鍵修正：套用 GetMnemonicText 以確保手把按鍵提示始終顯示於末尾。
                    mi.Text = mnemonic != ' ' ?
                        ControlExtensions.GetMnemonicText(fullText, mnemonic) :
                        fullText;

                    // 同步更新無障礙名稱，確保手把導覽時能播報目前數值。
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
                            AppSettings.Current.VibrationIntensity.ToString("F1") :
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
}