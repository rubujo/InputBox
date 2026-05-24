# InputBox - GitHub Copilot CLI 指引

本檔是 GitHub Copilot CLI 與 Copilot repository-wide custom instructions 的入口。GitHub 官方文件指出 Copilot CLI 會使用 `.github/copilot-instructions.md`，也會讀取 root `AGENTS.md` 並將其視為 primary instructions；因此本檔只做導向，不維護第二份完整規範。

## 讀取順序

1. 讀取根目錄 `AGENTS.md`。
2. 載入 `.agents/skills/inputbox-dev/SKILL.md`。
3. 依任務性質讀取 `docs/engineering/` 下的對應規範。

## Copilot 相容要求

- 若本檔與 `AGENTS.md`、`inputbox-dev` 或 `docs/engineering/` 有衝突，以共同規範與更細部工程規範為準。
- 不要在 `.github/copilot-instructions.md` 複製完整工程規則，避免和 `AGENTS.md` 發散。
- 若未來新增 `.github/instructions/**/*.instructions.md`，只放 path-specific 補充，且不得與 root `AGENTS.md` 衝突。
