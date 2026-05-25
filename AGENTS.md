# InputBox gh-pages - Agent 工作區入口

本檔是 InputBox `gh-pages` 分支的唯一跨工具 agent 指引入口，供支援 `AGENTS.md` 的 Codex CLI、GitHub Copilot CLI 與 Antigravity CLI 使用。Claude Code 只透過最小 `CLAUDE.md` 與 Claude project skill 橋接進入同一條權威鏈，不維護第二份完整規範。

開始任何修改前，先讀取 `.agents/skills/inputbox-web-dev/SKILL.md`，再依網站任務類型讀取 `docs/engineering/` 下的對應規範。

## 0. Agent 支援結構

### 0.1 官方依據查核日期：2026-05-25

- Codex CLI：OpenAI 文件定義 `AGENTS.md` 為 custom instructions 檔，repo skills 位於 `.agents/skills`。
- Claude Code：Anthropic 文件定義 project memory 位於 `CLAUDE.md` 或 `.claude/CLAUDE.md`，`CLAUDE.md` 可匯入 `AGENTS.md`，project skills 位於 `.claude/skills`。
- GitHub Copilot CLI：GitHub 文件定義 root `AGENTS.md` 為 primary instructions，project skills 可位於 `.agents/skills`。
- Antigravity CLI：Google 文件定義 workspace skills 位於 `.agents/skills`；若未來需要 workspace rules，使用 `.agents/rules`。

### 0.2 權威鏈

1. `AGENTS.md`：跨工具共同入口，只放載入順序、安全紅線與 web engineering 文件索引。
2. `.agents/skills/inputbox-web-dev/SKILL.md`：`gh-pages` 權威網站工程 skill。
3. `docs/engineering/`：任務領域的原子化網站工程規範。
4. `CLAUDE.md` 與 `.claude/skills/inputbox-web-dev/SKILL.md`：Claude Code 必要橋接。

不要新增重複的 root instructions、舊版相容入口，或任何工具專屬的完整規範副本。細節規範只維護在 project skill 與 `docs/engineering/`。

### 0.3 支援矩陣

| 工具               | 入口                         | Skill 路徑                                      | 分支策略                                                         |
| ------------------ | ---------------------------- | ----------------------------------------------- | ---------------------------------------------------------------- |
| Codex CLI          | `AGENTS.md`                  | `.agents/skills/inputbox-web-dev/SKILL.md`      | 使用共同入口與權威 project skill。                               |
| Claude Code        | `CLAUDE.md` 匯入 `AGENTS.md` | `.claude/skills/inputbox-web-dev/SKILL.md` 橋接 | 僅作橋接；權威規範仍在 `.agents/skills` 與 `docs/engineering/`。 |
| GitHub Copilot CLI | `AGENTS.md`                  | `.agents/skills/inputbox-web-dev/SKILL.md`      | 使用 root primary instructions 與權威 project skill。            |
| Antigravity CLI    | `AGENTS.md`                  | `.agents/skills/inputbox-web-dev/SKILL.md`      | 使用共同入口與權威 project skill；目前不新增 workspace rules。   |

## 1. 必讀規範索引

至少依任務類型讀取下列文件：

| 任務類型                      | 必讀文件                                   |
| ----------------------------- | ------------------------------------------ |
| 任何網站異動                  | `docs/engineering/web-architecture.md`     |
| 多語系、語言 radio 狀態、術語 | `docs/engineering/web-localization.md`     |
| 色彩、對比、焦點、動畫、A11y  | `docs/engineering/web-a11y-safety.md`      |
| 排版、`<kbd>`、CJK 標點       | `docs/engineering/web-style-typography.md` |
| 格式化、提交與驗證            | `docs/engineering/web-git.md`              |

## 2. 安全與架構紅線

以下限制為硬性要求：

- 零 JavaScript：禁止使用 `<script>`、事件屬性或腳本注入。
- 零圖片資產：禁止使用 PNG、JPG、SVG 或其他實體圖檔；視覺元素只可使用 emoji 或純 CSS。
- 零 inline CSS：禁止 `style=""`；樣式必須位於合法樣式表結構。
- 所有互動必須以純 CSS 實作，例如 `:checked`、`:has()`、`:target`、`:focus-visible`、`@supports` 與媒體查詢。
- 使用 `body:has()` 的規則必須包在 `@supports selector(body:has())`，並以 `@supports not selector(body:has())` 提供退化方案。
- 依賴 `animation-timeline` 的功能 UI 必須採漸進增強：預設可見，只在 `@supports` 內隱藏並套用動畫。

## 3. 多語系與內容同步

- 每段內容必須同時提供七語：`zh-Hant`、`en`、`de`、`fr`、`ja`、`ko`、`zh-Hans`。
- 禁止以任一語系內容回退替代其他語系。
- 語言容器必須有正確 `lang` 屬性。
- 語系切換使用 `name="lang"` radio controls；保留既有結構與 selector 假設。
- 主題切換使用 `name="theme"` radio controls：`#theme-sys`、`#theme-light`、`#theme-dark`。
- 使用者可見文字異動必須同步更新所有語系版本。
- Footer 第三方函式庫清單必須與 `main` 分支 `README.md` 完全同步。

## 4. A11y 與視覺安全

- 文字對比度必須符合 WCAG 2.2 AAA：`>= 7:1`。
- 非文字 UI 對比度必須 `>= 3:1`。
- 點擊目標至少 `44x44px`。
- Hover、focus 與 active 狀態不得造成版面抖動。
- 支援 `prefers-color-scheme: dark`。
- 提供 `@media (forced-colors: active)` 以支援 Windows 高對比模式。
- 支援 `@media (prefers-reduced-motion: reduce)`。
- 動畫頻率上限為 `1Hz`。

## 5. 編碼、格式與驗證

- 本分支文字檔使用 UTF-8 無 BOM。
- Working tree 文字檔使用 CRLF。
- 遵循 `.editorconfig`、`.gitattributes` 與 `.prettierrc.json`。
- 交付前依異動類型執行：

```powershell
npm run format:check
npm test
```

若需要套用格式：

```powershell
npm run format
```

## 6. Git 工作流

- 提交格式遵循 Conventional Commits：`<type>(<scope>): <description>`。
- 提交訊息必須包含 subject 與 body。
- 提交訊息使用正體中文。
- 本分支直接在 `gh-pages` 工作。
- Git 提交必須使用使用者既有的 GPG 簽章設定。
- 不得修改 `gpg.conf`、`gpg-agent.conf` 或相關簽章設定。
- 提交後以 `git verify-commit HEAD` 或 `git log --show-signature -1` 驗證簽章。
