# 開發環境與編碼規範 (Environment & Encoding)

- **預設作業系統**：Microsoft Windows。
- **編碼規範**：環境必須使用 **UTF-8 (Code Page 65001)**。
  - **PowerShell**：執行指令前必先設定 `[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8`。
  - **CMD**：執行 `chcp 65001`。
  - **編輯或新增 `*.cs` 檔案**：文字編碼一律使用 **UTF-8 with BOM**，換行一律使用 **CRLF**。
  - **編輯或新增 `*.resx` 檔案**：文字編碼一律使用 **UTF-8 with BOM**，換行一律使用 **CRLF**。
  - **編輯或新增其他文字檔案**：文字編碼一律使用 **UTF-8**，換行一律使用 **CRLF**。
- **Shell 優先順序**：
  1. PowerShell 7+ (pwsh)
  2. Windows PowerShell 5.1
  3. Command Prompt (cmd)
- **指令相容性**：優先使用與環境相容的內建指令 (如 `dir` 或 `Get-ChildItem`)。
