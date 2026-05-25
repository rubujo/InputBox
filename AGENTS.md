# InputBox - Agent 工作區入口

本檔是 InputBox repo root 的唯一跨工具 agent 指引入口，供支援 `AGENTS.md` 的 Codex CLI、GitHub Copilot CLI 與 Antigravity CLI 使用。Claude Code 只透過最小 `CLAUDE.md` 與 Claude project skill 橋接進入同一條權威鏈，不維護第二份完整規範。

開始任何修改前，先讀取 `.agents/skills/inputbox-dev/SKILL.md`，再依任務類型讀取 `docs/engineering/` 下的對應規範。

## 0. Agent 支援結構

### 0.1 官方依據查核日期：2026-05-25

- Codex CLI：OpenAI 文件定義 `AGENTS.md` 為 custom instructions 檔，repo skills 位於 `.agents/skills`。
- Claude Code：Anthropic 文件定義 project memory 位於 `CLAUDE.md` 或 `.claude/CLAUDE.md`，`CLAUDE.md` 可匯入 `AGENTS.md`，project skills 位於 `.claude/skills`。
- GitHub Copilot CLI：GitHub 文件定義 root `AGENTS.md` 為 primary instructions，project skills 可位於 `.agents/skills`。
- Antigravity CLI：Google 文件定義 workspace skills 位於 `.agents/skills`；若未來需要 workspace rules，使用 `.agents/rules`。

### 0.2 權威鏈

1. `AGENTS.md`：跨工具共同入口，只放載入順序、安全紅線與工程文件索引。
2. `.agents/skills/inputbox-dev/SKILL.md`：InputBox 權威工程 skill。
3. `docs/engineering/`：任務領域的原子化工程規範。
4. `CLAUDE.md` 與 `.claude/skills/inputbox-dev/SKILL.md`：Claude Code 必要橋接。

不要新增重複的 root instructions、舊版相容入口，或任何工具專屬的完整規範副本。細節規範只維護在 project skill 與 `docs/engineering/`。

### 0.3 支援矩陣

| 工具 | 入口 | Skill 路徑 | Repo 策略 |
|---|---|---|---|
| Codex CLI | `AGENTS.md` | `.agents/skills/inputbox-dev/SKILL.md` | 使用共同入口與權威 project skill。 |
| Claude Code | `CLAUDE.md` 匯入 `AGENTS.md` | `.claude/skills/inputbox-dev/SKILL.md` 橋接 | 僅作橋接；權威規範仍在 `.agents/skills` 與 `docs/engineering/`。 |
| GitHub Copilot CLI | `AGENTS.md` | `.agents/skills/inputbox-dev/SKILL.md` | 使用 root primary instructions 與權威 project skill。 |
| Antigravity CLI | `AGENTS.md` | `.agents/skills/inputbox-dev/SKILL.md` | 使用共同入口與權威 project skill；目前不新增 workspace rules。 |

## 1. 必讀規範索引

至少依任務類型讀取下列文件：

| 任務類型 | 必讀文件 |
|---|---|
| 任何程式碼異動 | `docs/engineering/environment.md`、`docs/engineering/core-engineering.md` |
| Steam Deck、Wine、Proton、Gamescope、支援平台或 UI 技術方向 | `docs/engineering/environment.md` |
| UI、WinForms、DPI、版面、視覺回饋、螢幕報讀 | `docs/engineering/a11y-safety.md` |
| 控制器輸入、XInput、GameInput、按鍵映射 | `docs/engineering/gamepad-api.md` |
| 使用者可見文字、`.resx`、術語、助記鍵 | `docs/engineering/localization.md` |
| 測試、xUnit v3、冒煙測試、覆蓋率 | `docs/engineering/testing.md` |
| Git 工作流、安全紅線、合規 | `docs/engineering/git-commit-safety.md` |

## 2. 安全性紅線

以下限制為硬性要求：

- 禁止記憶體注入、封包攔截、電磁紀錄，或修改第三方程式行為。
- 禁止模擬輸入到其他視窗；輸出邊界僅限於「複製到剪貼簿」。
- 禁止實作任何自動化遊戲行為，例如自動連點、自動施法、輪播或掛機控制。
- 禁止主動偵測特定第三方應用程式。
- Git 提交必須使用使用者既有的 GPG 簽章設定；不得修改 `gpg.conf`、`gpg-agent.conf`、`common.conf` 或相關簽章設定。若簽章失敗，停止並回報。

若任務涉及輸入流程、剪貼簿流程、快速鍵、控制器映射、輸出行為或返回前景視窗邏輯，交付前必須依 `docs/engineering/git-commit-safety.md` 完成即時合規檢查。

## 3. 環境與編碼

- 預設作業系統：Windows。
- 預設 shell：PowerShell。
- 執行可能輸出非 ASCII 文字的 PowerShell 指令前，先設定 UTF-8：

```powershell
[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
```

- 遵循 `.editorconfig`。
- 新增或修改 `*.cs`、`*.resx` 時，必須使用 UTF-8 with BOM 與 CRLF。
- 其他文字檔使用 UTF-8 與 CRLF。

## 4. 完成前檢查

依異動類型確認下列項目：

1. `dotnet build src/InputBox/InputBox.csproj --configuration Debug`
2. 異動行為或測試時，執行 `dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj`
3. 修改過的 `*.cs` 沒有新增 IDE 或 CS 診斷
4. 使用者可見文字異動已同步更新所有必要 `.resx`
5. 輸入、輸出、剪貼簿、快速鍵或控制器異動已依 `docs/engineering/git-commit-safety.md` 完成合規檢查
