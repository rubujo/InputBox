# InputBox 工作區指令 (GitHub Copilot)

本專案利用 **Agent Skills** 來強制執行工程標準。

## 0. 技能啟動
- **觸發條件**：開始任務時，請確保已載入 `inputbox-dev` 技能。
- **知識庫**：Copilot Agent 在執行修改前，應先參考 `docs/engineering/` 中的原子化規範。

## 1. 安全與合規
- 嚴格遵守 `docs/engineering/git-commit-safety.md`。
- **Git 提交必須使用使用者既有的 GPG 簽章設定**；Agent 嚴禁自行修改 `gpg.conf`、`gpg-agent.conf` 或其他相關設定檔。若簽章失敗，應提醒使用者自行處理其本機簽章環境，不得以停用簽章或自動改寫設定方式繞過。
- **嚴禁**：記憶體注入、封包修改、自動化或輸入模擬。
- **僅限**：僅允許剪貼簿操作。

## 2. 環境
- 在執行指令前，必須先設定 PowerShell 環境為 UTF-8 編碼：
  ```powershell
  [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
  ```
- 對每個檔案的異動，必須套用專案根目錄 `.editorconfig` 的格式、縮排、編碼與換行設定。
- 修改 `*.cs` 檔案後，完成前必須檢查並修正該檔案中的 IDE 與 CS 類型建議、警告或錯誤；不得留下新的診斷項目。
- 使用以下指令驗證異動：`dotnet build src/InputBox/InputBox.csproj --configuration Debug`

## 3. 參考資料
- 原子化標準：`docs/engineering/`
- Gemini CLI 指令：`GEMINI.md`
