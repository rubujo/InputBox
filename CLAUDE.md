# InputBox gh-pages - Claude Code 工作區指引

本檔案供 Claude Code 使用，作為 `gh-pages` 分支的相容入口。共同規範以 [AGENTS.md](AGENTS.md)、`.agents/skills/inputbox-web-dev/SKILL.md` 與 `docs/engineering/` 為準；本檔僅保留 Claude Code 所需的最小導引。

## 1. 載入順序

開始任何任務前，請依序執行：

1. 讀取根目錄 `AGENTS.md`
2. 載入 `.agents/skills/inputbox-web-dev/SKILL.md`
3. 依任務性質讀取 `docs/engineering/` 下的對應網頁規範

若本檔與 `AGENTS.md` 有衝突，以 `AGENTS.md` 與更細部的工程規範為準。

## 2. Claude Code 專屬要求

- 使用 `CLAUDE.md` 作為 Claude Code 的專案記憶入口。
- 若 Claude Code 只自動讀取 `CLAUDE.md`，仍必須由本檔導向 `AGENTS.md`，不得在此維護第二套完整規範。
- 若任務涉及 HTML 結構、多語系、A11y、CSS 狀態切換、格式化或 Git 提交，必須主動展開對應的 `docs/engineering/web-*.md`。

## 3. 不可違反的共同紅線

- 禁止 JavaScript、圖片資產與 Inline CSS。
- 所有互動必須以純 CSS 實作，且 `body:has()` 規則必須附帶 `@supports selector()` 防禦與退化方案。
- 修改文字時必須同步更新七語內容，且不得以任一語系回退替代其他語系。
- Git 提交必須使用使用者既有的 GPG 簽章設定；不得修改任何 GPG 或 gpg-agent 設定檔。

## 4. 常用驗證

```powershell
npm run format:check
npm test
```
