# InputBox - Antigravity / Gemini 相容入口

本檔是 Antigravity CLI 與 Gemini 相容工具的 context 入口。Antigravity CLI 官方遷移文件指出 workspace context 會讀取 `GEMINI.md` 與 `AGENTS.md`，因此本檔只做薄相容層，避免和共同規範重複。

## 載入順序

1. 讀取根目錄 `AGENTS.md`。
2. 載入 `.agents/skills/inputbox-dev/SKILL.md`。
3. 依任務性質讀取 `docs/engineering/` 下的對應規範。

## 相容要求

- `AGENTS.md` 是跨工具主要入口；本檔不得複製完整規範。
- Workspace skills 保持在 `.agents/skills`。
- 若日後需要 Antigravity workspace rules，放在 `.agents/rules`，並只引用共同規範。
- 若本檔與 `AGENTS.md`、`inputbox-dev` 或 `docs/engineering/` 有衝突，以共同規範與更細部工程規範為準。
