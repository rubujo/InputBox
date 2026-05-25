# InputBox 網頁工程規範目錄 (Web Engineering Standards)

本文件是 `gh-pages` 分支的工程規範索引。對 Codex CLI、Claude Code、GitHub Copilot CLI 與 Antigravity CLI 而言，repo 根目錄 `AGENTS.md` 為共同入口，實際細節則以下列原子化模組為準。

## 核心規範檔案

- [網頁架構 (Web Architecture)](docs/engineering/web-architecture.md)
- [多語系機制 (Web Localization)](docs/engineering/web-localization.md)
- [A11y 無障礙與視覺安全 (Web A11y)](docs/engineering/web-a11y-safety.md)
- [排版與標籤規範 (Web Typography)](docs/engineering/web-style-typography.md)
- [Git 提交與驗證 (Web Git)](docs/engineering/web-git.md)（包含 Conventional Commits、GPG 簽章提交與驗證要求）

## 格式化流程

- HTML 開發以 `.editorconfig` + Prettier 為標準格式來源。
- 修改網頁、文件或設定檔後，必須立即格式化檔案，再進行測試與提交。

## Agent 規範位置

- **共同入口**：`AGENTS.md`
- **權威技能**：`.agents/skills/inputbox-web-dev/SKILL.md`
- **Claude Code 橋接**：`CLAUDE.md` 與 `.claude/skills/inputbox-web-dev/SKILL.md`
- 若需擴充技能內容，請以 `.agents/skills/inputbox-web-dev/SKILL.md` 為單一來源，避免多份規範漂移。

---

_註：本分支嚴格執行「零 JS／零圖片／零 Inline CSS」政策。_
