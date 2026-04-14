# InputBox GitHub Pages 指令（GitHub Copilot）

本分支透過 Agent Skills 強制執行網頁工程規範。

## 0. 技能啟用

- **觸發條件**：當修改本分支內容時，請確認已載入 `inputbox-web-dev` 技能。
- **共用技能**：本技能由 Gemini 與 Copilot 共用，請以 `.agents/skills/inputbox-web-dev/SKILL.md` 為準。
- **知識基準**：實作前請先參考文件入口 `ENGINEERING_GUIDELINES.md`，再依需求延伸閱讀 `docs/engineering/`。

## 1. 核心網頁規則

- **零 JS／零圖片／零 Inline CSS**。
- **`:has()` 防禦規範**：所有使用 `body:has()` 的 CSS 規則，必須以 `@supports selector(body:has())` 包裹；須同時提供 `@supports not selector()` 退化方案。
- **多語系同步**：凡有文字變更，必須同步更新 ZH、EN、JA、SC 四種語系。
- **符號規則**：CJK 區塊必須使用全形符號，且符號前後不得留空格。
- **編碼與換行**：文字檔一律使用 UTF-8 編碼與 CRLF 換行，並遵循 `.editorconfig`、`.gitattributes`。
- **格式化要求**：每次修改網頁、設定或文件檔案後，必須立即套用專案格式化器，保持縮排、換行與屬性排版一致。

## 2. A11y 標準

- WCAG 2.2 AAA（對比度 ≥ 7:1）。
- 焦點指示器採雙環設計：`outline: 5px solid #e67e00` + `box-shadow: var(--focus-companion)`；淺色模式搭配深色伴侶環，深色模式單橘環即達 7.05:1。
- 點擊目標 ≥ 44x44px。
- Hover／Focus 禁止造成實體尺寸變化（Zero-Jitter）。
- 動畫頻率需為低頻（≤ 1Hz）。
- 已導入 Playwright + axe-core 自動化 A11y 測試；提交前必須執行 `npm test` 並通過。
- 自動化測試之外仍需完成人工驗證（符號全形化、`lang` 屬性、Landmark 導覽結構、四語內容一致性）。

## 3. 參考文件

- 文件入口：`ENGINEERING_GUIDELINES.md`
- 原子化網頁工程規範：`docs/engineering/`
- Gemini CLI 指令：`GEMINI.md`
