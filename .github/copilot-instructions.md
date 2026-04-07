# InputBox 工作區指令 (GitHub Copilot)

本專案利用 **Agent Skills** 來強制執行工程標準。

## 0. 技能啟動
- **觸發條件**：開始任務時，請確保已載入 `inputbox-dev` 技能。
- **知識庫**：Copilot Agent 在執行修改前，應先參考 `docs/engineering/` 中的原子化規範。

## 1. 安全與合規
- 嚴格遵守 `docs/engineering/git-commit-safety.md`。
- **嚴禁**：記憶體注入、封包修改、自動化或輸入模擬。
- **僅限**：僅允許剪貼簿操作。

## 2. 環境
- 在執行指令前，必須先設定 PowerShell 環境為 UTF-8 編碼：
  ```powershell
  [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
  ```
- 使用以下指令驗證異動：`dotnet build src/InputBox/InputBox.csproj --configuration Debug`

## 3. 參考資料
- 原子化標準：`docs/engineering/`
- Gemini CLI 指令：`GEMINI.md`
