# 網頁多語系機制 (Web Localization)

- **Radio Button Hack**：
  - 使用隱藏的 `input[type="radio"]` 控制顯隱。
  - **結構要求**：必須與主要內容同層，且必須被 Landmark 元素 (如 `<nav>`, `<header>`, `<main>`) 包覆。
  - 使用 CSS 兄弟選擇器 (`~`) 配合 `:checked` 切換 `.lang-xx` 標籤。
- **語意化與 Lang 屬性**：
  - 每一段文字必須同時提供 ZH, EN, JA, SC。
  - 語言容器必須標註 `lang`：`zh-Hant`, `en`, `ja`, `zh-Hans`。
  - **A+ 規範**：語系切換標籤的 `lang` 屬性不可與 `<html>` 主語系相同 (建議主語系標籤省略 `lang` 或設為 `und`)。
- **在地化術語**：
  - 臺灣：最佳化、快速鍵、應用程式、正體中文。
  - 大陸：优化、快捷键、应用程序、简体中文。
  - 日本：アプリケーション、携帯型 PC、タッチ キーボード。
