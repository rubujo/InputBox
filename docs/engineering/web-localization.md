# 網頁多語系機制 (Web Localization)

- **Radio Button Hack**：
  - 使用隱藏的 `input[type="radio"]` 作為切換器，目前應用於兩個功能：
    1. **語系切換**：`name="lang"`，四個 radio（`#lang-zh`、`#lang-en`、`#lang-ja`、`#lang-sc`）控制語言顯隱。
    2. **色彩主題切換**：`name="theme"`，三個 radio（`#theme-sys`、`#theme-light`、`#theme-dark`）以 `body:has()` 覆蓋 `:root` CSS 自訂屬性，實現跟隨系統／強制淺色／強制深色三段模式。
  - **結構要求**：所有 Radio 必須位於 `<nav class="lang-switcher">` 內，置於 `<header>` 與 `<main>` 之前，確保 `body:has()` 選擇器能正確向上根選取。
  - 可使用純 CSS 狀態選擇器（如 `:checked`、`:has()`）切換不同語系標籤的顯隱與焦點回饋；所有 `:has()` 規則須以 `@supports selector()` 包裹。
  - **退化方案**：`@supports not selector(body:has())` 區塊內隱藏整個切換器，並將四語 `.lang-zh`、`.lang-en`、`.lang-ja`、`.lang-sc` 全部展開為 `display: block`，確保舊瀏覽器仍可閱讀完整內容。
- **語意化與 Lang 屬性**：
  - 每一段文字內容必須同時提供 ZH (正體中文)、EN (英文)、JA (日文)、SC (簡體中文)。
  - 語言容器必須標註正確的 `lang` 屬性。
  - **A+ 規範**：語系切換標籤若與 `<html>` 主語系相同，可省略 `lang` 屬性以避免冗餘；非主語系標籤必須標註正確 `lang`。
- **在地化術語對齊**：
  - 臺灣：最佳化、快速鍵、應用程式、正體中文。
  - 大陸：优化、快捷键、应用程序、简体中文。
  - 日本：アプリケーション、携帯型 PC、タッチ キーボード。
