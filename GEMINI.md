# InputBox Workspace Instructions (Gemini CLI)

本檔案為 Gemini CLI 專用的精簡版專案指令。請在執行任務時嚴格遵守以下核心紅線與工程規範。若需要完整的架構理由、A11y 細節、術語表或控制器 API 規範，請**務必**參考 [ENGINEERING_GUIDELINES.md](ENGINEERING_GUIDELINES.md) 並使用 `read_file` 讀取。

## 0. 規劃與研究準則 (Planning & Research)
- **主動研究**：在開始任何非瑣碎 (Non-trivial) 的修改、功能開發或錯誤修復前，**必須**先使用 `read_file` 完整讀取 [ENGINEERING_GUIDELINES.md](ENGINEERING_GUIDELINES.md)。
- **合規驗證**：在撰寫計畫書 (/plan) 時，需明確指出修改內容如何符合 A11y 視覺安全、併發鎖定機制與安全性紅線。

## 1. 安全性與行為紅線 (Safety Boundaries)
- **嚴禁實作**：輸入注入、記憶體修改、封包攔截、同步多帳號或遊戲自動化。
- **僅限剪貼簿**：不模擬按鍵至其他視窗，僅允許將內容複製至剪貼簿。
- **不偵測第三方**：禁止主動偵測特定第三方應用程式（如遊戲或防護軟體）的狀態。

## 2. 執行環境與指令碼
- **預設環境**：Windows (`win32`)。
- **編碼設定**：在執行任何終端指令前，必須先確保環境編碼為 UTF-8，例如在 PowerShell 執行：
  ```powershell
  [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
  [Console]::InputEncoding = [System.Text.Encoding]::UTF8
  ```
- **建置驗證**：完成修改後，至少執行一次 `dotnet build src/InputBox/InputBox.csproj --configuration Debug` 進行驗證。

## 3. 核心工程規範 (Engineering Rules)
- **非同步安全**：嚴格使用 `async/await`，禁止 `async void` (事件處理器除外，但需包裝完整 `try-catch`)。
- **UI 執行緒**：跨執行緒操作必須使用 `SafeInvoke` / `SafeBeginInvoke`，或在 async 流程中使用 `await control.InvokeAsync(...)`。
- **資源管理**：終止非同步任務或清理資源時，使用原子化處置，如 `Interlocked.Exchange(ref _field, null)?.CancelAndDispose()`。
- **鎖定機制**：必須宣告專用的 `System.Threading.Lock` 物件進行鎖定，嚴禁直接 lock 集合或控制項。
- **P/Invoke**：優先使用 `LibraryImport` 並維持 `DefaultDllImportSearchPaths(System32)` 的安全邊界。

## 4. A11y 與視覺安全 (A11y Rules)
- **廣播機制**：動態播報優先走 `MainForm.AnnounceA11y`。必須尊重隱私模式，不得在隱私模式下播報實際字元。
- **視覺回饋**：所有自訂 UI 狀態變更（懸停、焦點、警示）**禁止僅依賴顏色**，需結合形狀、反轉或厚度變化。高對比模式下使用系統色，還原預設配色時使用 `Color.Empty`。
- **動畫安全**：Flash Alert 與 Dwell 動畫律動必須維持 1Hz / 平滑正弦波 / **零佈局抖動** (不得透過改變尺寸、Margin 或 Padding 製造回饋)。
- **字型資源**：A11y 共享字型只能透過 `MainForm.GetSharedA11yFont(...)` 取得並以歸零引用處理，禁止由個別視窗手動 `Dispose()`。

## 5. 參考文件
- 詳細工程、設計與術語規範：[ENGINEERING_GUIDELINES.md](ENGINEERING_GUIDELINES.md)
- GitHub Copilot 專用指令：[.github/copilot-instructions.md](.github/copilot-instructions.md)
- 專案說明與設定：[README.md](README.md)
