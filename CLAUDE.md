# InputBox gh-pages — Claude Code 工作區指引

本檔供 **Claude Code** 自動載入。規則以 `ENGINEERING_GUIDELINES.md` 與 `docs/engineering/` 為權威來源；本檔將最常用規則內嵌，以減少工具讀取次數。

---

## 快速驗證

```bash
# 格式化檢查（提交前必執行）
npm run format:check

# A11y 自動化測試（提交前必通過）
npm test

# 套用格式化
npm run format
```

**完成前必做：** `npm run format:check` 零漂移、`npm test` 全通過、完成人工驗證四項（符號全形化、`lang` 屬性、Landmark 結構、七語內容一致性）。

---

## 安全性紅線（任何任務前必讀）

- **零 JS**：絕對禁止使用任何 JavaScript，含 `<script>`、事件屬性（`onclick` 等）。
- **零圖片資產**：禁止使用 PNG、JPG、SVG 等實體圖檔；以 Emoji 或純 CSS 繪製視覺元素。
- **零 Inline CSS**：禁止 `<div style="...">`；樣式一律置於 `<style>` 區塊。
- **GPG 簽章**：所有 Git 提交使用使用者既有 GPG 設定；禁止修改 `gpg.conf`、`gpg-agent.conf`；若簽章失敗回報使用者，**不得**用 `--no-gpg-sign` 繞過。

---

## 規範索引（依任務展開）

| 任務 | 必讀規範 |
|---|---|
| 任何異動 | `docs/engineering/web-architecture.md` |
| 多語系、Radio Hack、術語 | `docs/engineering/web-localization.md` |
| 色彩、對比、焦點、動畫、A11y | `docs/engineering/web-a11y-safety.md` |
| 排版、`<kbd>`、CJK 符號 | `docs/engineering/web-style-typography.md` |
| Git 工作流、格式化、提交檢查清單 | `docs/engineering/web-git.md` |

---

## CSS 關鍵模式（高頻違反項）

### `:has()` 防禦規則（強制）

凡使用 `body:has()` 的 CSS 規則，**一律**以雙層 `@supports` 包裹：

```css
/* ✓ 正確 */
@supports selector(body:has()) {
  body:has(#theme-dark:checked) {
    color-scheme: dark;
  }
}

@supports not selector(body:has()) {
  /* 退化：展開全部語系，隱藏切換器 */
  .lang-switcher { display: none; }
  .lang-zh, .lang-en, .lang-ja, .lang-sc { display: block; }
}
```

### Radio Hack 結構

- **語系切換**：`name="lang"` — 七個 radio（`#lang-zh`、`#lang-en`、`#lang-de`、`#lang-fr`、`#lang-ja`、`#lang-ko`、`#lang-sc`）
- **主題切換**：`name="theme"` — 三個 radio（`#theme-sys`、`#theme-light`、`#theme-dark`）
- 所有 Radio 必須位於 `<nav class="lang-switcher">` 內，置於 `<header>` 與 `<main>` 之前。

### 捲動驅動動畫（漸進增強）

```css
/* ✓ 預設可見、@supports 內隱藏並套用動畫 */
.back-to-top { visibility: visible; }

@supports (animation-timeline: scroll()) {
  .back-to-top {
    visibility: hidden;
    animation: show-on-scroll linear;
    animation-timeline: scroll();
  }
}
```

---

## A11y 核心數值

| 規範 | 要求 |
|---|---|
| 文字對比度（WCAG AAA） | ≥ 7:1 |
| 非文字 UI 對比度（WCAG 1.4.11） | ≥ 3:1 |
| 焦點指示器主環 | `outline: 5px solid #e67e00` |
| 焦點伴侶環（淺色模式） | `box-shadow: 0 0 0 10px #111827` |
| 焦點伴侶環（深色模式） | `--focus-companion: none`（單橘環 7.05:1） |
| 點擊目標最小尺寸 | ≥ 44×44px |
| 動畫頻率上限 | ≤ 1Hz |
| Hover/Focus 佈局影響 | 零抖動（禁止改變 width/height/margin/padding） |

- 高對比模式：必須提供 `@media (forced-colors: active)`，覆蓋 `.skip-link:focus`、`.cta-button`、導覽列 hover/active。
- 低效能觸控裝置：`(hover: none) and (pointer: coarse)` 條件下停用高成本動畫、濾鏡與陰影。

---

## 多語系規則

- 每段內容必須同時提供七語：**ZH、EN、DE、FR、JA、KO、SC**，禁止以任一語系代替其他語系。
- 語言容器標註正確 `lang` 屬性；與 `<html>` 主語系相同時可省略。
- **CJK 全形符號**：正體中文、日文、簡體中文區塊使用全形 `／`、`（`、`）`，且符號前後**嚴禁留空格**。
- **`<kbd>` 標籤**：所有按鍵名稱包裹於 `<kbd>`；組合鍵加號前後保留一個半形空格（`<kbd>Alt</kbd> + <kbd>F4</kbd>`）。
- **Footer 第三方清單**：必須與 `main` 分支 `README.md` 保持 100% 同步。

---

## 提交前檢查清單

1. `npm run format:check` — 格式零漂移
2. `npm test` — Playwright + axe-core 全通過
3. 人工確認：符號全形化、`lang` 屬性、Landmark 結構、七語內容一致性
4. 確認 Light / Dark / 跟隨系統三段主題切換正確
5. 確認 `forced-colors: active` 下關鍵元素可辨識
6. 確認互動元素 hover/focus 邊框對所有相鄰背景均達 ≥ 3:1
7. 確認編碼 UTF-8、換行 CRLF 未漂移

---

## Git 提交規範

```
格式：<type>(<scope>): <description>
語言：正體中文
Body：必須提供（說明目的與影響範圍）
分支：gh-pages（直接提交，不合併至 main）
驗證：git verify-commit HEAD
```

常用 type：`feat`、`fix`、`style`、`docs`、`test`、`chore`

---

## 完整規範來源

細節規則以 `ENGINEERING_GUIDELINES.md`、`.agents/skills/inputbox-web-dev/SKILL.md` 與 `docs/engineering/` 為準。
