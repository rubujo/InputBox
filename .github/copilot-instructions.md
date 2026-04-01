# InputBox GitHub Pages 指令（GitHub Copilot）

本分支透過 Agent Skills 強制執行網頁工程規範。

## 0. 技能啟用
- **觸發條件**：當修改本分支內容時，請確認已載入 `inputbox-web-dev` 技能。
- **共用技能**：本技能由 Gemini 與 Copilot 共用，請以 `.agents/skills/inputbox-web-dev/SKILL.md` 為準。
- **知識基準**：實作前請先參考文件入口 `ENGINEERING_GUIDELINES.md`，再依需求延伸閱讀 `docs/engineering/`。

## 1. 核心網頁規則
- **零 JS／零圖片／零 Inline CSS**。
- **多語系同步**：凡有文字變更，必須同步更新 ZH、EN、JA、SC 四種語系。
- **符號規則**：CJK 區塊必須使用全形符號，且符號前後不得留空格。

## 2. A11y 標準
- WCAG 2.2 AAA（對比度 ≥ 7:1）。
- 點擊目標 ≥ 44x44px。
- Hover／Focus 禁止造成實體尺寸變化（Zero-Jitter）。
- 動畫頻率需為低頻（≤ 1Hz）。

## 3. 參考文件
- 文件入口：`ENGINEERING_GUIDELINES.md`
- 原子化網頁工程規範：`docs/engineering/`
- Gemini CLI 指令：`GEMINI.md`
