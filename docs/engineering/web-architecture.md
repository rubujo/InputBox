# 網頁架構原則 (Web Architecture)

- **零指令碼政策 (Zero-JS Policy)**：
  - 絕對禁止使用任何 JavaScript。
  - 所有互動（如語系切換、按鈕回饋、區塊顯示）必須透過純 CSS (`:checked`, `:target`, `:hover`, `:focus-visible`) 達成。
- **零圖片資產 (Zero-Image Assets)**：
  - 不使用任何實體圖檔（PNG, JPG, SVG 等）。
  - 使用 Emoji 進行視覺引導。
  - 利用 CSS 繪製所有 UI 元素（如 `<kbd>` 樣式、裝飾線條）。
- **內嵌 CSS 規範**：
  - 禁止使用 Inline CSS（`<div style="...">`）。
  - 所有樣式必須位於 `<style>` 區塊或外部樣式表。
