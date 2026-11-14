# InputBox.Tests 🧪

[![作業系統](https://img.shields.io/badge/作業系統-Windows-003A6D?style=for-the-badge)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/Runtime-.NET%2011-512BD4?logo=dotnet&logoColor=white&style=for-the-badge)](https://dotnet.microsoft.com/)
[![測試框架](https://img.shields.io/badge/Test%20Framework-xUnit%20v3-5C2D91?style=for-the-badge)](https://xunit.net/)
[![UI%20Smoke](https://img.shields.io/badge/Desktop%20UI-FlaUI-2E8B57?style=for-the-badge)](https://github.com/FlaUI/FlaUI)

本文件說明 InputBox 測試專案的覆蓋範圍、執行方式、UI 冒煙測試與第三方測試相依授權揭露。

## 一、測試範圍 📋

| 測試類別 | 被測目標 | 測試數 |
|---|---|---|
| `AnnouncementServiceTests` | `AnnouncementService` 訊息排隊、Dispose 行為與關閉時背景工作退出保護，以及 `AnnouncerLabel` 必要 A11y 預設值回歸保護 | 6 |
| `AppSettingsTests` | `AppSettings` 關鍵常數、Clamp 行為、遊戲控制器調校快照，以及設定檔實際保存／讀回、併發保存、暫存清理、外來暫存檔保留與併發暫存檔誤刪回歸保護 | 52 |
| `CmdKeyDispatcherTests` | `CmdKeyDispatcher` 對右鍵選單混合輸入的鍵盤命令轉譯與全域快捷鍵分派回歸保護 | 7 |
| `DialogLayoutHelperTests` | `DialogLayoutHelper` 對話框版面輔助方法 | 9 |
| `DialogLabelStabilityTests` | 片語管理／編輯對話框的動態計數標籤固定寬度、完整數字可視與零抖動回歸保護 | 3 |
| `PhraseManagerDialogGamepadTests` | `PhraseManagerDialog` 左側片語清單的 LB/RB/LT/RT 快速切換、邊界跳轉、焦點接手與非必要連發抑制回歸保護 | 3 |
| `FloatingPointFormatConverterTests` | `FloatingPointFormatConverter` 字串轉換 | 16 |
| `FormInputStateManagerTests` | `FormInputStateManager` 輸入狀態切換 | 15 |
| `GamepadDeadzoneHysteresisTests` | `GamepadDeadzoneHysteresis.ResolveDirection`（int / float 多載），含正負方向對稱性守門（硬體平等原則） | 14 |
| `GamepadControllerFactoryTests` | 控制器後端建立策略，驗證 XInput 預設路徑、GameInput 成功路徑，以及 GameInput 執行階段不可用時退避至 XInput | 3 |
| `GamepadControllerPauseTests` | 控制器在 `Pause()` / `Resume()`、連線可用性語意、GameInput 讀取缺失斷線重列舉中斷與暫態輸入清理、斷線快取與連發計數清除、探索權杖取消保護、空裝置清單防護、非同步分階段裝置探索、初始化快速取消死鎖防護、探索所有權與競態防護、世代分配隔離、過期呼叫者工作保護、探索清理移交追蹤、熱插拔版本防覆蓋與請求共用同步、探索失敗重試保留、裝置狀態過濾、震動停止安全性、`ClearAllEvents` 肩鍵釋放訂閱清除與原生對話框切換時的殘留輸入回歸保護 | 30 |
| `GameInputDirectUsageTests` | `GameInputGamepadController` 直接使用 `InputWeave.GameInput` 遊戲控制器介面的守門、callback function pointer marshaling、官方 v3 按鍵位元、快照邊緣偵測、Release 日誌邊界、穩定裝置識別與震動參數保留 | 9 |
| `FlashAlertAnimatorTests` | `FlashAlertAnimator` 共用閃爍警示：主題警示色、純淨底色至警示色的插值、整個脈衝過程文字對比 ≥4.5:1、高對比模式半強度切換，以及 1Hz 單次正弦脈衝強度 | 7 |
| `GamepadBiasSmoothingTests` | `GamepadBiasSmoothing` 自適應 EMA 學習率公式：零誤差用基礎值、超出範圍用最大值，以及 XInput 與 GameInput 不同數值尺度下結果一致 | 5 |
| `GamepadCalibrationVisualizerMapperTests` | `GamepadCalibrationVisualizerMapper` 對校準視覺化座標限制、死區半徑換算、D-Pad 導覽防誤觸，以及雙搖桿狀態／控制器連線文案格式化的回歸保護 | 14 |
| `GamepadEventBinderTests` | `GamepadEventBinder` 的 LB / RB / LT / RT 與肩鍵放開事件綁定回歸保護 | 1 |
| `GamepadFaceButtonProfileTests` | `GamepadFaceButtonProfile` 的 Auto 解析、手動覆寫優先權、GameInput 裝置識別保留 VID/PID 時的 Sony/Nintendo 判斷，以及 Xbox / PlayStation / Nintendo 模式的按鍵標示、助記詞同步、資源化字串、主畫面說明文字、目前生效配置顯示、標題列提示、選單勾選邏輯與 PlayStation ○/× 確認模式回歸保護 | 25 |
| `GamepadShoulderShortcutArbiterTests` | `GamepadShoulderShortcutArbiter` 的肩鍵單按、連發、修飾鍵與雙肩鍵組合仲裁回歸保護 | 4 |
| `GamepadMappedDirectionGuardTests` | `GamepadMappedDirectionGuard` 全方向幽靈保護的封鎖／解除節奏 | 2 |
| `GamepadRepeatSettingsTests` | `GamepadRepeatSettings` 預設值與 `Validate()` | 7 |
| `GamepadRepeatStateMachineTests` | `GamepadRepeatStateMachine.AdvanceDirectionRepeat` / `AdvanceHeldRepeat` | 11 |
| `GamepadSignalEvaluatorTests` | `GamepadSignalEvaluator.IsActive` / `IsIdle`（int / float 多載） | 13 |
| `GaussianDelayHelperTests` | `GaussianDelayHelper` 延遲計算 | 5 |
| `GamepadMessageBoxTests` | `GamepadMessageBox` 關閉取消、生命週期資源保護與已連線控制器提示同步 | 5 |
| `InputBoxLayoutManagerTests` | `InputBoxLayoutManager` 版面管理 | 4 |
| `InputHistoryServiceTests` | `InputHistoryService` 歷程記錄 CRUD 與 5 筆翻頁導覽 | 15 |
| `LoggerServiceTests` | `LoggerService` 測試環境專屬日誌分流、Release-like 日誌門檻、環境變數覆寫與正式日誌隔離保護 | 8 |
| `MainFormUiSmokeTests` | `MainForm` 使用 FlaUI 驗證主視窗啟動、右鍵選單主要命令、設定中的控制器子選單、控制器校準視覺化對話框、片語子選單、片語管理視窗、片語編輯視窗、HelpDialog、返回時最小化確認對話框、程式內確認重啟後主視窗保持前景，以及基本複製流程的 UI 冒煙測試 | 11 |
| `PhraseServiceTests` | `PhraseService` CRUD、匯出／匯入、併發匯出、併發暫存檔誤刪（含 managed 暫存檔寬限期保留），以及持久化失敗時的記憶體復原回歸保護 | 40 |
| `RestartActivationCoordinatorTests` | `RestartActivationCoordinator` 的一次性重啟前景啟用標記、單次消費與過期清理保護；以及查詢待處理請求不消費標記、無標記或過期時不判定為交接中 | 5 |
| `RestartPromptStateTests` | 需重啟設定的待處理狀態追蹤、標題列提示，以及右鍵選單依 App 設定／系統變更／兩者同時存在而動態切換文案的回歸保護 | 7 |
| `RestartProcessLauncherTests` | `RestartProcessLauncher` 重啟啟動資訊：沿用目前執行檔、不透過殼層啟動、只轉送執行檔之後的引數並保留空白與引號（取代 `Application.Restart()` 以便只授權新程序前景的回歸保護）；以及附加單一執行個體交接參數、連續重啟時取代舊交接參數 | 6 |
| `RestartRequestDeciderTests` | 手動重啟與設定變更兩種入口的確認策略回歸保護 | 3 |
| `SingleInstanceHandoffTests` | `SingleInstanceHandoff` 程式內重啟交接：交接參數解析與移除、啟動動作矩陣（正常啟動／等待接手／喚醒既有實例）、喚醒失敗後的 fallback 判斷（前景被阻擋、交接進行中不得多開視窗）；對應 Mutex 提早釋放被搶先取得的競態 | 16 |
| `SystemHelperTests` | `SystemHelper.EvaluateGamescopeEnvironment` 在不同 DISPLAY、XDG_CURRENT_DESKTOP 與 DESKTOP_SESSION 環境變數組合下的 Gamescope 環境偵測回歸保護 | 11 |
| `TaskExtensionsTests` | `TaskExtensions` CTS 擴充方法與生命週期連結保護 | 12 |
| `TextBoxSelectionExtensionsTests` | 右搖桿延伸選取共用的 `ResolveSelectionAnchor` 錨點解析（無選取、正向、反向、錨點失效）與 `GetWordJumpTarget` 單字跳轉目標（不改變目前選取、方向與邊界）；原本三處複本集中後的一致性保護 | 6 |
| `VibrationPatternsTests` | `VibrationPatterns` 與方向性震動設定、語意情境解析、能力感知的多段式微震動序列，以及歷程滾輪阻尼感、字數上限硬牆、震動強度預覽、右搖桿選取粒度、組合鍵進入提示與喚起握手回饋的回歸保護 | 37 |
| `VibrationSafetyLimiterTests` | `VibrationSafetyLimiter` 熱保護、Duty Cycle 限制器與極端邊界保護；馬達數量熱成本倍率正規化，以及所有內建震動模式在冷啟動下不得被拒絕、單次超出預算改為降強度，以及Critical 自動連發（含不同模式交錯）降級、熱負載不失控，以及冷啟動短序列仍完整送出的回歸保護 | 20 |
| `ButtonFeedbackRenderingTests` | Latest 預設／一般按鈕實際文字與背景、焦點／預設動作同步及對比襯底、四種底色進度條與色覺紋理、粗體、按下亮度差、零抖動與配色還原 | 14 |
| **合計** | | **481** |

## 二、執行方式 🚀

```powershell
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj
```

加入詳細輸出（MTP）：

```powershell
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj --logger "console;verbosity=detailed"
```

收集 Code Coverage（MTP 原生模式，同 CI）：

```powershell
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj -c Release --no-build `
		--filter-not-trait "Category=UI" `
		--coverage `
		--coverage-output-format cobertura `
		--coverage-output coverage.cobertura.xml
```

執行 UI 冒煙測試（需顯式啟用，避免一般開發時誤啟動桌面應用程式）：

```powershell
$env:INPUTBOX_RUN_UI_TESTS = "1"
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj -c Release --no-build --filter-trait "Category=UI"
Remove-Item Env:INPUTBOX_RUN_UI_TESTS -ErrorAction SilentlyContinue
```

## 三、注意事項 ⚠️

### 1. Microsoft Testing Platform

本測試專案使用 `global.json` 指定：

```json
{
	"test": {
		"runner": "Microsoft.Testing.Platform"
	}
}
```

因此 `dotnet test` 會使用 Microsoft Testing Platform。Coverage 也應搭配 `Microsoft.Testing.Extensions.CodeCoverage`，不要改成 `coverlet.collector`。

### 2. PhraseService 測試的資料隔離

`PhraseService` 會讀寫使用者的 `%AppData%\InputBox\phrases.json`。
為了避免測試污染真實資料，相關測試類別採用以下策略：

- **建構子**：若檔案存在，複製至 `phrases.json.testbackup`
- **Dispose**：測試結束後自動還原備份（或刪除測試產生的檔案）

xUnit v3 為每個 `[Fact]` 建立獨立的測試類別實例，`IDisposable.Dispose()` 在每個測試後自動呼叫，確保各測試之間完全隔離。

`InputHistoryService` 是記憶體內歷程服務，不會讀寫使用者資料目錄，因此不需要檔案備份／還原。

### 3. 維護規則

只要此專案有**新增、刪除或調整測試案例**，請同步更新本 README 的測試範圍表與總數，避免文件與實際覆蓋率不一致。

### 4. 第三方測試函式庫與授權 📦

本測試專案會使用第三方函式庫作為測試框架、覆蓋率、WinForms UI 冒煙測試與 GameInput 介面守門用途。

> 下列名稱已對齊 [InputBox.Tests.csproj](InputBox.Tests.csproj) 中的 NuGet 套件名稱；若 GitHub 原始碼儲存庫名稱不同，會另外標示為「原始碼儲存庫」。

以下第三方元件之權利歸原作者所有，並遵循其各自之授權條款，不屬於主專案授權範圍：

- Microsoft.NET.Test.Sdk：原始碼儲存庫為 [microsoft/vstest](https://github.com/microsoft/vstest)，由 [Microsoft](https://github.com/microsoft) 及其 [貢獻者](https://github.com/microsoft/vstest/graphs/contributors) 開發並採用 [MIT License](https://github.com/microsoft/vstest/blob/main/LICENSE) 授權，作為測試專案建置與執行基礎。
- xunit.runner.visualstudio：原始碼儲存庫為 [xunit/visualstudio.xunit](https://github.com/xunit/visualstudio.xunit)，由 [xUnit](https://github.com/xunit) 及其 [貢獻者](https://github.com/xunit/visualstudio.xunit/graphs/contributors) 開發並採用 [Apache-2.0](https://github.com/xunit/visualstudio.xunit/blob/main/License.txt) 授權，提供 Visual Studio 與測試主機整合適配器。
- xunit.v3.mtp-v2：原始碼儲存庫為 [xunit/xunit](https://github.com/xunit/xunit)，由 [xUnit](https://github.com/xunit) 及其 [貢獻者](https://github.com/xunit/xunit/graphs/contributors) 開發並採用 [Apache-2.0](https://github.com/xunit/xunit/blob/main/LICENSE) 授權，作為 xUnit v3 與 Microsoft Testing Platform 整合套件。
- Microsoft.Testing.Extensions.CodeCoverage：原始碼儲存庫為 [microsoft/codecoverage](https://github.com/microsoft/codecoverage)，由 [Microsoft](https://github.com/microsoft) 及其 [貢獻者](https://github.com/microsoft/codecoverage/graphs/contributors) 開發並採用 [MIT License](https://github.com/microsoft/codecoverage/blob/main/LICENSE) 授權，用於測試覆蓋率收集。
- FlaUI.Core：原始碼儲存庫為 [FlaUI/FlaUI](https://github.com/FlaUI/FlaUI)，由 [Roman Baeriswyl](https://github.com/Roemer) 及其 [貢獻者](https://github.com/FlaUI/FlaUI/graphs/contributors) 開發並採用 [MIT License](https://github.com/FlaUI/FlaUI/blob/main/LICENSE.txt) 授權，作為 Windows UI 自動化核心函式庫。
- FlaUI.UIA3：原始碼儲存庫為 [FlaUI/FlaUI](https://github.com/FlaUI/FlaUI)，由 [Roman Baeriswyl](https://github.com/Roemer) 及其 [貢獻者](https://github.com/FlaUI/FlaUI/graphs/contributors) 開發並採用 [MIT License](https://github.com/FlaUI/FlaUI/blob/main/LICENSE.txt) 授權，作為 UIA3 後端，用於 WinForms UI 冒煙測試。
- InputWeave.GameInput 0.0.1：原始碼儲存庫固定於 [rubujo/InputWeave.GameInput 封裝來源](https://github.com/rubujo/InputWeave.GameInput/tree/da4ff730a1d52a8f4d21d43aba7408c59b50a7a2)，由 [rubujo](https://github.com/rubujo) 及其 [貢獻者](https://github.com/rubujo/InputWeave.GameInput/graphs/contributors) 開發並採用 [CC0 1.0 Universal](https://github.com/rubujo/InputWeave.GameInput/blob/da4ff730a1d52a8f4d21d43aba7408c59b50a7a2/LICENSE) 授權，作為 GameInput 直接使用與震動型別守門測試依賴；套件固定於 [../../eng/nuget/InputWeave.GameInput.0.0.1.nupkg](../../eng/nuget/InputWeave.GameInput.0.0.1.nupkg)，SHA256 為 `41aca5564e443497179313e02c0b1365d040386fcb1acc2cd106e1799253ce0b`。

本測試專案的相關說明詳見本文件；主專案授權與完整聲明仍以 [../../README.md](../../README.md) 及 [../../LICENSE](../../LICENSE) 為準。

### 5. UI 冒煙測試失敗產物

當 UI 冒煙測試在 GitHub Actions 失敗時，流程會自動輸出：

- 桌面擷圖 `.png`
- 對應例外與環境資訊 `.txt`
- 最後成功步驟 `lastStep`
- 屬於 InputBox 的 `applicationWindows` / `openMenus` 摘要

並作為 Artifact 上傳，方便排查 GitHub Actions 的 Windows 執行環境中可能出現的偶發 UI 問題。

> 目前 CI 會將這組 UI 冒煙測試視為**報告用途**：若失敗會留下警告與 Artifact，但**不阻擋**主建置、一般單元測試與覆蓋率檢查。

> 為避免 hosted runner 與本機 Agent 因自訂 modal dialog 的偵測不穩而長時間卡住，目前所有 UI 案例皆加上 fail-fast 逾時保護；dialog 案例會在此保護下持續驗證。

> UI 冒煙測試會將 xUnit 測試逾時的 `TestContext.Current.CancellationToken` 傳入共用執行流程，確保 xUnit v3 MTP v2 4.0.0 能在逾時後及時終止測試。

> UI 元素比對一律優先使用資源檔的目前語系字串與 AutomationId，而非寫死繁中或英文標籤，因此 Hosted Runner 的系統語系變動不應成為這組 smoke test 的失敗來源。

### 6. 目標框架

測試專案使用 `net11.0-windows`（需要 WinForms 執行環境），**僅支援 Windows**。

### UIA 搜尋範圍回歸保護

既有 11 項 UI 冒煙測試維持真實點擊與對話框生命週期檢查；先以 Win32 HWND／ProcessId 篩選可見頂層視窗，再搜尋 InputBox 視窗內與浮動選單，避免遍歷瀏覽器等無關 UIA 子樹造成延遲、選單失效與測試逾時。測試數量不變。

UI 測試宿主在啟動 UIA 前使用 Application.SetHighDpiMode(PerMonitorV2)，關閉受測視窗後才處置 UIA；初始視窗直接由受測程序 HWND 綁定，啟動失敗時清理該受測程序。設定 INPUTBOX_CAPTURE_UI=1 時，UiVisualSnapshot 會擷取主視窗及對話框，記錄 HWND 的原生 DPI、高對比旗標與視窗邊界，並檢查可見按鈕未超出視窗。可用 INPUTBOX_EXPECTED_DPI=144 等值要求實際 DPI 必須符合預期；圖片不做 DPI 模擬。

### .NET 11／Latest 原生 UI 矩陣（2026-11-14）

| Windows 縮放／實際 HWND DPI | 淺色 Classic | 深色 Dark | Windows 高對比 |
|---|---|---|---|
| 100%／96 | 11/11 | 11/11 | 11/11 |
| 125%／120 | 11/11 | 11/11 | 11/11 |
| 150%／144 | 11/11 | 11/11 | 11/11 |

矩陣使用同一實體顯示器的 Windows 原生縮放；隔離副本只覆寫啟動色彩模式以強制 Classic／Dark，保留 Latest 與 Deferred、所有 UI 與測試來源。appearance.txt 記錄 Application.IsDarkModeEnabled、SystemInformation.HighContrast 與 Application.DefaultVisualStylesMode，三者均符合預期；每一組保留主視窗、說明、片語管理／編輯、確認與校正視窗擷圖。原縮放 125% 與主題／高對比設定於結束後還原。

原始 .NET 10 測試亦可重現 NoClickablePointException（10/11）；限制搜尋範圍後，.NET 10、.NET 11 未啟用 Deferred、.NET 11 啟用 Deferred 的對照均為 11/11。後續納入 Win32 HWND 探索、PerMonitorV2 測試宿主與 UIA 處置順序修正，避免其他無回應 provider、座標虛擬化及殘留測試程序干擾驗證。

最終 dev 正式 System + Latest + Deferred 路徑於 125%／120 DPI 為 11/11，非 UI 回歸為 456/456，行覆蓋率 32.58%。Release 建置使用 -warnaserror 為 0 警告／0 錯誤；完整 style 與 analyzers 的 severity info 檢查報告皆為空清單。七語系資源的鍵名與格式預留位置一致；Xbox 使用模式術語已對齊文件與網站，應用程式資源未發現舊稱，控制器 Xbox 配置名稱維持既有語意。

合規查核（2026-11-14）：以可執行 JavaScript 的官方頁面確認 FFXIV 使用者合約、USERJOY 使用條款與隱私政策，並讀取第三方授權及免責聲明。此次輸入相關來源僅清理多餘初值、索引與必要回呼參數名稱，保持既有剪貼簿輸出邊界與控制器行為。

## .NET 11 Latest 按鈕配色回歸（2026-11-14）

`ButtonFeedbackRenderingTests` 是需 `INPUTBOX_RUN_UI_TESTS=1` 才執行的 14 項 UI 測試。它以實際點陣圖檢查預設／一般按鈕的焦點、懸停、按下背景、文字及焦點邊框，並檢查文字對比至少 4.5:1、尺寸／內距不變，以及回饋結束後的一般／停用外觀。停用狀態只檢查文字仍可辨識，不套用一般可操作文字的 4.5:1 門檻。

以獨立測試程序分別執行淺色與深色（測試主機設定 `INPUTBOX_BUTTON_TEST_DARK=1`）；高對比必須實際啟用 Windows 高對比，並在驗證後還原。`Latest` 在高對比下由框架解析為 `Classic`，測試保留此無障礙行為。這些測試只改變測試控制項的狀態，不向其他視窗送出模擬輸入。`INPUTBOX_BUTTON_ARTIFACTS` 可指定點陣圖輸出目錄。

缺陷來源是 Net11 的預設按鈕強調色會優先於自訂背景，但自訂文字色仍生效；回饋期間改為成對繪製既有背景與文字，再疊加既有焦點／按下提示。高對比使用配對的系統色，一般非高對比及停用狀態保留原生渲染。全域 Latest、System 色彩模式、Deferred 首次顯示與必要的主題重啟提示均保留。

參考：[.NET 11.0.0 ButtonDarkModeAdapter](https://github.com/dotnet/winforms/blob/v11.0.0/src/System.Windows.Forms/System/Windows/Forms/Controls/Buttons/ButtonInternal/DarkMode/ButtonDarkModeAdapter.cs)。

前一輪配色修正驗證：淺色、深色與實際 Windows 高對比各 8/8；完整 UI 19/19、非 UI 456/456；Release -warnaserror 建置 0 警告／0 錯誤。修改 C# 的 style／analyzers severity info 報告均為空；高對比設定已還原。暫時略過配色修正時，像素測試有 6 項失敗，確認可偵測原本缺陷。系統色變更會重套目前回饋；主題重啟提示相關程式碼未移除，既有回歸測試通過。

預設動作提示補強：輸入框與 AcceptButton 共用情境選色。Latest 按鈕本體保留原生強調色，提示邊框加入固定像素的對比襯底；不改變尺寸、Margin 或 Padding。回歸測試以實際邊框及緊鄰襯底像素驗證反轉 16.75／11.16:1、中性 8.14／至少 7.2:1；四種底色進度條主填色對比為 5.14／9.91／3.75／5.21:1，另檢查色覺紋理仍有實際像素。HatchBrush 的縮放抗鋸齒像素允許落在指定填色與紋理色的混色範圍；不將裝飾紋理每個像素誤當成主填色對比門檻。

測試另外驗證焦點粗體、按下第三層視覺及深色琥珀色 Tritanopia ΔL*≥13。文件比值顯示到小數兩位，數值斷言僅容許四捨五入的 0.005 差距；4.5:1 文字及 3:1 非文字最低門檻不因顯示取整而降低。

補強後最終驗證（實際 125%／120 DPI）：淺色、深色及 Windows 高對比各 14/14；完整 UI 25/25、非 UI 456/456；Release -warnaserror 建置 0 警告／0 錯誤；四個修改 C# 的 severity info style／analyzers 報告均為空。預設動作提示實測：淺色反轉 16.748:1／中性 8.139:1，深色反轉 11.161:1／中性 7.218:1，高對比 7.893:1。進度條四種底色實測 5.137／9.914／3.746／5.208:1，高對比 7.893:1。已還原 Windows 高對比狀態；必要重啟提示與其既有回歸測試保留。
