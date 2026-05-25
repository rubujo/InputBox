# InputBox 工程規範目錄 (Engineering Standards)

為了提升 Codex CLI、Claude Code、GitHub Copilot CLI 與 Antigravity CLI 的檢索效率與 context 管理，本專案已將工程規範拆解為共同入口、project skill 與原子化工程文件。

## 核心規範檔案
- [環境與編碼 (Environment)](docs/engineering/environment.md)
- [核心工程 (.NET/Async/Lock)](docs/engineering/core-engineering.md)
- [A11y 無障礙與視覺安全 (A11y)](docs/engineering/a11y-safety.md)
- [遊戲控制器 API (Gamepad)](docs/engineering/gamepad-api.md)
- [在地化與術語表 (Localization)](docs/engineering/localization.md)
- [Git 提交規範與安全性紅線 (Git/Safety)](docs/engineering/git-commit-safety.md)（包含 Conventional Commits、GPG 簽章提交、main-first 分支守門與合規紅線）

## Agent 規範位置

- **共同入口**：`AGENTS.md`
- **權威技能**：`.agents/skills/inputbox-dev/SKILL.md`
- **Claude Code 橋接**：`CLAUDE.md` 與 `.claude/skills/inputbox-dev/SKILL.md`

---
*註：若需人工閱讀，建議從上方清單選取對應主題。*
