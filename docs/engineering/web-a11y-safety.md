# 網頁 A11y 無障礙與視覺安全 (Web A11y & Safety)

- **色彩對比標準 (WCAG 2.2 AAA)**：
  - 文字對比度必須 ≥ 7:1。
  - 必須完整支援 `prefers-color-scheme: dark` 且對比度同樣達標。
  - 視覺警示配色採用 **`#e67e00`** (DarkOrange)。
- **眼動儀優化 (Eye Tracker Optimized)**：
  - **大點擊目標**：按鈕與連結最小尺寸為 44x44px。
  - **強化焦點框**：`:focus-visible` 必須具備高對比色且厚度 ≥ 5px。
  - **預設動作引導**：主要行動按鈕（如 CTA 下載）應具備與焦點框一致的視覺特徵。
  - **零佈局抖動 (Zero-Jitter)**：禁止在互動（Hover/Focus）時變動 Width, Height, Margin 或 Padding。
- **動畫安全與光敏防護**：
  - 動畫頻率鎖定 ≤ 1Hz（每秒一次）。
  - 必須支援 `@media (prefers-reduced-motion: reduce)`。
  - 使用平滑正弦波過渡，杜絕高頻閃爍或劇烈跳變。
