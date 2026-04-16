# InputBox 工程規範目錄 (Engineering Standards)

為了提升 AI Agent (Gemini & Copilot) 的檢索效率與 Context 管理，本專案已將工程規範拆解為原子化的技能模組。

## 核心規範檔案
- [環境與編碼 (Environment)](docs/engineering/environment.md)
- [核心工程 (.NET/Async/Lock)](docs/engineering/core-engineering.md)
- [A11y 無障礙與視覺安全 (A11y)](docs/engineering/a11y-safety.md)
- [遊戲控制器 API (Gamepad)](docs/engineering/gamepad-api.md)
- [在地化與術語表 (Localization)](docs/engineering/localization.md)
- [Git 提交規範與安全性紅線 (Git/Safety)](docs/engineering/git-commit-safety.md)（包含 Conventional Commits、GPG 簽章提交、main-first 分支守門與合規紅線）

## AI 技能位置
- **Gemini CLI**: `.agents/skills/inputbox-dev/`（目前共用）
- **GitHub Copilot / Others**: `.agents/skills/inputbox-dev/`

---
*註：若需人工閱讀，建議從上方清單選取對應主題。*
