# InputBox Workspace Instructions

本檔只保留「每次任務都應套用」的精簡規則。詳細工程理由、術語表與完整約束請參考 [GEMINI.md](../GEMINI.md)；建置與發佈細節請參考 [README.md](../README.md)。

## Project Context

- 專案為 Windows WinForms 應用程式，目標框架為 `.NET 10`。
- 主要專案路徑為 `src/InputBox/InputBox.csproj`。
- 預設工作環境為 Windows；執行終端命令前應先切換 UTF-8。

## Build And Validate

- PowerShell 先執行：
  ```powershell
  [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
  [Console]::InputEncoding = [System.Text.Encoding]::UTF8
  ```
- 主要驗證命令：`dotnet build src/InputBox/InputBox.csproj --configuration Debug`
- 完成修改後至少執行一次建置；若異動 A11y、資源或對話框，應特別檢查相關檔案是否仍可編譯。

## Engineering Rules

- 嚴格使用 `async/await`；除事件處理器外不得使用 `async void`。
- 事件處理器必須包含完整 `try-catch`，避免 UI 執行緒崩潰。
- 背景執行緒更新 UI 時使用 `SafeInvoke`、`SafeBeginInvoke`，在 async 流程中優先使用 `await control.InvokeAsync(...)` 或現有 `SafeInvokeAsync` 包裝。
- 存取 `CancellationTokenSource?` 的 Token 一律使用 `?.Token ?? CancellationToken.None`。
- 終止非同步工作時使用 `Interlocked.Exchange(ref field, null)?.CancelAndDispose()`；不要只 `Cancel()`。
- 使用 `System.Threading.Lock` 作為專用鎖，不要直接鎖定集合或控制項。
- P/Invoke 優先使用 `LibraryImport`，並維持 `DefaultDllImportSearchPaths(System32)` 的安全邊界。

## A11y Rules

- 動態播報優先走 `MainForm.AnnounceA11y`；不要直接繞過既有廣播器呼叫 UIA。
- 所有 `AccessibleRole.Grouping` 容器都要補齊 `AccessibleName` 與 `AccessibleDescription`。
- 必須尊重 `AppSettings.Current.A11yInterruptEnabled` 與隱私模式，不得在隱私模式下播報選取內容或被刪除的實際字元。
- 高對比模式下使用系統色；還原預設配色時使用 `Color.Empty`，不要硬編碼 `SystemColors.Control`。
- Flash Alert 與 Dwell 動畫必須維持 1Hz / 正弦波 / 零抖動原則，不得透過改變尺寸、Margin 或 Padding 製造回饋。

## Resource Rules

- A11y 字型必須透過 `MainForm.GetSharedA11yFont(...)` 取得。
- 共享字型只能歸零引用，不能由個別視窗手動 `Dispose()`。
- 新增 `.resx` 字串時，所有語系都要補齊，且每個 `<data>` 都要有同語系 `<comment>`。

## Safety Boundaries

- 嚴禁實作輸入注入、記憶體注入、封包互動、同步多帳號或遊戲自動化。
- 程式只允許複製到剪貼簿，不得模擬輸入到其他視窗。
- 不得主動偵測或鎖定特定第三方應用程式狀態。

## References

- 詳細工程規範：[GEMINI.md](../GEMINI.md)
- 使用與設定說明：[README.md](../README.md)
