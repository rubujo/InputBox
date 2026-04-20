# InputBox gh-pages - OpenAI Codex 工作區指引

本檔案供 OpenAI Codex 與其他支援 `AGENTS.md` 的工具於 `gh-pages` 分支中使用。開始任何修改前，應先讀取 `.agents/skills/inputbox-web-dev/SKILL.md`，再依任務需要載入 `docs/engineering/` 下的對應網頁規範。

## 0. 權威來源

本分支的共同規範以下列來源為準：

- `ENGINEERING_GUIDELINES.md`
- `.agents/skills/inputbox-web-dev/SKILL.md`
- `docs/engineering/web-architecture.md`
- `docs/engineering/web-localization.md`
- `docs/engineering/web-a11y-safety.md`
- `docs/engineering/web-style-typography.md`
- `docs/engineering/web-git.md`

若本檔與上述文件有衝突，以上述文件為準。

## 1. 必讀規範索引

至少依任務類型讀取下列文件：

| 任務類型                     | 必讀文件                                   |
| ---------------------------- | ------------------------------------------ |
| 任何異動                     | `docs/engineering/web-architecture.md`     |
| 多語系、Radio Hack、術語     | `docs/engineering/web-localization.md`     |
| 色彩、對比、焦點、動畫、A11y | `docs/engineering/web-a11y-safety.md`      |
| 排版、`<kbd>`、CJK 符號      | `docs/engineering/web-style-typography.md` |
| 格式化、提交與驗證           | `docs/engineering/web-git.md`              |

## 2. 不可違反的安全與架構紅線

以下限制為硬性要求：

- **零 JavaScript**：禁止使用任何 JavaScript，包含 `<script>`、事件屬性與任何腳本注入。
- **零圖片資產**：禁止使用 PNG、JPG、SVG 等實體圖檔；視覺元素僅可使用 Emoji 或純 CSS。
- **零 Inline CSS**：禁止 `style=""`；所有樣式必須位於 `<style>` 區塊或合法樣式表結構中。
- 所有互動必須透過純 CSS 實作，例如 `:checked`、`:has()`、`:target`、`:focus-visible`、`@supports` 與媒體查詢。
- 凡使用 `body:has()` 的規則，必須以 `@supports selector(body:has())` 包裹，並提供 `@supports not selector()` 退化方案。
- 若功能性 UI 依賴 `animation-timeline`，必須採用「預設可見、`@supports` 內隱藏並套用動畫」的漸進增強模式。

## 3. 多語系與內容同步

- 每段內容必須同時提供七語：`zh-Hant`、`en`、`de`、`fr`、`ja`、`ko`、`zh-Hans`。
- 禁止以任一語系內容回退替代其他語系。
- 語言容器必須標註正確 `lang` 屬性。
- 語系切換使用 `name="lang"` 的 Radio Hack，必須保留既有結構與 selector 假設。
- 主題切換使用 `name="theme"` 的 Radio Hack（`#theme-sys`、`#theme-light`、`#theme-dark`）。
- 修改任何使用者可見文字時，必須同步更新所有語系版本。
- Footer 的第三方函式庫清單必須與 `main` 分支 `README.md` 保持 100% 同步。

## 4. 排版與標記規則

- 正體中文、日文、簡體中文內容必須使用全形 `／`、`（`、`）`。
- 全形符號與前後文字、數字、按鍵標籤之間禁止留空格。
- 所有按鍵名稱必須包在 `<kbd>` 中。
- 組合鍵中的 `+` 前後保留一個半形空格，例如 `<kbd>Alt</kbd> + <kbd>F4</kbd>`。
- 資料表格第一欄若為列標題，必須使用 `<th scope="row">`。
- 行動版純 CSS 重複顯示的欄位標籤必須加上 `aria-hidden="true"`。

## 5. A11y 與視覺安全

- 文字對比度必須達到 WCAG 2.2 AAA：`>= 7:1`
- 非文字 UI 對比度必須 `>= 3:1`
- 焦點主環使用 `outline: 5px solid #e67e00`
- 淺色模式焦點伴侶環使用 `box-shadow: 0 0 0 10px #111827`
- 深色模式使用單橘環，避免不必要伴侶環
- 點擊目標最小尺寸為 `44x44px`
- Hover / Focus / Active 不得造成版面抖動，不可變動 `width`、`height`、`margin`、`padding`
- 必須支援 `prefers-color-scheme: dark`
- 必須提供 `@media (forced-colors: active)` 以支援 Windows 高對比模式
- 必須支援 `@media (prefers-reduced-motion: reduce)`
- 所有動畫頻率上限為 `1Hz`

## 6. 編碼、格式與驗證

- 本分支相關文字檔一律使用 **UTF-8 無 BOM**
- 工作目錄中的文字檔換行一律使用 **CRLF**
- 所有異動必須遵循 repo 根目錄 `.editorconfig`、`.gitattributes` 與 `.prettierrc.json`
- 修改 HTML、CSS、JSON、Markdown 或 Playwright 檔案後，應執行格式化器

常用驗證指令：

```powershell
npm run format:check
npm test
```

若需要套用格式：

```powershell
npm run format
```

## 7. 提交前檢查清單

交付前至少確認：

1. `npm run format:check` 無漂移
2. `npm test` 全部通過
3. 人工驗證符號全形化、`lang` 屬性、Landmark 結構與七語內容一致性
4. 驗證 Light / Dark / 跟隨系統三段主題切換
5. 驗證 `forced-colors: active` 下關鍵元素可辨識
6. 確認 hover / focus 邊框色對所有相鄰背景皆達 `>= 3:1`
7. 確認編碼與換行未漂移

## 8. Git 工作流

- 提交格式遵循 Conventional Commits：`<type>(<scope>): <description>`
- 提交訊息必須包含 Subject 與 Body
- 提交訊息使用正體中文
- 本分支以 `gh-pages` 為工作分支
- Git 提交必須使用使用者既有的 GPG 簽章設定
- 禁止修改 `gpg.conf`、`gpg-agent.conf` 或其他 GPG 相關設定檔
- 提交後應以 `git verify-commit HEAD` 或 `git log --show-signature -1` 驗證簽章
