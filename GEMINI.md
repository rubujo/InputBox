# InputBox Workspace Instructions (Gemini CLI)

本檔案為 Gemini CLI 專用的精簡版專案指令。執行任何開發任務時，**必須**優先啟動專屬技能以獲取詳細規範。

## 0. 技能與規範載入 (Skills & Guidelines)
- **核心技能**：執行任務前，請務必執行 `activate_skill("inputbox-dev")`。
- **主動研究**：在開始修改前，請依據技能指引，讀取 `docs/engineering/` 下相關的規範檔案。

## 1. 安全性與行為紅線 (Safety Boundaries)
- 載入技能後，請嚴格遵守 `docs/engineering/git-commit-safety.md` 中的安全紅線。
- **嚴禁**：輸入注入、記憶體修改、封包攔截、自動化遊戲行為。
- **僅限**：剪貼簿操作。

## 2. 執行環境
- **編碼設定**：執行指令前，必須先確保環境編碼為 UTF-8：
  ```powershell
  [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
  ```
- **建置驗證**：完成修改後執行 `dotnet build src/InputBox/InputBox.csproj --configuration Debug`。

## 3. 參考索引
- 原子化工程規範：`docs/engineering/`
- GitHub Copilot 專用指令：`.github/copilot-instructions.md`
- 專案說明：`README.md`
