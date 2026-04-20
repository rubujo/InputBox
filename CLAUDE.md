# InputBox - Claude Code 工作區指引

本檔案供 Claude Code 使用，作為專案入口說明。專案的共同規範以 [AGENTS.md](AGENTS.md)、`.agents/skills/inputbox-dev/SKILL.md` 與 `docs/engineering/` 為準；本檔僅保留 Claude Code 所需的最小相容層。

## 1. 載入順序

開始任何任務前，請依序執行：

1. 讀取根目錄 `AGENTS.md`
2. 載入 `.agents/skills/inputbox-dev/SKILL.md`
3. 依任務性質讀取 `docs/engineering/` 下的對應規範

若本檔與 `AGENTS.md` 有衝突，以 `AGENTS.md` 與更細部的工程規範為準。

## 2. Claude Code 專屬要求

- 使用 `CLAUDE.md` 作為 Claude Code 的專案記憶入口。
- 若 Claude Code 只自動讀取 `CLAUDE.md`，仍必須由本檔導向 `AGENTS.md` 與 `inputbox-dev`，不得把完整規範再複製一份到這裡。
- 若任務涉及 UI、非同步、資源管理、在地化、控制器、測試或 Git 提交，必須主動展開對應的 `docs/engineering/*.md`。

## 3. 不可違反的共同紅線

- 禁止記憶體注入、封包攔截、輸入模擬與自動化遊戲行為。
- 行為邊界僅限於剪貼簿，不得主動輸入到其他視窗。
- Git 提交必須使用使用者既有的 GPG 簽章設定；不得修改任何 GPG 或 gpg-agent 設定檔。
- 若任務涉及輸入、輸出、剪貼簿、快速鍵或控制器邏輯，必須依 `docs/engineering/git-commit-safety.md` 完成 ToS 合規檢查。

## 4. 常用驗證

```powershell
[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
dotnet build src/InputBox/InputBox.csproj --configuration Debug
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj
```
