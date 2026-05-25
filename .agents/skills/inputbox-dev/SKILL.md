---
name: inputbox-dev
description: InputBox 專案的權威工程技能。當修改程式碼、設計 UI、實作控制器邏輯、處理在地化、調整測試或準備 Git 提交時，請使用此技能。
---

# InputBox 工程規範指引

本技能是 InputBox 的權威 project skill。`AGENTS.md` 只負責載入順序與索引；詳細工程規範以本技能和 `docs/engineering/` 為準。Claude Code 若透過 `.claude/skills/inputbox-dev/SKILL.md` 進入，也必須回到本技能與對應工程文件。

## 核心規範索引

請根據目前任務載入相關檔案：

1. **環境與編碼**：`docs/engineering/environment.md`
2. **核心工程（.NET／非同步／鎖／資源）**：`docs/engineering/core-engineering.md`
3. **A11y 與視覺安全**：`docs/engineering/a11y-safety.md`
4. **遊戲控制器 API（XInput／GameInput）**：`docs/engineering/gamepad-api.md`
5. **在地化與術語規範**：`docs/engineering/localization.md`
6. **Git 提交與安全性紅線**：`docs/engineering/git-commit-safety.md`
7. **測試規範（xUnit／隔離模式／CI）**：`docs/engineering/testing.md`

## 工作流程指令

- **UI/並行處理**：在實作前，務必先閱讀 `a11y-safety.md` 與 `core-engineering.md`。
- **ToS 驗證**：涉及輸入、輸出或控制器邏輯變更時，必須使用可用的官方網頁工具擷取 `git-commit-safety.md` 中列出的第三方服務條款，進行即時合規分析。
- **資源管理**：所有 IDisposable 資源必須遵循「原子化處置模式」。
- **合規性**：提交前須對照 `git-commit-safety.md` 檢查異動，避免觸發防弊系統。
- **GPG 簽章提交**：凡執行 Git 提交，預設必須使用使用者既有且有效的 GPG 簽章設定；Agent 嚴禁自行修改 `gpg.conf`、`gpg-agent.conf` 或其他相關設定檔。若簽章環境異常，僅可提醒使用者自行修復，不得以停用簽章或自動改寫設定方式繞過。
