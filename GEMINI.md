# InputBox - Gemini CLI 工作區指引

本檔案供 Gemini CLI 使用，作為專案入口說明。專案的共同規範以 [AGENTS.md](AGENTS.md)、`.agents/skills/inputbox-dev/SKILL.md` 與 `docs/engineering/` 為準；本檔僅保留 Gemini CLI 所需的最小相容層。

## 1. 載入順序

開始任何任務前，請依序執行：

1. 讀取根目錄 `AGENTS.md`
2. 載入 `.agents/skills/inputbox-dev/SKILL.md`
3. 依任務性質讀取 `docs/engineering/` 下的對應規範

若本檔與 `AGENTS.md` 有衝突，以 `AGENTS.md` 與更細部的工程規範為準。

## 2. Gemini CLI 專屬要求

- 使用 `GEMINI.md` 作為 Gemini CLI 的預設 context 入口。
- 若 Gemini CLI 僅自動讀取 `GEMINI.md`，仍必須由本檔導向 `AGENTS.md` 與 `inputbox-dev`，不得把完整規範再複製一份到這裡。
- 若有設定可讓 Gemini CLI 額外讀取 `AGENTS.md`，應優先啟用，但不可假設所有使用環境都已配置。

## 3. 不可違反的共同紅線

- 禁止記憶體注入、封包攔截、輸入模擬與自動化遊戲行為。
- 行為邊界僅限於剪貼簿，不得主動輸入到其他視窗。
- Git 提交必須使用使用者既有的 GPG 簽章設定；不得修改任何 GPG 或 gpg-agent 設定檔。
- 若任務涉及輸入、輸出、剪貼簿、快速鍵或控制器邏輯，必須依 `docs/engineering/git-commit-safety.md` 完成 ToS 合規檢查。

## 4. 常用驗證

```powershell
[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
dotnet build src/InputBox/InputBox.csproj --configuration Debug
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj
```
