# InputBox.Tests

InputBox 的單元測試專案，使用 [xUnit v3](https://xunit.net/) 撰寫。

## 測試範圍

| 測試類別 | 被測目標 | 測試數 |
|---|---|---|
| `AppSettingsTests` | `AppSettings` 關鍵常數（安全邊界、A11y 上限） | 7 |
| `GamepadDeadzoneHysteresisTests` | `GamepadDeadzoneHysteresis.ResolveDirection`（int / float 多載） | 12 |
| `GamepadRepeatSettingsTests` | `GamepadRepeatSettings` 預設值與 `Validate()` | 7 |
| `GamepadRepeatStateMachineTests` | `GamepadRepeatStateMachine.AdvanceDirectionRepeat` / `AdvanceHeldRepeat` | 11 |
| `GamepadSignalEvaluatorTests` | `GamepadSignalEvaluator.IsActive` / `IsIdle`（int / float 多載） | 12 |
| `PhraseServiceTests` | `PhraseService` CRUD（Add / Update / Remove / MoveUp / MoveDown） | 23 |
| **合計** | | **72** |

## 執行測試

```powershell
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj
```

加入詳細輸出：

```powershell
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj --logger "console;verbosity=detailed"
```

## 注意事項

### PhraseService 測試的資料隔離

`PhraseService` 的 CRUD 方法會寫入使用者的 `%AppData%\InputBox\phrases.json`。
為了避免測試污染真實資料，`PhraseServiceTests` 採用以下策略：

- **建構子**：若檔案存在，複製至 `phrases.json.testbackup`
- **Dispose**：測試結束後自動還原備份（或刪除測試產生的檔案）

xUnit v3 為每個 `[Fact]` 建立獨立的測試類別實例，`IDisposable.Dispose()` 在每個測試後自動呼叫，確保各測試之間完全隔離。

### 目標框架

測試專案使用 `net10.0-windows`（需要 WinForms 執行環境），**僅支援 Windows**。
