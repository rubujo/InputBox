# InputBox GitHub Pages 指令（Gemini CLI）

本檔案為 Gemini CLI 專用的「專案網頁分支」指令。執行任務前，**必須**優先啟動專屬技能。

## 0. 技能與規範載入 (Skills & Guidelines)
- **核心技能**：執行任務前，請務必執行 `activate_skill("inputbox-web-dev")`。
- **共用技能路徑**：本技能由 Gemini 與 Copilot 共用，請以 `.agents/skills/inputbox-web-dev/SKILL.md` 為準。
- **主動研究**：在開始修改前，請依據技能指引，讀取 `docs/engineering/` 下相關的網頁規範檔案。

## 1. 核心紅線
- **零 JS／零圖片／零 Inline CSS**：嚴禁違背此政策。
- **多語系同步**：修改文字時必須同時更新 ZH、EN、JA、SC 四種語系。
- **編碼與換行**：文字檔一律使用 UTF-8 編碼與 CRLF 換行，並遵循 `.editorconfig`、`.gitattributes`。

## 2. A11y 要求
- 色彩對比必須 ≥ 7:1。
- 點擊目標 ≥ 44x44px。
- 嚴格遵守「零佈局抖動」原則。

## 3. 參考索引
- 文件入口：`ENGINEERING_GUIDELINES.md`
- 原子化網頁工程規範：`docs/engineering/`
- GitHub Copilot 專用指令：`.github/copilot-instructions.md`
- 分支說明：`README.md`
