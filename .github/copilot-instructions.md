# InputBox GitHub Pages Instructions

本檔案定義 GitHub Copilot 在維護此分支（靜態網頁資源）時應遵循的精簡規則。詳細架構請參考 [ENGINEERING_GUIDELINES.md](../ENGINEERING_GUIDELINES.md)。

## Web Context
- 分支內容為 GitHub Pages 靜態資源，主要檔案為 `index.html`。
- **核心架構**：純 HTML/CSS，**嚴禁使用 JavaScript、Inline CSS 或外部圖片**。
- **多語系技術**：採用隱藏 Radio Buttons (`#lang-xx`) 配合 CSS 兄弟選擇器切換顯隱。

## Maintenance Rules
- **多語系同步**：修改文字時，**必須同時更新四種語系**（ZH, EN, JA, SC）。
- **邏輯對齊**：操作指引必須與 `main` 分支的原始碼實作保持 100% 同步。
- **符號規範**：CJK 語系统一使用全形 `／`、`（`、`）`，且前後 **不留空格**。
- **按鍵標籤**：CJK 使用 `[按鍵]`，EN 使用 `Key`。所有按鍵需包覆在 `<kbd>` 標籤內。
- **第三方披露**：頁尾需同步維護與 `main` 分支一致的第三方函式庫聲明。

## A11y & CSS Rules
- **色彩對比**：必須符合 WCAG 2.2 AAA (對比度 ≥ 7:1)。
- **點擊目標**：按鈕與連結必須保持 ≥ 44x44px。
- **眼動儀定位**：焦點框 (`:focus-visible`) 需具備高對比色與足夠厚度（建議 5px）。
- **零佈局抖動**：禁止在互動（Hover/Focus）時修改物理尺寸（width, height, margin, padding）。
- **動畫限制**：僅限低頻平滑脈衝 (≤ 1Hz)，且必須受 `prefers-reduced-motion` 控制。

## Terminology
- 臺灣：最佳化、快速鍵、應用程式、正體中文、觸控式鍵盤、遊戲控制器、視圖鍵（View）、功能表鍵（Menu）。
- 大陸：优化、快捷键、应用程序、简体中文、触摸键盘、游戏控制器。
- 日本：アプリケーション、携帯型 PC、タッチ キーボード、ゲーム コントローラー。

## Validation
- 每次異動後應確認在 Light / Dark 模式下的視覺表現。
- 驗證所有多語系標籤的 `lang` 屬性正確。
- 確保符號全形化與間距規範無誤。
