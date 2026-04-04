---
name: inputbox-web-dev
description: InputBox 專案網頁開發工程規範技能。當任務涉及修改 index.html、調整 CSS 樣式、同步網頁多語系或優化網頁 A11y 時，載入此技能。
---

# InputBox 網頁工程規範指引 (Web Guidelines)

本技能提供 InputBox `gh-pages` 分支的權威網頁開發標準。本專案嚴格執行「零 JS／零圖片／零 Inline CSS」政策，並致力於達成 WCAG 2.2 AAA 無障礙標準。

## 核心規範索引 (References)

請根據任務性質載入相關檔案：

1.  **網頁架構 (零 JS／零圖片)**：`docs/engineering/web-architecture.md`
2.  **多語系機制 (Radio Hack/術語表)**：`docs/engineering/web-localization.md`
3.  **A11y 無障礙與視覺安全 (AAA)**：`docs/engineering/web-a11y-safety.md`
4.  **排版與標籤規範 (CJK/kbd)**：`docs/engineering/web-style-typography.md`
5.  **Git 提交與驗證規範**：`docs/engineering/web-git.md`

## 工作流程指令 (Workflow Mandates)

- **互動開發**：絕對禁止使用 JavaScript。所有顯隱切換透過 `body:has(:checked)` 實現；凡使用 `:has()` 的規則，必須以 `@supports selector(body:has())` 包裹，並提供 `@supports not selector()` 退化方案。
- **主題切換**：使用 `name="theme"` Radio Hack（`#theme-sys`／`#theme-light`／`#theme-dark`），以 `body:has()` 覆蓋 `:root` CSS 自訂屬性實現三段模式切換；語系切換使用 `name="lang"`。
- **視覺一致性**：確保所有互動（Hover/Focus）達成「零佈局抖動」。
- **內容同步**：修改任何文字時，必須同步更新 ZH、EN、JA、SC 四種語系。
- **A11y 目標**：互動元件點擊目標應維持 ≥ 44x44px。
- **最終驗證**：提交前執行 `npm test`（Playwright 44 項全過），並檢查符號全形化、`lang` 屬性以及 Landmark 導覽結構的正確性。
