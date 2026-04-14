# InputBox.Tests

InputBox 的單元測試專案，使用 [xUnit v3](https://xunit.net/) 撰寫。

## 測試範圍

| 測試類別 | 被測目標 | 測試數 |
|---|---|---|
| `AnnouncementServiceTests` | `AnnouncementService` 訊息排隊與 Dispose 行為 | 4 |
| `AppSettingsTests` | `AppSettings` 關鍵常數、Clamp 行為與設定檔併發保存回歸保護 | 46 |
| `DialogLayoutHelperTests` | `DialogLayoutHelper` 對話框版面輔助方法 | 9 |
| `FloatingPointFormatConverterTests` | `FloatingPointFormatConverter` 字串轉換 | 16 |
| `FormInputStateManagerTests` | `FormInputStateManager` 輸入狀態切換 | 15 |
| `GamepadDeadzoneHysteresisTests` | `GamepadDeadzoneHysteresis.ResolveDirection`（int / float 多載） | 12 |
| `GamepadControllerPauseTests` | 控制器在 `Pause()` / `Resume()` 與原生對話框切換時的殘留輸入回歸保護 | 2 |
| `GamepadRepeatSettingsTests` | `GamepadRepeatSettings` 預設值與 `Validate()` | 7 |
| `GamepadRepeatStateMachineTests` | `GamepadRepeatStateMachine.AdvanceDirectionRepeat` / `AdvanceHeldRepeat` | 11 |
| `GamepadSignalEvaluatorTests` | `GamepadSignalEvaluator.IsActive` / `IsIdle`（int / float 多載） | 13 |
| `GaussianDelayHelperTests` | `GaussianDelayHelper` 延遲計算 | 5 |
| `InputBoxLayoutManagerTests` | `InputBoxLayoutManager` 版面管理 | 4 |
| `InputHistoryServiceTests` | `InputHistoryService` 歷程記錄 CRUD | 13 |
| `PhraseServiceTests` | `PhraseService` CRUD、匯出／匯入與併發匯出回歸保護 | 36 |
| `TaskExtensionsTests` | `TaskExtensions` CTS 擴充方法與生命週期連結保護 | 12 |
| `VibrationPatternsTests` | `VibrationPatterns` 震動模式常數與行為 | 13 |
| `VibrationSafetyLimiterTests` | `VibrationSafetyLimiter` 熱保護與 Duty Cycle 限制器 | 6 |
| **合計** | | **224** |

## 執行測試

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
		--coverage `
		--coverage-output-format cobertura `
		--coverage-output coverage.cobertura.xml
```

## 注意事項

### Microsoft Testing Platform

本測試專案使用 `global.json` 指定：

```json
{
	"test": {
		"runner": "Microsoft.Testing.Platform"
	}
}
```

因此 `dotnet test` 會使用 Microsoft Testing Platform。Coverage 也應搭配 `Microsoft.Testing.Extensions.CodeCoverage`，不要改成 `coverlet.collector`。

### PhraseService / InputHistoryService 測試的資料隔離

`PhraseService` 與 `InputHistoryService` 的方法會寫入使用者的 `%AppData%\InputBox\` 目錄下的 JSON 檔案。
為了避免測試污染真實資料，這些測試類別採用以下策略：

- **建構子**：若檔案存在，複製至 `phrases.json.testbackup`
- **Dispose**：測試結束後自動還原備份（或刪除測試產生的檔案）

xUnit v3 為每個 `[Fact]` 建立獨立的測試類別實例，`IDisposable.Dispose()` 在每個測試後自動呼叫，確保各測試之間完全隔離。

### 維護規則

只要此專案有**新增、刪除或調整測試案例**，請同步更新本 README 的測試範圍表與總數，避免文件與實際覆蓋率不一致。

### 目標框架

測試專案使用 `net10.0-windows`（需要 WinForms 執行環境），**僅支援 Windows**。
