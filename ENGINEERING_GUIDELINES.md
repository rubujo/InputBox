# InputBox 網頁工程規範 (GitHub Pages Branch)

本文件定義了「輸入框」專案網頁的開發標準，旨在確保網頁在不使用指令碼的情況下，仍具備高度的互動性、多語系支援與頂尖的無障礙（A11y）相容性。

---

## 1. 架構原則 (Core Architecture)

### 1.1 零指令碼與安全 (Zero-JS Policy)
- **規範**：本網站禁止使用任何 JavaScript。
- **目的**：極大化安全性，並確保在各種受限環境（如極簡瀏覽器、高安全性設定）下均能正常運作。
- **實現**：所有互動效果（語系切換、按鈕回饋、區塊顯示）必須透過純 CSS (`:checked`, `:target`, `:hover`, `:focus-visible`) 達成。

### 1.2 零圖片資產 (Zero-Image Assets)
- **規範**：本網站不使用實體圖圖檔（PNG, JPG, SVG 等）。
- **實現**：使用 Emoji 進行視覺引導，並利用 CSS 進行排版美化（如按鍵標籤 `<kbd>` 樣式）。

---

## 2. 多語系機制 (Multi-language Implementation)

### 2.1 Radio Button Hack
為了在不使用 JS 且不影響頁內錨點導覽（如點擊跳至「快速操作」區塊）的情況下切換語系，本站採用 Radio Button 技術：
- **結構**：在 `<body>` 開頭定義隱藏的 `input[type="radio"]`。
- **控制**：利用 CSS 相鄰兄弟選擇器 (`~`) 配合 `:checked` 虛擬類別，控制 `.lang-xx` 標籤的 `display` 狀態。
- **持久性**：此機制能確保使用者在瀏覽頁面各處時，語系狀態不會因為 URL 變動而重置。

### 2.2 語意化同步
- **規範**：每一段文字必須同時提供 ZH, EN, JA, SC 四種版本。
- **A11y 重要規則**：每個語言容器必須明確標註 `lang` 屬性：
    - 正體中文：`<span class="lang-zh" lang="zh-Hant">`
    - 英文：`<span class="lang-en" lang="en">`
    - 日文：`<span class="lang-ja" lang="ja">`
    - 簡體中文：`<span class="lang-sc" lang="zh-Hans">`

---

## 3. 無障礙與視覺安全 (A11y & Visual Safety)

### 3.1 色彩與對比合規性 (WCAG 2.2 AAA)
- **對比度**：所有文字與背景的對比度必須 ≥ 7:1。
- **深色模式**：必須支援 `prefers-color-scheme: dark`，且深色模式下的對比度亦須達標。
- **視覺警示配色 (Visual Alert Pairing)**：過渡配色採用 **`#e67e00`** (DarkOrange)，具備極高辨識度。
- **不依賴顏色**：所有狀態變更必須具備「非顏色提示」，如 Emoji 變化、字體加粗或邊框變化。

### 3.2 眼動儀友善 (Eye Tracker Optimized)
- **大點擊目標**：所有互動元素最小尺寸為 44x44px。
- **強化焦點框**：`:focus-visible` 必須具備高亮度對比與足夠厚度（≥ 5px），協助眼動儀使用者確認目前的目標。
- **預設動作引導 (Default Action Guidance)**：頁面中的主要行動按鈕（如 CTA 下載按鈕）應與「焦點框」具備一致的視覺特徵（如採用 `var(--focus-ring)`），以引導使用者的視覺預期。
- **防抖動原則**：禁止在 `:hover` 或 `:focus` 時改變物理尺寸（如變動 Margin、Padding 或 Border-width），避免觸發重排（Reflow）導致眼動儀視線遺失。

### 3.3 動畫安全
- **頻率限制**：所有動畫律動必須 ≤ 1Hz（每秒一次）。
- **平滑度**：使用 `ease-in-out` 或正弦波過渡，杜絕高頻閃爍。
- **運動減緩**：必須實作 `@media (prefers-reduced-motion: reduce)` 以停用所有非必要動畫。

---

## 4. 在地化術語與排版 (Localization & Typography)

### 4.1 術語表 (Terminology)
本網頁文案需嚴格對齊 `main` 分支定義的術語表：
- **正體中文 (臺灣)**：優先使用「最佳化」、「快速鍵」、「應用程式」、「正體中文」、「視圖鍵（View）」、「功能表鍵（Menu）」。
- **簡體中文**：使用「优化」、「快捷键」、「应用程序」、「简体中文」。
- **日文**：使用「アプリケーション」、「携帯型 PC」、「タッチ キーボード」（中間需空格）。按鍵名稱遵循微軟日文標準。

### 4.2 CJK 排版規範 (CJK Typography)
- **符號全形化**：所有 CJK 區塊（ZH, JA, SC）的斜線、括號必須使用全形符號（`／`、`（`、`）`）。
- **間距規則**：全形符號與文字、數字、按鍵標籤之間 **不應有空格**。
- **統一視覺**：若為了全站視覺風格統一，英文區塊亦可視需求跟進使用全形括號（如 `XX（EN Content）`）。

### 4.3 按鍵標籤 (Keyboard Formatting)
- **標籤使用**：所有按鍵名稱、符號、數字均必須包裹於 `<kbd>` 標籤中。
- **括號格式**：
    - **CJK 語系**：一律採用 `[按鍵]` 格式（如 `<kbd>[Alt]</kbd>`）。
    - **英文語系**：採用標準 `Key` 格式（如 `<kbd>Alt</kbd>`），不加括號。
- **組合鍵**：加號 `+` 前後必須保留一個半形空格（如 `[Alt] + [F4]`）。

---

## 5. 第三方授權聲明 (Third-Party Notices)

- **同步義務**：頁尾（Footer）必須披露專案所使用的第三方函式庫，且清單必須與 `main` 分支之 `README.md` 保持 100% 同步。
- **必要資訊**：必須包含元件名稱、指向原始碼的連結、以及授權類型標記（如 `（MIT）`、`（3-clause BSD）`）。

---

## 6. Git 提交規範 (Git Commit Guidelines)

為了確保版本紀錄的可讀性與自動化相容性，所有提交必須遵循以下標準：

- **格式標準**：必須採用 [慣例式提交（Conventional Commits）v1.0.0](https://www.conventionalcommits.org/zh-hant/v1.0.0/) 規範。
  - 格式範例：`<type>(<scope>): <description>`。
  - 常見類型：`feat`（新功能）、`fix`（修補）、`docs`（文件）、`style`（格式）、`refactor`（重構）、`perf`（效能）、`test`（測試）、`chore`（例行事務）。
- **訊息完整性**：嚴禁僅使用 `git commit -m` 方式提交簡短主旨。必須提供完整的提交訊息，除 **Subject**（主旨）外，還必須包含 **Body**（說明內容），詳盡描述異動的原因、背景與具體實作細節。
- **語系要求**：所有 Git 提交訊息（Commit Message）**必須使用正體中文**，除非使用者明確指定使用其他語言。

---

## 7. 提交與驗證 (Validation)

- **品質檢查**：修改 HTML 後，必須驗證在深色與淺色模式下的視覺正確性。
- **A11y 驗證**：確保每一處變動都具備正確的語系標籤與 A11y 屬性。
- **術語校對**：檢查是否符合微軟在地化原則。
