using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using InputBox.Core.Configuration;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using Xunit;
using Xunit.Sdk;
using FlaUiApplication = FlaUI.Core.Application;
using FlaUiButton = FlaUI.Core.AutomationElements.Button;
using FlaUiTextBox = FlaUI.Core.AutomationElements.TextBox;

namespace InputBox.Tests;

/// <summary>
/// 使用 FlaUI 驗證 InputBox 主視窗、右鍵選單與核心互動流程。
/// 僅涵蓋 GitHub Actions 上相對穩定的 UI 冒煙測試，不取代既有邏輯單元測試。
/// </summary>
[Collection(UiSmokeTestRequirements.CollectionName)]
public sealed class MainFormUiSmokeTests : IDisposable
{
    /// <summary>
    /// 由測試啟動的受測應用程式執行個體。
    /// </summary>
    private readonly FlaUiApplication? _application;

    /// <summary>
    /// FlaUI 的 UIA3 自動化物件。
    /// </summary>
    private readonly UIA3Automation? _automation;

    /// <summary>
    /// 受測應用程式的主視窗。
    /// </summary>
    private readonly Window? _mainWindow;

    /// <summary>
    /// 最近一次成功完成的 UI 操作步驟，供失敗 Artifact 診斷使用。
    /// </summary>
    private string _lastUiStep = "UI smoke test initialized.";

    /// <summary>
    /// 建構子：在符合前置條件時啟動受測 WinForms 應用程式。
    /// </summary>
    public MainFormUiSmokeTests()
    {
        if (!UiSmokeTestRequirements.IsEnabled)
        {
            return;
        }

        string applicationPath = GetApplicationPath();

        CleanupStaleProcesses(applicationPath);

        _application = FlaUiApplication.Launch(applicationPath);
        _automation = new UIA3Automation();
        _mainWindow = WaitForMainWindow(_application, _automation);
    }

    /// <summary>
    /// GitHub Actions 或本機顯式啟用時，主視窗應可成功啟動並找到核心控制項。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 30000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void Launch_ShowsPrimaryControls()
    {
        RunWithFailureArtifacts(nameof(Launch_ShowsPrimaryControls), () =>
        {
            Assert.NotNull(_mainWindow);

            FlaUiTextBox inputTextBox = FindInputBox();
            FlaUiButton copyButton = FindCopyButton();

            Assert.True(_mainWindow!.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(_mainWindow.Title));
            Assert.True(inputTextBox.IsEnabled);
            Assert.True(copyButton.IsEnabled);
        });
    }

    /// <summary>
    /// 右鍵選單應可成功開啟，並顯示主要命令項目，表示自訂 ContextMenuStrip 已正確綁定。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 30000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void ContextMenu_Open_ShowsCoreCommands()
    {
        RunWithFailureArtifacts(nameof(ContextMenu_Open_ShowsCoreCommands), () =>
        {
            AutomationElement contextMenu = OpenContextMenu();
            AutomationElement privacyModeMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_PrivacyMode);
            AutomationElement hotkeySettingsMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_HotkeySettings);
            AutomationElement clearHistoryMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_ClearHistory);

            Assert.True(contextMenu.IsEnabled);
            Assert.True(privacyModeMenuItem.IsEnabled);
            Assert.True(hotkeySettingsMenuItem.IsEnabled);
            Assert.True(clearHistoryMenuItem.IsEnabled);
        });
    }

    /// <summary>
    /// 說明對話框應可從右鍵選單開啟並正常關閉，表示自訂 HelpDialog 的基本生命週期正常。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 30000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void HelpDialog_Open_And_CloseSuccessfully()
    {
        RunWithFailureArtifacts(nameof(HelpDialog_Open_And_CloseSuccessfully), () =>
        {
            AutomationElement contextMenu = OpenContextMenu();
            AutomationElement helpMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_Help);

            helpMenuItem.Click();

            Window helpDialog = FindDialogWindowByTitle(Strings.Help_Title, "找不到說明對話框視窗。");
            AutomationElement keyboardHeading = FindDescendantByLabel(helpDialog, ControlType.Text, Strings.Help_Section_Keyboard, "找不到說明對話框中的鍵盤區段標題。");
            FlaUiButton closeButton = FindDescendantByLabel(helpDialog, ControlType.Button, Strings.Help_Btn_Close, "找不到說明對話框中的關閉按鈕。").AsButton();

            Assert.True(helpDialog.IsEnabled);
            Assert.NotNull(keyboardHeading);
            Assert.True(closeButton.IsEnabled);

            closeButton.Invoke();

            WaitUntil(
                () => TryFindDialogWindowByTitle(Strings.Help_Title) == null,
                TimeSpan.FromSeconds(10),
                "說明對話框未在預期時間內關閉。"
            );
        });
    }

    /// <summary>
    /// 返回時最小化的確認對話框應可開啟並取消，表示自訂 GamepadMessageBox 的基本開關流程正常。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 30000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void MinimizeOnReturn_ConfirmDialog_Open_And_CancelSuccessfully()
    {
        RunWithFailureArtifacts(nameof(MinimizeOnReturn_ConfirmDialog_Open_And_CancelSuccessfully), () =>
        {
            AutomationElement contextMenu = OpenContextMenu();
            AutomationElement minimizeOnReturnMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_MinimizeOnReturn);

            minimizeOnReturnMenuItem.Click();

            Window confirmDialog = FindDialogWindowByTitle(Strings.Msg_MinimizeOnReturn_Confirm_Title, "找不到返回時最小化的確認對話框視窗。");
            FlaUiButton cancelButton = FindDescendantByLabel(confirmDialog, ControlType.Button, Strings.Btn_Cancel, "找不到確認對話框中的取消按鈕。").AsButton();

            Assert.True(confirmDialog.IsEnabled);
            Assert.True(cancelButton.IsEnabled);

            cancelButton.Invoke();

            WaitUntil(
                () => TryFindDialogWindowByTitle(Strings.Msg_MinimizeOnReturn_Confirm_Title) == null,
                TimeSpan.FromSeconds(10),
                "確認對話框未在預期時間內關閉。"
            );
        });
    }

    /// <summary>
    /// 片語子選單應可展開並顯示管理、匯入與匯出命令，表示動態重建的片語選單可正常互動。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 30000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void PhraseSubMenu_Open_ShowsManagementCommands()
    {
        RunWithFailureArtifacts(nameof(PhraseSubMenu_Open_ShowsManagementCommands), () =>
        {
            AutomationElement contextMenu = OpenContextMenu();
            AutomationElement phrasesMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_Phrases);

            phrasesMenuItem.Click();

            AutomationElement manageMenuItem = FindAnyOpenMenuItemByLabel(Strings.Menu_ManagePhrases);
            AutomationElement exportMenuItem = FindAnyOpenMenuItemByLabel(Strings.Menu_ExportPhrases);
            AutomationElement importMenuItem = FindAnyOpenMenuItemByLabel(Strings.Menu_ImportPhrases);

            Assert.True(manageMenuItem.IsEnabled);
            Assert.True(exportMenuItem.IsEnabled);
            Assert.True(importMenuItem.IsEnabled);
        });
    }

    /// <summary>
    /// 片語管理視窗應可從片語子選單開啟並正常關閉，表示主要片語管理流程入口正常。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 30000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void PhraseManagerDialog_Open_And_CloseSuccessfully()
    {
        RunWithFailureArtifacts(nameof(PhraseManagerDialog_Open_And_CloseSuccessfully), () =>
        {
            AutomationElement contextMenu = OpenContextMenu();
            AutomationElement phrasesMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_Phrases);

            phrasesMenuItem.Click();

            AutomationElement manageMenuItem = FindAnyOpenMenuItemByLabel(Strings.Menu_ManagePhrases);
            manageMenuItem.Click();

            Window phraseDialog = FindDialogWindowByTitle(Strings.Phrase_Title, "找不到片語管理對話框視窗。");
            AutomationElement phraseList = FindDescendantByLabel(phraseDialog, ControlType.List, Strings.Phrase_A11y_List_Name, "找不到片語管理對話框中的片語清單。");
            FlaUiButton closeButton = FindDescendantByLabel(phraseDialog, ControlType.Button, Strings.Phrase_Btn_Close, "找不到片語管理對話框中的關閉按鈕。").AsButton();

            Assert.True(phraseDialog.IsEnabled);
            Assert.NotNull(phraseList);
            Assert.True(closeButton.IsEnabled);

            closeButton.Invoke();

            WaitUntil(
                () => TryFindDialogWindowByTitle(Strings.Phrase_Title) == null,
                TimeSpan.FromSeconds(10),
                "片語管理對話框未在預期時間內關閉。"
            );
        });
    }

    /// <summary>
    /// 片語編輯對話框應可從片語管理視窗開啟並取消，表示新增片語入口的基本生命週期正常。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 60000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void PhraseEditDialog_Open_And_CancelSuccessfully()
    {
        RunWithFailureArtifacts(nameof(PhraseEditDialog_Open_And_CancelSuccessfully), () =>
        {
            AutomationElement contextMenu = OpenContextMenu();
            AutomationElement phrasesMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_Phrases);

            phrasesMenuItem.Click();

            AutomationElement manageMenuItem = FindAnyOpenMenuItemByLabel(Strings.Menu_ManagePhrases);
            manageMenuItem.Click();

            Window phraseDialog = FindDialogWindowByTitle(Strings.Phrase_Title, "找不到片語管理對話框視窗。");
            FlaUiButton addButton = FindDescendantByLabel(phraseDialog, ControlType.Button, Strings.Phrase_Btn_Add, "找不到片語管理對話框中的新增按鈕。").AsButton();
            FlaUiButton managerCloseButton = FindDescendantByLabel(phraseDialog, ControlType.Button, Strings.Phrase_Btn_Close, "找不到片語管理對話框中的關閉按鈕。").AsButton();

            Assert.True(addButton.IsEnabled);

            addButton.Click();

            Window editDialog = FindDialogWindowByTitle(Strings.Phrase_Edit_Title_Add, "找不到片語編輯對話框視窗。");
            FlaUiButton cancelButton = FindDescendantByLabel(editDialog, ControlType.Button, Strings.Phrase_Btn_Cancel, "找不到片語編輯對話框中的取消按鈕。").AsButton();

            Assert.True(editDialog.IsEnabled);
            Assert.True(cancelButton.IsEnabled);

            cancelButton.Invoke();

            WaitUntil(
                () => TryFindDialogWindowByTitle(Strings.Phrase_Edit_Title_Add) == null,
                TimeSpan.FromSeconds(10),
                "片語編輯對話框未在預期時間內關閉。"
            );

            managerCloseButton.Invoke();

            WaitUntil(
                () => TryFindDialogWindowByTitle(Strings.Phrase_Title) == null,
                TimeSpan.FromSeconds(10),
                "片語管理對話框未在預期時間內關閉。"
            );
        });
    }

    /// <summary>
    /// 在輸入文字後按下複製按鈕，輸入框應被清空，表示基本主流程成功完成且未崩潰。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 30000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void CopyButton_Click_ClearsInput()
    {
        RunWithFailureArtifacts(nameof(CopyButton_Click_ClearsInput), () =>
        {
            FlaUiTextBox inputTextBox = FindInputBox();
            FlaUiButton copyButton = FindCopyButton();

            ActivateMainWindow();

            inputTextBox.Text = "UI smoke test";
            Assert.Equal("UI smoke test", inputTextBox.Text);

            copyButton.Invoke();

            WaitUntil(
                () => string.IsNullOrEmpty(inputTextBox.Text),
                TimeSpan.FromSeconds(10),
                "按下複製按鈕後，輸入框未在預期時間內被清空。"
            );
        });
    }

    /// <summary>
    /// 當使用者在程式內確認重新啟動後，新的主視窗應自動回到前景，避免焦點落回先前的外部視窗。
    /// </summary>
    [Fact(
        Skip = "Requires Windows interactive desktop and INPUTBOX_RUN_UI_TESTS=1.",
        Timeout = 60000,
        SkipUnless = nameof(UiSmokeTestRequirements.IsEnabled),
        SkipType = typeof(UiSmokeTestRequirements))]
    [Trait("Category", "UI")]
    public void RestartPrompt_ConfirmYes_RelaunchedWindowStaysForeground()
    {
        RunWithFailureArtifacts(nameof(RestartPrompt_ConfirmYes_RelaunchedWindowStaysForeground), () =>
        {
            Assert.NotNull(_application);
            Assert.NotNull(_mainWindow);

            string applicationPath = GetApplicationPath();
            int originalProcessId = _application!.ProcessId;
            AppSettings.GamepadProvider originalProvider = AppSettings.Current.GamepadProviderType;
            string targetProvider = originalProvider == AppSettings.GamepadProvider.XInput ?
                AppSettings.GamepadProvider.GameInput.ToString() :
                AppSettings.GamepadProvider.XInput.ToString();

            try
            {
                AutomationElement contextMenu = OpenContextMenu();
                AutomationElement settingsMenuItem = FindMenuItemByLabel(contextMenu, Strings.Menu_Settings);
                settingsMenuItem.Click();

                AutomationElement gamepadMenuItem = FindAnyOpenMenuItemByLabel(Strings.Menu_Settings_Gamepad);
                gamepadMenuItem.Click();

                AutomationElement providerMenuItem = FindAnyOpenMenuItemByLabel(Strings.Menu_Settings_Provider);
                providerMenuItem.Click();

                AutomationElement targetProviderMenuItem = FindAnyOpenMenuItemByLabel(targetProvider);
                targetProviderMenuItem.Click();

                Window restartDialog = FindDialogWindowByTitle(Strings.Wrn_Title, "找不到重新啟動確認對話框視窗。");
                FlaUiButton yesButton = FindDescendantByLabel(restartDialog, ControlType.Button, Strings.Btn_Yes, "找不到重新啟動確認對話框中的確認按鈕。").AsButton();

                yesButton.Click();

                WaitUntil(
                    () =>
                    {
                        try
                        {
                            using Process originalProcess = Process.GetProcessById(originalProcessId);
                            return originalProcess.HasExited;
                        }
                        catch (ArgumentException)
                        {
                            return true;
                        }
                    },
                    TimeSpan.FromSeconds(15),
                    "重新啟動前的舊執行個體未在預期時間內結束。"
                );

                using UIA3Automation restartedAutomation = new();
                using FlaUiApplication restartedApplication = WaitForReplacementApplication(applicationPath, originalProcessId);

                Window restartedWindow = WaitForMainWindow(restartedApplication, restartedAutomation);

                WaitUntil(
                    () =>
                    {
                        nint restartedHandle = restartedWindow.Properties.NativeWindowHandle.Value;
                        return restartedHandle != 0 && User32.ForegroundWindow == restartedHandle;
                    },
                    TimeSpan.FromSeconds(10),
                    "重新啟動後的主視窗未在預期時間內保持前景。"
                );

                try
                {
                    restartedWindow.Close();
                }
                catch
                {

                }
            }
            finally
            {
                AppSettings.Current.GamepadProviderType = originalProvider;
                AppSettings.Save();
            }
        });
    }

    /// <summary>
    /// 關閉 UI 測試啟動的應用程式與自動化資源。
    /// </summary>
    public void Dispose()
    {
        RestartActivationCoordinator.Shared.ClearPendingActivationRequest();

        _automation?.Dispose();

        if (_application == null)
        {
            return;
        }

        try
        {
            if (!_application.HasExited)
            {
                _mainWindow?.Close();
            }
        }
        catch
        {
        }

        try
        {
            using Process process = Process.GetProcessById(_application.ProcessId);
            if (!process.WaitForExit(5000))
            {
                _application.Kill();
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 記錄目前正在執行的 UI 步驟，供失敗診斷輸出最後成功進度。
    /// </summary>
    /// <param name="stepDescription">目前完成或正在進行的步驟描述。</param>
    private void MarkStep(string stepDescription)
    {
        _lastUiStep = $"{DateTime.UtcNow:O} | {stepDescription}";
        Console.WriteLine($"[UI Smoke] {stepDescription}");
    }

    /// <summary>
    /// 將受測主視窗帶到前景並取得焦點，避免桌面自動化誤作用到編輯器或其他程式。
    /// </summary>
    private void ActivateMainWindow()
    {
        if (_mainWindow == null)
        {
            return;
        }

        try
        {
            nint handle = _mainWindow.Properties.NativeWindowHandle.Value;
            if (handle != 0)
            {
                _ = User32.ShowWindow(handle, User32.ShowWindowCommand.Restore);
                _ = User32.BringWindowToTop(handle);
                _ = User32.SetForegroundWindow(handle);
            }

            _mainWindow.Focus();
        }
        catch
        {
        }
    }

    /// <summary>
    /// 尋找輸入區容器。
    /// </summary>
    /// <returns>承載輸入框並綁定右鍵選單的容器元素。</returns>
    private AutomationElement FindInputHost()
    {
        return WaitForElement(
            () => _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("PInputHost")),
            TimeSpan.FromSeconds(10),
            "找不到輸入區容器 PInputHost。"
        );
    }

    /// <summary>
    /// 尋找主輸入框。
    /// </summary>
    /// <returns>已成功解析的 FlaUI 文字輸入框物件。</returns>
    private FlaUiTextBox FindInputBox()
    {
        AutomationElement? element = WaitForElement(
            () => _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("TBInput")),
            TimeSpan.FromSeconds(10),
            "找不到主輸入框 TBInput。"
        );

        return element.AsTextBox();
    }

    /// <summary>
    /// 依對話框標題尋找由受測應用程式開啟的頂層視窗。
    /// </summary>
    /// <param name="dialogTitle">目前 UI 語系下的對話框標題。</param>
    /// <param name="failureMessage">等待逾時後要拋出的錯誤訊息。</param>
    /// <returns>成功找到的對話框視窗。</returns>
    private Window FindDialogWindowByTitle(string dialogTitle, string failureMessage)
    {
        MarkStep($"Waiting for dialog: {dialogTitle}");

        Window dialogWindow = WaitForWindow(
            () => TryFindDialogWindowByTitle(dialogTitle),
            TimeSpan.FromSeconds(10),
            failureMessage);

        MarkStep($"Dialog ready: {dialogTitle}");

        return dialogWindow;
    }

    /// <summary>
    /// 嘗試依標題尋找由受測應用程式開啟的對話框。
    /// </summary>
    /// <param name="dialogTitle">目前 UI 語系下的對話框標題。</param>
    /// <returns>符合條件的視窗；若找不到則回傳 <see langword="null" />。</returns>
    private Window? TryFindDialogWindowByTitle(string dialogTitle)
    {
        if (_automation == null || _application == null)
        {
            return null;
        }

        nint handle = User32.FindWindow(null, dialogTitle);
        if (handle == 0 || !User32.IsWindow(handle))
        {
            return null;
        }

        _ = User32.GetWindowThreadProcessId(handle, out uint processId);
        if (processId != _application.ProcessId)
        {
            return null;
        }

        try
        {
            return _automation.FromHandle(handle).AsWindow();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在指定的對話框內依目前語系標籤尋找控制項。
    /// </summary>
    /// <param name="rootElement">要搜尋的對話框根元素。</param>
    /// <param name="controlType">目標控制項類型。</param>
    /// <param name="labelText">來自資源檔的目前語系標籤文字。</param>
    /// <param name="failureMessage">等待逾時後要拋出的錯誤訊息。</param>
    /// <returns>成功找到的控制項元素。</returns>
    private AutomationElement FindDescendantByLabel(AutomationElement rootElement, ControlType controlType, string labelText, string failureMessage)
    {
        return WaitForElement(
            () => FindFirstDescendantContainingText(rootElement, controlType, labelText),
            TimeSpan.FromSeconds(10),
            failureMessage);
    }

    /// <summary>
    /// 在指定根元素下搜尋第一個名稱包含目標文字的控制項。
    /// </summary>
    /// <param name="rootElement">要搜尋的根元素。</param>
    /// <param name="controlType">目標控制項類型。</param>
    /// <param name="labelText">目前語系對應的標籤文字。</param>
    /// <returns>第一個符合條件的控制項；若找不到則回傳 <see langword="null" />。</returns>
    private static AutomationElement? FindFirstDescendantContainingText(AutomationElement? rootElement, ControlType controlType, string labelText)
    {
        if (rootElement == null)
        {
            return null;
        }

        foreach (AutomationElement element in rootElement.FindAllDescendants())
        {
            if (element.ControlType != controlType)
            {
                continue;
            }

            string elementName = element.Name ?? string.Empty;
            if (elementName.Contains(labelText, StringComparison.CurrentCultureIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>
    /// 僅在 InputBox 主視窗內開啟右鍵選單，避免本地執行時誤干擾 VS Code 或其他前景程式。
    /// </summary>
    /// <returns>已成功開啟且可供互動的內容選單元素。</returns>
    private AutomationElement OpenContextMenu()
    {
        AutomationElement inputHost = FindInputHost();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            MarkStep($"Opening context menu (attempt {attempt + 1}).");
            ActivateMainWindow();

            try
            {
                inputHost.Focus();
            }
            catch
            {
            }

            inputHost.RightClick();

            AutomationElement? contextMenu = TryWaitForElement(TryFindContextMenu, TimeSpan.FromSeconds(3));
            if (contextMenu != null)
            {
                MarkStep("Context menu opened.");
                return contextMenu;
            }
        }

        throw new XunitException("找不到已開啟的右鍵選單。");
    }

    /// <summary>
    /// 尋找已開啟且屬於受測應用程式的右鍵選單。
    /// </summary>
    /// <returns>目前可供互動的內容選單元素。</returns>
    private AutomationElement FindContextMenu()
    {
        return WaitForElement(
            TryFindContextMenu,
            TimeSpan.FromSeconds(10),
            "找不到已開啟的右鍵選單。"
        );
    }

    /// <summary>
    /// 嘗試尋找屬於受測應用程式的右鍵選單，避免誤抓到編輯器或其他程式的選單。
    /// </summary>
    /// <returns>符合條件的內容選單元素；若找不到則回傳 <see langword="null" />。</returns>
    private AutomationElement? TryFindContextMenu()
    {
        if (_automation == null)
        {
            return null;
        }

        foreach (AutomationElement menuElement in _automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)))
        {
            if (!BelongsToApplication(menuElement))
            {
                continue;
            }

            if (FindFirstMenuItemContainingText(menuElement, Strings.Menu_PrivacyMode) != null ||
                FindFirstMenuItemContainingText(menuElement, Strings.Menu_Phrases) != null ||
                FindFirstMenuItemContainingText(menuElement, Strings.Menu_Help) != null)
            {
                return menuElement;
            }
        }

        return null;
    }

    /// <summary>
    /// 依目前 UI 語系的選單標籤尋找已開啟選單中的項目，避免寫死特定語言字串。
    /// </summary>
    /// <param name="menuElement">已開啟的右鍵選單元素。</param>
    /// <param name="labelText">來自資源檔的目前語系標籤文字。</param>
    /// <returns>成功找到的選單項目元素。</returns>
    private AutomationElement FindMenuItemByLabel(AutomationElement menuElement, string labelText)
    {
        return WaitForElement(
            () => FindFirstMenuItemContainingText(menuElement, labelText),
            TimeSpan.FromSeconds(10),
            $"找不到選單項目：{labelText}。"
        );
    }

    /// <summary>
    /// 從目前桌面上所有已開啟選單中尋找指定標籤的項目，供片語子選單等巢狀選單使用。
    /// </summary>
    /// <param name="labelText">來自資源檔的目前語系標籤文字。</param>
    /// <returns>成功找到的選單項目元素。</returns>
    private AutomationElement FindAnyOpenMenuItemByLabel(string labelText)
    {
        return WaitForElement(
            () => FindAnyMenuItemContainingText(labelText),
            TimeSpan.FromSeconds(10),
            $"找不到已開啟選單中的項目：{labelText}。"
        );
    }

    /// <summary>
    /// 尋找複製按鈕。
    /// </summary>
    /// <returns>已成功解析的 FlaUI 按鈕物件。</returns>
    private FlaUiButton FindCopyButton()
    {
        AutomationElement? element = WaitForElement(
            () => _mainWindow?.FindFirstDescendant(cf => cf.ByAutomationId("BtnCopy")),
            TimeSpan.FromSeconds(10),
            "找不到複製按鈕 BtnCopy。"
        );

        return element.AsButton();
    }


    /// <summary>
    /// 自指定選單中搜尋第一個包含目標標籤文字的項目。
    /// </summary>
    /// <param name="menuElement">要搜尋的內容選單元素。</param>
    /// <param name="labelText">目前 UI 語系對應的標籤文字。</param>
    /// <returns>第一個符合條件的選單項目；若找不到則回傳 <see langword="null" />。</returns>
    private static AutomationElement? FindFirstMenuItemContainingText(AutomationElement? menuElement, string labelText)
    {
        if (menuElement == null)
        {
            return null;
        }

        foreach (AutomationElement childElement in menuElement.FindAllChildren())
        {
            if (childElement.ControlType != ControlType.MenuItem)
            {
                continue;
            }

            string elementName = childElement.Name ?? string.Empty;
            if (elementName.Contains(labelText, StringComparison.CurrentCultureIgnoreCase))
            {
                return childElement;
            }
        }

        return null;
    }

    /// <summary>
    /// 自桌面上所有已開啟的選單中搜尋第一個包含目標標籤文字的項目。
    /// </summary>
    /// <param name="labelText">目前 UI 語系對應的標籤文字。</param>
    /// <returns>第一個符合條件的選單項目；若找不到則回傳 <see langword="null" />。</returns>
    private AutomationElement? FindAnyMenuItemContainingText(string labelText)
    {
        if (_automation == null)
        {
            return null;
        }

        foreach (AutomationElement menuElement in _automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)))
        {
            if (!BelongsToApplication(menuElement))
            {
                continue;
            }

            AutomationElement? menuItem = FindFirstMenuItemContainingText(menuElement, labelText);
            if (menuItem != null)
            {
                return menuItem;
            }
        }

        return null;
    }

    /// <summary>
    /// 列舉目前屬於受測 InputBox 行程的頂層視窗。
    /// </summary>
    /// <returns>所有屬於 InputBox.exe 的頂層視窗元素。</returns>
    private IEnumerable<AutomationElement> EnumerateApplicationWindows()
    {
        if (_automation == null)
        {
            yield break;
        }

        foreach (AutomationElement windowElement in _automation.GetDesktop().FindAllChildren(cf => cf.ByControlType(ControlType.Window)))
        {
            if (BelongsToApplication(windowElement))
            {
                yield return windowElement;
            }
        }
    }

    /// <summary>
    /// 判斷指定 UI 元素是否屬於目前受測的 InputBox 行程。
    /// </summary>
    /// <param name="element">要判斷的 UI 自動化元素。</param>
    /// <returns>若元素屬於受測行程則為 <see langword="true" />；否則為 <see langword="false" />。</returns>
    private bool BelongsToApplication(AutomationElement element)
    {
        if (_application == null)
        {
            return true;
        }

        try
        {
            int processId = element.Properties.ProcessId.Value;
            return processId == _application.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 依目前測試輸出組態推算受測程式的可執行檔路徑。
    /// </summary>
    /// <returns>受測 WinForms 應用程式的可執行檔完整路徑。</returns>
    private static string GetApplicationPath()
    {
        DirectoryInfo testOutputDirectory = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string buildConfiguration = testOutputDirectory.Parent?.Name ?? "Debug";
        string targetFramework = testOutputDirectory.Name;
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        string exePath = Path.Combine(
            repositoryRoot,
            "src",
            "InputBox",
            "bin",
            buildConfiguration,
            targetFramework,
            "InputBox.exe");

        if (!File.Exists(exePath))
        {
            throw new XunitException($"找不到受測程式：{exePath}");
        }

        return exePath;
    }

    /// <summary>
    /// 等待主視窗建立完成。
    /// </summary>
    /// <param name="application">已啟動的受測應用程式。</param>
    /// <param name="automation">用於抓取主視窗的 FlaUI 自動化物件。</param>
    /// <returns>已成功建立並可供互動的主視窗。</returns>
    private static Window WaitForMainWindow(FlaUiApplication application, UIA3Automation automation)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(15))
        {
            Window? window = application.GetMainWindow(automation);
            if (window != null)
            {
                return window;
            }

            Thread.Sleep(100);
        }

        throw new XunitException("InputBox 主視窗未在預期時間內出現。");
    }

    /// <summary>
    /// 等待重新啟動後的新 InputBox 行程出現，並附加到新的執行個體。
    /// </summary>
    /// <param name="applicationPath">受測程式的可執行檔完整路徑。</param>
    /// <param name="originalProcessId">重啟前舊執行個體的行程識別碼，用於排除舊程序。</param>
    /// <returns>已成功附加的新應用程式執行個體。</returns>
    private static FlaUiApplication WaitForReplacementApplication(string applicationPath, int originalProcessId)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(15))
        {
            foreach (Process process in Process.GetProcessesByName("InputBox"))
            {
                try
                {
                    if (process.Id == originalProcessId)
                    {
                        continue;
                    }

                    string? processPath = process.MainModule?.FileName;

                    if (!string.Equals(processPath, applicationPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return FlaUiApplication.Attach(process.Id);
                }
                catch
                {

                }
                finally
                {
                    process.Dispose();
                }
            }

            Thread.Sleep(100);
        }

        throw new XunitException("重新啟動後的新 InputBox 執行個體未在預期時間內出現。");
    }

    /// <summary>
    /// 反覆等待直到找到指定頂層視窗。
    /// </summary>
    /// <param name="windowFactory">每次輪詢時用來取得視窗的委派。</param>
    /// <param name="timeout">最長等待時間。</param>
    /// <param name="failureMessage">等待逾時後要拋出的錯誤訊息。</param>
    /// <returns>成功找到的視窗。</returns>
    private static Window WaitForWindow(
        Func<Window?> windowFactory,
        TimeSpan timeout,
        string failureMessage)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            Window? window = windowFactory();
            if (window != null)
            {
                return window;
            }

            Thread.Sleep(100);
        }

        throw new XunitException(failureMessage);
    }

    /// <summary>
    /// 在指定時間內嘗試取得 UI 元素，若逾時則回傳 <see langword="null" />。
    /// </summary>
    /// <param name="elementFactory">每次輪詢時用來取得 UI 元素的委派。</param>
    /// <param name="timeout">最長等待時間。</param>
    /// <returns>成功找到的 UI 元素；若逾時則回傳 <see langword="null" />。</returns>
    private static AutomationElement? TryWaitForElement(
        Func<AutomationElement?> elementFactory,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            AutomationElement? element = elementFactory();
            if (element != null)
            {
                return element;
            }

            Thread.Sleep(100);
        }

        return null;
    }

    /// <summary>
    /// 反覆等待直到找到指定 UI 元素。
    /// </summary>
    /// <param name="elementFactory">每次輪詢時用來取得 UI 元素的委派。</param>
    /// <param name="timeout">最長等待時間。</param>
    /// <param name="failureMessage">等待逾時後要拋出的錯誤訊息。</param>
    /// <returns>成功找到的 UI 元素。</returns>
    private static AutomationElement WaitForElement(
        Func<AutomationElement?> elementFactory,
        TimeSpan timeout,
        string failureMessage)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            AutomationElement? element = elementFactory();
            if (element != null)
            {
                return element;
            }

            Thread.Sleep(100);
        }

        throw new XunitException(failureMessage);
    }

    /// <summary>
    /// 反覆等待指定條件為 true。
    /// </summary>
    /// <param name="condition">要輪詢判斷的條件委派。</param>
    /// <param name="timeout">最長等待時間。</param>
    /// <param name="failureMessage">等待逾時後要拋出的錯誤訊息。</param>
    private static void WaitUntil(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(100);
        }

        throw new XunitException(failureMessage);
    }

    /// <summary>
    /// 在測試失敗時擷取桌面畫面與診斷文字，方便 GitHub Actions 回傳 Artifact。
    /// </summary>
    /// <param name="testName">目前執行中的測試名稱。</param>
    /// <param name="testAction">實際要執行的測試邏輯。</param>
    private void RunWithFailureArtifacts(string testName, Action testAction)
    {
        try
        {
            MarkStep($"{testName}: started");
            testAction();
            MarkStep($"{testName}: completed");
        }
        catch (Exception exception)
        {
            SaveFailureArtifacts(testName, exception);
            throw;
        }
    }

    /// <summary>
    /// 將 UI 失敗的螢幕擷圖與例外資訊寫入測試結果目錄。
    /// </summary>
    /// <param name="testName">目前執行中的測試名稱。</param>
    /// <param name="exception">觸發失敗的例外物件。</param>
    private void SaveFailureArtifacts(string testName, Exception exception)
    {
        string artifactDirectory = GetArtifactDirectory();
        string safeName = string.Concat(testName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string artifactPrefix = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}_{safeName}";
        string screenshotPath = Path.Combine(artifactDirectory, artifactPrefix + ".png");
        string diagnosticsPath = Path.Combine(artifactDirectory, artifactPrefix + ".txt");

        string windowTitle;

        try
        {
            windowTitle = _mainWindow?.Title ?? "<null>";
        }
        catch (Exception titleException)
        {
            windowTitle = $"<unavailable: {titleException.GetType().Name}: {titleException.Message}>";
        }

        List<string> diagnostics =
        [
            $"test={testName}",
            $"utc={DateTime.UtcNow:O}",
            $"windowTitle={windowTitle}",
            $"lastStep={_lastUiStep}",
            $"processId={_application?.ProcessId.ToString(CultureInfo.InvariantCulture) ?? "<null>"}",
            $"osVersion={Environment.OSVersion}",
            $"userInteractive={Environment.UserInteractive}",
            $"culture={CultureInfo.CurrentCulture.Name}",
            $"uiCulture={CultureInfo.CurrentUICulture.Name}",
            $"exception={exception}"
        ];

        try
        {
            Screen primaryScreen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            Rectangle screenBounds = primaryScreen.Bounds;

            using Bitmap bitmap = new(screenBounds.Width, screenBounds.Height);
            using Graphics graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
            bitmap.Save(screenshotPath, ImageFormat.Png);

            diagnostics.Add($"screenshot={screenshotPath}");
            Console.WriteLine($"[UI Smoke] Failure screenshot saved: {screenshotPath}");
        }
        catch (Exception captureException)
        {
            diagnostics.Add($"screenshot-error={captureException}");
        }

        diagnostics.Add("applicationWindows:");
        diagnostics.AddRange(GetApplicationWindowDiagnostics());
        diagnostics.Add("openMenus:");
        diagnostics.AddRange(GetOpenMenuDiagnostics());

        File.WriteAllLines(diagnosticsPath, diagnostics);
        Console.WriteLine($"[UI Smoke] Failure diagnostics saved: {diagnosticsPath}");
    }

    /// <summary>
    /// 取得 UI 冒煙測試失敗產物的輸出目錄。
    /// </summary>
    /// <returns>已確保存在的 Artifact 目錄完整路徑。</returns>
    private static string GetArtifactDirectory()
    {
        string artifactDirectory = Environment.GetEnvironmentVariable("INPUTBOX_UI_ARTIFACT_DIR") ??
            Path.Combine(AppContext.BaseDirectory, "TestResults", "UiArtifacts");

        Directory.CreateDirectory(artifactDirectory);

        return artifactDirectory;
    }

    /// <summary>
    /// 收集屬於受測 InputBox 行程的所有頂層視窗資訊，協助判斷失敗當下的視窗狀態。
    /// </summary>
    /// <returns>適合直接寫入 Artifact 文字檔的視窗描述清單。</returns>
    private IEnumerable<string> GetApplicationWindowDiagnostics()
    {
        bool hasWindow = false;

        foreach (AutomationElement windowElement in EnumerateApplicationWindows())
        {
            hasWindow = true;
            yield return "  " + DescribeElement(windowElement);
        }

        if (!hasWindow)
        {
            yield return "  <none>";
        }
    }

    /// <summary>
    /// 收集目前已開啟且屬於 InputBox 的選單與其子項目摘要，方便追查選單互動失敗原因。
    /// </summary>
    /// <returns>適合直接寫入 Artifact 文字檔的選單描述清單。</returns>
    private IEnumerable<string> GetOpenMenuDiagnostics()
    {
        if (_automation == null)
        {
            yield return "  <automation unavailable>";
            yield break;
        }

        bool hasMenu = false;

        foreach (AutomationElement menuElement in _automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)))
        {
            if (!BelongsToApplication(menuElement))
            {
                continue;
            }

            hasMenu = true;
            List<string> itemNames = [];

            try
            {
                foreach (AutomationElement childElement in menuElement.FindAllChildren())
                {
                    if (childElement.ControlType == ControlType.MenuItem)
                    {
                        itemNames.Add(SafeGetElementName(childElement));
                    }
                }
            }
            catch (Exception menuException)
            {
                itemNames.Add($"<items unavailable: {menuException.GetType().Name}: {menuException.Message}>");
            }

            yield return $"  {DescribeElement(menuElement)} items=[{string.Join(" | ", itemNames)}]";
        }

        if (!hasMenu)
        {
            yield return "  <none>";
        }
    }

    /// <summary>
    /// 安全地描述 UI 元素的關鍵屬性，避免診斷流程再次因 UIA 逾時而失敗。
    /// </summary>
    /// <param name="element">要輸出的 UI 元素。</param>
    /// <returns>包含名稱、AutomationId、類型、啟用狀態與行程識別碼的摘要字串。</returns>
    private static string DescribeElement(AutomationElement? element)
    {
        return $"name={SafeGetElementName(element)}, automationId={SafeGetElementAutomationId(element)}, controlType={SafeGetElementControlType(element)}, enabled={SafeGetElementEnabled(element)}, processId={SafeGetElementProcessId(element)}";
    }

    /// <summary>
    /// 安全取得 UI 元素名稱。
    /// </summary>
    /// <param name="element">要讀取名稱的元素。</param>
    /// <returns>元素名稱；若不可用則回傳診斷用佔位字串。</returns>
    private static string SafeGetElementName(AutomationElement? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        try
        {
            return string.IsNullOrWhiteSpace(element.Name) ? "<empty>" : element.Name;
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.GetType().Name}: {exception.Message}>";
        }
    }

    /// <summary>
    /// 安全取得 UI 元素的 AutomationId。
    /// </summary>
    /// <param name="element">要讀取 AutomationId 的元素。</param>
    /// <returns>AutomationId；若不可用則回傳診斷用佔位字串。</returns>
    private static string SafeGetElementAutomationId(AutomationElement? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        try
        {
            return string.IsNullOrWhiteSpace(element.AutomationId) ? "<empty>" : element.AutomationId;
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.GetType().Name}: {exception.Message}>";
        }
    }

    /// <summary>
    /// 安全取得 UI 元素控制項類型。
    /// </summary>
    /// <param name="element">要讀取類型的元素。</param>
    /// <returns>控制項類型名稱；若不可用則回傳診斷用佔位字串。</returns>
    private static string SafeGetElementControlType(AutomationElement? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        try
        {
            return element.ControlType.ToString();
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.GetType().Name}: {exception.Message}>";
        }
    }

    /// <summary>
    /// 安全取得 UI 元素是否啟用。
    /// </summary>
    /// <param name="element">要讀取狀態的元素。</param>
    /// <returns>元素啟用狀態；若不可用則回傳診斷用佔位字串。</returns>
    private static string SafeGetElementEnabled(AutomationElement? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        try
        {
            return element.IsEnabled.ToString();
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.GetType().Name}: {exception.Message}>";
        }
    }

    /// <summary>
    /// 安全取得 UI 元素的行程識別碼。
    /// </summary>
    /// <param name="element">要讀取行程識別碼的元素。</param>
    /// <returns>行程識別碼；若不可用則回傳診斷用佔位字串。</returns>
    private static string SafeGetElementProcessId(AutomationElement? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        try
        {
            return element.Properties.ProcessId.Value.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.GetType().Name}: {exception.Message}>";
        }
    }

    /// <summary>
    /// 清理同一測試輸出路徑下的殘留 InputBox 行程，避免單一執行個體互相干擾。
    /// </summary>
    /// <param name="applicationPath">目前測試預期啟動的執行檔完整路徑。</param>
    private static void CleanupStaleProcesses(string applicationPath)
    {
        foreach (Process process in Process.GetProcessesByName("InputBox"))
        {
            try
            {
                string? processPath = process.MainModule?.FileName;
                if (!string.Equals(processPath, applicationPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
            }
        }
    }
}