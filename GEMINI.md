# InputBox GitHub Pages Instructions (Gemini CLI)

本檔案為 Gemini CLI 專用的「專案網頁分支」指令。請在維護靜態網頁時嚴格遵守以下核心規範與 A11y 要求。若需完整的網頁架構標準，請參考 [ENGINEERING_GUIDELINES.md](ENGINEERING_GUIDELINES.md)。

## 0. 分支定位與規範
- **定位**：本分支僅負責 `gh-pages` 靜態網頁資源，包含 `index.html` 與相關文件。
- **絕對禁令**：
    - **禁止使用 JavaScript**：所有互動與狀態切換必須透過純 CSS 達成。
    - **禁止使用 Inline CSS**：所有樣式必須位於 `<style>` 區塊或外部樣式表。**嚴禁在 HTML 標籤內使用 `style="..."` 屬性**。
    - **禁止使用圖片**：全站需維持零圖片資產，僅允許使用 Emoji 或 CSS 繪製。

## 1. 內容維護規範
- **多語系同步**：修改任何文案時，必須同時更新 **正體中文 (ZH)**、**英文 (EN)**、**日文 (JA)**、**簡體中文 (SC)** 四種語系。
- **邏輯對齊**：所有功能描述、快速操作指引（Quick Guide）必須與 `main` 分支的原始碼實作邏輯及 `README.md` 保持高度一致。若有衝突，以原始碼邏輯為準。
- **排版美學**：
    - **CJK 符號**：一律使用全形 `／`、`（`、`）`，且符號前後 **禁止留空格**。
    - **按鍵標籤**：CJK 使用 `[按鍵]` 格式，EN 使用 `Key` 格式。所有按鍵均須使用 `<kbd>` 標籤。
- **第三方披露**：若 `main` 分支變更了第三方函式庫，必須同步更新頁尾的授權聲明區塊。

## 2. A11y 與視覺安全 (WCAG 2.2 AAA)
- **色彩對比**：正常文字對比度必須維持在 **7:1** 以上。
- **點擊目標**：所有連結與語言切換標籤的最小點擊尺寸必須 ≥ **44x44px**。
- **眼動儀優化**：
    - 焦點框（`:focus-visible`）需具備極高對比（如深橙色）且厚度 ≥ 5px。
    - 禁止使用會改變物理尺寸（Size, Margin, Padding）的動畫，防止「佈局抖動」。
- **光敏安全**：所有 CSS 動畫必須維持低頻（1Hz）、平滑過渡（正弦波），且尊重 `prefers-reduced-motion` 設定。

## 3. 術語一致性
- **同步原則**：網頁文案必須與 `main` 分支之 [ENGINEERING_GUIDELINES.md](https://github.com/rubujo/InputBox/blob/main/ENGINEERING_GUIDELINES.md) 中的術語表保持一致。
- **在地化**：嚴格區分「正體中文」標籤與「繁體中文版」描述，並遵循微軟在地化原則。

## 4. 參考文件
- 網頁工程規範：[ENGINEERING_GUIDELINES.md](ENGINEERING_GUIDELINES.md)
- GitHub Copilot 專用指令：[.github/copilot-instructions.md](.github/copilot-instructions.md)
- 分支說明：[README.md](README.md)
