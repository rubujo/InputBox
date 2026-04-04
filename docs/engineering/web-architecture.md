# 網頁架構原則 (Web Architecture)

- **零指令碼政策 (Zero-JS Policy)**：
  - 絕對禁止使用任何 JavaScript。
  - 所有互動（如語系切換、按鈕回饋、區塊顯示、導覽狀態切換）必須透過純 CSS（例如 `:checked`, `:has()`, `:target`, `:hover`, `:focus-visible`, `@supports`, `animation-timeline`, `view-timeline`）達成。
  - 使用較新的 CSS 能力時，必須提供可接受的退化方案，確保不支援該能力的瀏覽器仍可完成核心導覽與閱讀。
  - **`@supports selector()` 防禦規則**：凡使用 `:has()` 的 CSS 規則，一律以 `@supports selector(body:has())` 包裹，並搭配 `@supports not selector()` 撰寫退化方案（例如展開四語內容、隱藏切換器）。巢狀 `@supports` 內再包一層時（如 `animation-timeline`），同樣需雙層保護。
  - 針對小螢幕或低效能觸控裝置（如低階 Android、`(hover: none) and (pointer: coarse)` 情境），可使用純 CSS `@media` 條件提供效能降級檔位，以降低動畫、濾鏡與 GPU 合成成本。
- **零圖片資產 (Zero-Image Assets)**：
  - 不使用任何實體圖檔（PNG, JPG, SVG 等）。
  - 使用 Emoji 進行視覺引導。
  - 利用 CSS 繪製所有 UI 元素（如 `<kbd>` 樣式、裝飾線條）。
- **內嵌 CSS 規範**：
  - 禁止使用 Inline CSS（`<div style="...">`）。
  - 所有樣式必須位於 `<style>` 區塊或外部樣式表。
