---
name: inputbox-dev
description: InputBox 專案開發工程規範技能。當任務涉及程式碼修改、UI 設計、控制器實作、多語系翻譯或 Git 提交時，必須載入此技能以確保符合專案標準。
---

# InputBox 工程規範指引

本技能是 InputBox 專案的專業開發手冊。為了確保系統安全性與無障礙 (A11y) 合規，請在執行任何操作前，**必須讀取**並遵循位於 `docs/engineering/` 目錄下的原子化規範檔案。

## 核心規範索引 (References)

請根據任務性質，使用 `read_file` 讀取對應規範：

1.  **環境與編碼**：`docs/engineering/environment.md`
2.  **核心工程 (.NET/非同步/鎖/資源)**：`docs/engineering/core-engineering.md`
3.  **A11y 無障礙與視覺安全 (非常重要)**：`docs/engineering/a11y-safety.md`
4.  **遊戲控制器 API (XInput/GameInput)**：`docs/engineering/gamepad-api.md`
5.  **在地化與術語表**：`docs/engineering/localization.md`
6.  **Git 提交規範與法律安全性紅線**：`docs/engineering/git-commit-safety.md`

## 工作流程要求

- **研究階段**：涉及 UI 或多執行緒時，必須先讀取 `a11y-safety.md` 與 `core-engineering.md`。
- **法律驗證 (核心指令)**：涉及任何輸入、輸出或遊戲控制器邏輯變更前，**必須**依照 `git-commit-safety.md` 中的清單執行 `web_fetch` 擷取第三方合約進行即時分析。
- **執行階段**：確保所有 CancellationToken 與 Dispose 邏輯符合「原子化處置模式」。
- **驗證階段**：提交前再次對照紅線檢查，並在提交說明中簡述合規性。
