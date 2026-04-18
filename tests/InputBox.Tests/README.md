# InputBox.Tests 🧪

[![作業系統](https://img.shields.io/badge/作業系統-Windows-003A6D?style=for-the-badge)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/Runtime-.NET%2010-512BD4?logo=dotnet&logoColor=white&style=for-the-badge)](https://dotnet.microsoft.com/)
[![測試框架](https://img.shields.io/badge/Test%20Framework-xUnit%20v3-5C2D91?style=for-the-badge)](https://xunit.net/)
[![UI%20Smoke](https://img.shields.io/badge/Desktop%20UI-FlaUI-2E8B57?style=for-the-badge)](https://github.com/FlaUI/FlaUI)

本文件說明 InputBox 測試專案的覆蓋範圍、執行方式、UI 冒煙測試與第三方測試相依授權揭露。

## 一、測試範圍 📋

| 測試類別 | 被測目標 | 測試數 |
|---|---|---|
| `AnnouncementServiceTests` | `AnnouncementService` 訊息排隊、Dispose 行為與關閉時背景工作退出保護 | 5 |
| `AppSettingsTests` | `AppSettings` 關鍵常數、Clamp 行為、遊戲控制器調校快照，以及設定檔實際保存／讀回、併發保存、暫存清理與併發暫存檔誤刪回歸保護 | 51 |
| `CmdKeyDispatcherTests` | `CmdKeyDispatcher` 對右鍵選單混合輸入的鍵盤命令轉譯與原生確認鍵行為回歸保護 | 4 |
| `DialogLayoutHelperTests` | `DialogLayoutHelper` 對話框版面輔助方法 | 9 |
| `DialogLabelStabilityTests` | 片語管理／編輯對話框的動態計數標籤固定寬度、完整數字可視與零抖動回歸保護 | 3 |
| `PhraseManagerDialogGamepadTests` | `PhraseManagerDialog` 左側片語清單的 LB/RB/LT/RT 快速切換、邊界跳轉、焦點接手與非必要連發抑制回歸保護 | 3 |
| `FloatingPointFormatConverterTests` | `FloatingPointFormatConverter` 字串轉換 | 16 |
| `FormInputStateManagerTests` | `FormInputStateManager` 輸入狀態切換 | 15 |
| `GamepadDeadzoneHysteresisTests` | `GamepadDeadzoneHysteresis.ResolveDirection`（int / float 多載） | 12 |
| `GamepadControllerPauseTests` | 控制器在 `Pause()` / `Resume()`、連線可用性語意與原生對話框切換時的殘留輸入回歸保護 | 5 |
| `GamepadCalibrationVisualizerMapperTests` | `GamepadCalibrationVisualizerMapper` 對校準視覺化座標限制、死區半徑換算、D-Pad 導覽防誤觸，以及雙搖桿狀態／控制器連線文案格式化的回歸保護 | 9 |
| `GamepadEventBinderTests` | `GamepadEventBinder` 的 LB / RB / LT / RT 與肩鍵放開事件綁定回歸保護 | 1 |
| `GamepadFaceButtonProfileTests` | `GamepadFaceButtonProfile` 的 Auto 解析、手動覆蓋優先權，以及 Xbox / PlayStation / Nintendo 模式的按鍵標示、助記詞同步、資源化字串、主畫面說明文字、目前生效配置顯示、標題列提示、選單勾選邏輯與 PlayStation ○/× 確認模式回歸保護 | 13 |
| `GamepadShoulderShortcutArbiterTests` | `GamepadShoulderShortcutArbiter` 的肩鍵單按、連發、修飾鍵與雙肩鍵組合仲裁回歸保護 | 4 |
| `GamepadMappedDirectionGuardTests` | `GamepadMappedDirectionGuard` 全方向幽靈保護的封鎖／解除節奏 | 2 |
| `GamepadRepeatSettingsTests` | `GamepadRepeatSettings` 預設值與 `Validate()` | 7 |
| `GamepadRepeatStateMachineTests` | `GamepadRepeatStateMachine.AdvanceDirectionRepeat` / `AdvanceHeldRepeat` | 11 |
| `GamepadSignalEvaluatorTests` | `GamepadSignalEvaluator.IsActive` / `IsIdle`（int / float 多載） | 13 |
| `GaussianDelayHelperTests` | `GaussianDelayHelper` 延遲計算 | 5 |
| `GamepadMessageBoxTests` | `GamepadMessageBox` 關閉取消、生命週期資源保護與已連線控制器提示同步 | 2 |
| `InputBoxLayoutManagerTests` | `InputBoxLayoutManager` 版面管理 | 4 |
| `InputHistoryServiceTests` | `InputHistoryService` 歷程記錄 CRUD 與 5 筆翻頁導覽 | 15 |
| `LoggerServiceTests` | `LoggerService` 測試環境專屬日誌分流與正式日誌隔離保護 | 1 |
| `MainFormUiSmokeTests` | `MainForm` 使用 FlaUI 驗證主視窗啟動、右鍵選單主要命令、片語子選單、片語管理視窗、片語編輯視窗、HelpDialog、返回時最小化確認對話框、程式內確認重啟後主視窗保持前景，以及基本複製流程的 UI 冒煙測試 | 9 |
| `PhraseServiceTests` | `PhraseService` CRUD、匯出／匯入、併發匯出、併發暫存檔誤刪，以及持久化失敗時的記憶體回滾回歸保護 | 39 |
| `RestartActivationCoordinatorTests` | `RestartActivationCoordinator` 的一次性重啟前景啟用標記、單次消費與過期清理保護 | 3 |
| `RestartPromptStateTests` | 需重啟設定的待處理狀態追蹤、標題列提示，以及右鍵選單依 App 設定／系統變更／兩者同時存在而動態切換文案的回歸保護 | 7 |
| `RestartRequestDeciderTests` | 手動重啟與設定變更兩種入口的確認策略回歸保護 | 3 |
| `TaskExtensionsTests` | `TaskExtensions` CTS 擴充方法與生命週期連結保護 | 12 |
| `VibrationPatternsTests` | `VibrationPatterns` 與方向性震動設定、語意情境解析、能力感知的多段式微震動序列，以及歷程滾輪阻尼感、字數上限硬牆、震動強度預覽、右搖桿選取粒度、組合鍵進入提示與喚起握手回饋的回歸保護 | 30 |
| `VibrationSafetyLimiterTests` | `VibrationSafetyLimiter` 熱保護、Duty Cycle 限制器與極端邊界保護 | 8 |
| **合計** | | **337** |

## 二、執行方式 🚀

```powershell
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj
```

加入詳細輸出（MTP）：

```powershell
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj --logger "console;verbosity=detailed"
```

收集 Code Coverage（MTP 原生模式，同 CI）：

```powershell
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj -c Release --no-build `
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

### 2. PhraseService / InputHistoryService 測試的資料隔離

`PhraseService` 與 `InputHistoryService` 的方法會寫入使用者的 `%AppData%\InputBox\` 目錄下的 JSON 檔案。
為了避免測試污染真實資料，這些測試類別採用以下策略：

- **建構子**：若檔案存在，複製至 `phrases.json.testbackup`
- **Dispose**：測試結束後自動還原備份（或刪除測試產生的檔案）

xUnit v3 為每個 `[Fact]` 建立獨立的測試類別實例，`IDisposable.Dispose()` 在每個測試後自動呼叫，確保各測試之間完全隔離。

### 3. 維護規則

只要此專案有**新增、刪除或調整測試案例**，請同步更新本 README 的測試範圍表與總數，避免文件與實際覆蓋率不一致。

### 4. 第三方測試函式庫與授權 📦

本測試專案會使用第三方函式庫作為測試框架、Coverage 與 WinForms UI 冒煙測試用途。

> 下列名稱已對齊 [InputBox.Tests.csproj](InputBox.Tests.csproj) 中的 NuGet 套件名稱；若 GitHub 原始碼儲存庫名稱不同，會另外標示為「原始碼儲存庫」。

以下第三方元件之權利歸原作者所有，並遵循其各自之授權條款，不屬於主專案授權範圍：

- Microsoft.NET.Test.Sdk：原始碼儲存庫為 [microsoft/vstest](https://github.com/microsoft/vstest)，由 [Microsoft](https://github.com/microsoft) 及其 [貢獻者](https://github.com/microsoft/vstest/graphs/contributors) 開發並採用 [MIT License](https://github.com/microsoft/vstest/blob/main/LICENSE) 授權，作為測試專案建置與執行基礎。
- xunit.runner.visualstudio：原始碼儲存庫為 [xunit/visualstudio.xunit](https://github.com/xunit/visualstudio.xunit)，由 [xUnit](https://github.com/xunit) 及其 [貢獻者](https://github.com/xunit/visualstudio.xunit/graphs/contributors) 開發並採用 [Apache-2.0](https://github.com/xunit/visualstudio.xunit/blob/main/License.txt) 授權，提供 Visual Studio 與測試主機整合適配器。
- xunit.v3.mtp-v2：原始碼儲存庫為 [xunit/xunit](https://github.com/xunit/xunit)，由 [xUnit](https://github.com/xunit) 及其 [貢獻者](https://github.com/xunit/xunit/graphs/contributors) 開發並採用 [Apache-2.0](https://github.com/xunit/xunit/blob/main/LICENSE) 授權，作為 xUnit v3 與 Microsoft Testing Platform 整合套件。
- Microsoft.Testing.Extensions.CodeCoverage：原始碼儲存庫為 [microsoft/codecoverage](https://github.com/microsoft/codecoverage)，由 [Microsoft](https://github.com/microsoft) 及其 [貢獻者](https://github.com/microsoft/codecoverage/graphs/contributors) 開發並採用 [MIT License](https://github.com/microsoft/codecoverage/blob/main/LICENSE) 授權，用於測試覆蓋率收集。
- FlaUI.Core：原始碼儲存庫為 [FlaUI/FlaUI](https://github.com/FlaUI/FlaUI)，由 [Roman Baeriswyl](https://github.com/Roemer) 及其 [貢獻者](https://github.com/FlaUI/FlaUI/graphs/contributors) 開發並採用 [MIT License](https://github.com/FlaUI/FlaUI/blob/main/LICENSE.txt) 授權，作為 Windows UI 自動化核心函式庫。
- FlaUI.UIA3：原始碼儲存庫為 [FlaUI/FlaUI](https://github.com/FlaUI/FlaUI)，由 [Roman Baeriswyl](https://github.com/Roemer) 及其 [貢獻者](https://github.com/FlaUI/FlaUI/graphs/contributors) 開發並採用 [MIT License](https://github.com/FlaUI/FlaUI/blob/main/LICENSE.txt) 授權，作為 UIA3 後端，用於 WinForms UI 冒煙測試。

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

> UI 元素比對一律優先使用資源檔的目前語系字串與 AutomationId，而非寫死繁中或英文標籤，因此 Hosted Runner 的系統語系變動不應成為這組 smoke test 的失敗來源。

### 6. 目標框架

測試專案使用 `net10.0-windows`（需要 WinForms 執行環境），**僅支援 Windows**。
