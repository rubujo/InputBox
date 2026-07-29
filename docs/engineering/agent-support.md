# Agent 支援與規範維護

本文件保存 InputBox 的跨工具支援資訊與 agent 規範維護原則。它只在調整 agent 入口、skill、橋接或支援策略時載入，不屬於一般工程任務的常駐 Context。

## 權威鏈

1. `AGENTS.md`：跨工具共同入口，只放載入順序、授權邊界與安全紅線。
2. `.agents/skills/inputbox-dev/SKILL.md`：InputBox 權威工程 skill，負責任務路由。
3. `docs/engineering/`：任務領域的原子化工程規範。
4. `CLAUDE.md` 與 `.claude/skills/inputbox-dev/SKILL.md`：Claude Code 必要橋接。

不要新增重複的 root instructions、舊版相容入口，或工具專屬的完整規範副本。細節規則只在一個權威位置維護，其他入口只引用該位置。

## 支援矩陣

| 工具 | 入口 | Skill 路徑 | Repo 策略 |
|---|---|---|---|
| Codex CLI | `AGENTS.md` | `.agents/skills/inputbox-dev/SKILL.md` | 使用共同入口與權威 project skill。 |
| Claude Code | `CLAUDE.md` 匯入 `AGENTS.md` | `.claude/skills/inputbox-dev/SKILL.md` 橋接 | 僅作橋接；權威規範仍在 `.agents/skills` 與 `docs/engineering/`。 |
| GitHub Copilot CLI | `AGENTS.md` | `.agents/skills/inputbox-dev/SKILL.md` | 使用 root primary instructions 與權威 project skill。 |
| Antigravity CLI | `AGENTS.md` | `.agents/skills/inputbox-dev/SKILL.md` | 使用共同入口與權威 project skill；目前不新增 workspace rules。 |

## 官方依據與查核日期

- OpenAI（2026-07-30）：[`AGENTS.md` custom instructions](https://developers.openai.com/codex/guides/agents-md) 與 [Codex skills](https://developers.openai.com/codex/skills)。
- Claude Code、GitHub Copilot CLI 與 Antigravity CLI 的既有支援策略最後查核於 2026-05-25；若修改對應橋接或支援聲明，須先以各供應商最新官方文件重新查核。

## Context 維護原則

- 常駐 `AGENTS.md` 只保留每次任務都必須知道的內容。
- 任務路由集中在 project skill；工程細節集中在對應 `docs/engineering/` 文件。
- 每條規則只維護一次。安全紅線可在根入口保留精簡摘要，但操作細節不得跨檔複製。
- 新增規則前，確認它代表產品要求、安全界線或已觀察到的失敗模式；不要加入沒有可驗證效果的通用流程或風格要求。
- 精簡規範時一次只移除一組指令，使用代表性任務確認載入路由、授權行為與驗證結果沒有退化。

## 代表性驗證情境

調整 agent 規範後，至少抽查下列情境：

1. 只讀審查：不應自行修改檔案。
2. 一般程式碼修改：載入環境與核心工程規範並執行必要驗證。
3. UI 修改：額外載入 A11y 與視覺安全規範。
4. 控制器修改：載入 Gamepad API；涉及硬體路徑時再載入 GameInput 手動驗證矩陣。
5. 在地化修改：載入在地化規範並檢查必要 `.resx`。
6. Git 提交：載入 Git、安全與合規規範，使用既有 GPG 設定並驗證簽章。
