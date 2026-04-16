# InputBox GitHub Pages 指令（Gemini CLI）

本檔案為 Gemini CLI 專用的「專案網頁分支」指令。執行任務前，**必須**優先啟動專屬技能。

## 0. 技能與規範載入 (Skills & Guidelines)

- **核心技能**：執行任務前，請務必執行 `activate_skill("inputbox-web-dev")`。
- **共用技能路徑**：本技能由 Gemini 與 Copilot 共用，請以 `.agents/skills/inputbox-web-dev/SKILL.md` 為準。
- **主動研究**：在開始修改前，請依據技能指引，讀取 `docs/engineering/` 下相關的網頁規範檔案。

## 1. 核心紅線

- **零 JS／零圖片／零 Inline CSS**：嚴禁違背此政策。
- **Git 提交必須使用 GPG 簽章**；若簽章失敗，應先修復本機簽章環境，不得以停用簽章方式繞過。
- **`@supports selector()` 強制要求**：所有使用 `body:has()` 的 CSS 規則，必須以 `@supports selector(body:has())` 包裹，並提供 `@supports not selector()` 退化方案（展開四語內容、隱藏切換器）。
- **多語系同步**：修改文字時必須同時更新 ZH、EN、JA、SC 四種語系。
- **編碼與換行**：文字檔一律使用 UTF-8 編碼與 CRLF 換行，並遵循 `.editorconfig`、`.gitattributes`。

## 2. A11y 要求

- 色彩對比必須 ≥ 7:1（文字），非文字 UI ≥ 3:1。
- 焦點指示器：雙環 `outline: 5px solid #e67e00` + `box-shadow: var(--focus-companion)` 伴侶環（淺色模式）；深色模式單橘環 7.05:1。
- 點擊目標 ≥ 44x44px。
- 嚴格遵守「零佈局抖動」原則。
- 目前 `gh-pages` 尚未建置 Playwright 自動化測試；提交前請完成人工驗證（符號全形化、`lang` 屬性、Landmark 導覽結構、四語內容一致性）。若未來導入測試，再以 `npm test` 作為強制檢查。

## 3. 參考索引

- 文件入口：`ENGINEERING_GUIDELINES.md`
- 原子化網頁工程規範：`docs/engineering/`
- GitHub Copilot 專用指令：`.github/copilot-instructions.md`
- 分支說明：`README.md`
