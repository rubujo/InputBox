# 網頁 A11y 無障礙與視覺安全 (Web A11y & Safety)

- **色彩對比 (WCAG 2.2 AAA)**：
  - 對比度必須 ≥ 7:1。
  - 必須支援 `prefers-color-scheme: dark`。
  - 視覺警示配色採用 **`#e67e00`** (DarkOrange)。
- **眼動儀優化 (Eye Tracker Optimized)**：
  - **大點擊目標**：最小尺寸 44x44px。
  - **強化焦點框**：`:focus-visible` 對比度極高且厚度 ≥ 5px。
  - **預設動作引導**：主要行動按鈕 (如下載) 應具備與焦點框一致的視覺特徵。
  - **零佈局抖動**：禁止在互動時改變物理尺寸 (Width/Height/Margin/Padding/Border-width)。
- **動畫安全**：
  - 頻率限制 ≤ 1Hz。
  - 必須支援 `@media (prefers-reduced-motion: reduce)`。
  - 使用平滑正弦波過渡，禁止突變或劇烈閃爍。
