# 網頁架構原則 (Web Architecture)

- **零指令碼政策 (Zero-JS Policy)**：
  - 禁止使用任何 JavaScript。
  - 所有互動 (語系切換、按鈕回饋、區塊顯示) 必須透過純 CSS (`:checked`, `:target`, `:hover`, `:focus-visible`) 達成。
- **零圖片資產 (Zero-Image Assets)**：
  - 不使用實體圖檔 (PNG, JPG, SVG)。
  - 使用 Emoji 進行視覺引導。
  - 利用 CSS 繪製 UI 元素 (如 `<kbd>` 樣式)。
