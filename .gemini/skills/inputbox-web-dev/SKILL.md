---
name: inputbox-web-dev
description: InputBox 專案網頁開發工程規範技能。當任務涉及 index.html 修改、CSS 樣式調整、網頁多語系同步或網頁 A11y 優化時，必須載入此技能。
---

# InputBox 網頁工程規範指引

本技能是 InputBox `gh-pages` 分支的專業網頁開發手冊。本專案嚴格執行「零 JS、零圖片」政策，並要求符合 WCAG 2.2 AAA 標準。

## 核心規範索引 (References)

請根據任務性質，使用 `read_file` 讀取對應規範：

1.  **網頁架構 (Zero-JS/Zero-Image)**：`docs/engineering/web-architecture.md`
2.  **多語系機制 (Radio Hack/Terminology)**：`docs/engineering/web-localization.md`
3.  **A11y 無障礙與視覺安全 (AAA)**：`docs/engineering/web-a11y-safety.md`
4.  **排版與標籤規範 (CJK/kbd)**：`docs/engineering/web-style-typography.md`
5.  **Git 提交與驗證規範**：`docs/engineering/web-git.md`

## 工作流程要求

- **互動開發**：嚴禁使用 JavaScript。所有顯隱切換必須透過 CSS 兄弟選擇器 (`~`) 與 `:checked` 達成。
- **視覺變更**：確保在互動時（Hover/Focus）達成「零佈局抖動」。
- **內容更新**：修改任何文字時，必須同步更新 ZH, EN, JA, SC 四種語系。
- **驗證階段**：提交前必須檢查所有符號全形化與 `lang` 屬性正確性。
