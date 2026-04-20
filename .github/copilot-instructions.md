# InputBox - GitHub Copilot 指引

本檔提供 GitHub Copilot 使用，特別是 **Visual Studio 的 Copilot Chat**。由於不同 Copilot 客戶端對 `AGENTS.md` 的支援程度不同，本檔保留為 Copilot 的穩定入口；共同規範仍以根目錄 [AGENTS.md](../AGENTS.md)、`.agents/skills/inputbox-dev/SKILL.md` 與 `docs/engineering/` 為準。

## 1. 讀取順序

開始任何任務前，請依序參考：

1. 本檔
2. 根目錄 `AGENTS.md`
3. `.agents/skills/inputbox-dev/SKILL.md`
4. `docs/engineering/` 下與任務相關的規範文件

## 2. Copilot 必守事項

- 嚴格遵守 `docs/engineering/git-commit-safety.md`。
- 禁止記憶體注入、封包修改、輸入模擬與自動化遊戲行為。
- 行為邊界僅限於剪貼簿，不得主動輸入到其他視窗。
- Git 提交必須使用使用者既有的 GPG 簽章設定；不得修改任何 GPG 或 gpg-agent 設定檔。
- 修改 `*.cs` 後，完成前必須修正新增的 IDE 與 CS 診斷。
- 所有異動都必須遵循 repo 根目錄 `.editorconfig`。

## 3. Copilot Chat 與 Agent 的適用策略

- 在 **Visual Studio** 內，請以本檔作為 Copilot Chat 的主要入口。
- 在 **VS Code** 與 **Copilot cloud agent** 內，除本檔外，也應讀取 `AGENTS.md`。
- 若任務涉及 UI、非同步、控制器、在地化、測試或 Git 提交，必須主動展開對應的 `docs/engineering/*.md`。

## 4. 常用驗證

```powershell
[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
dotnet build src/InputBox/InputBox.csproj --configuration Debug
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj
```
