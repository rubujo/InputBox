---
name: inputbox-dev
description: InputBox 專案的工程規範與安全標準。當修改程式碼、設計 UI、實作控制器邏輯、處理在地化或準備 Git 提交時，請使用此技能以確保符合專案專屬規則。
---

# InputBox 工程規範指引 (Engineering Guidelines)

本技能提供 InputBox 專案的權威工程標準。為了確保系統安全性、無障礙 (A11y) 與技術完整性，你**必須參考**位於 `docs/engineering/` 目錄下的原子化規範檔案。

## 核心規範索引 (Core Reference Index)

請根據目前任務載入相關檔案：

1.  **環境與編碼**：`docs/engineering/environment.md`
2.  **核心工程 (.NET/非同步/鎖/資源)**：`docs/engineering/core-engineering.md`
3.  **A11y 與視覺安全 (關鍵)**：`docs/engineering/a11y-safety.md`
4.  **遊戲控制器 API (XInput/GameInput)**：`docs/engineering/gamepad-api.md`
5.  **在地化與術語規範**：`docs/engineering/localization.md`
6.  **Git 提交與安全性紅線**：`docs/engineering/git-commit-safety.md`
7.  **測試規範 (xUnit / 隔離模式 / CI)**：`docs/engineering/testing.md`

## 工作流程指令 (Workflow Mandates)

- **UI/並行處理**：在實作前，務必先閱讀 `a11y-safety.md` 與 `core-engineering.md`。
- **ToS 驗證 (核心要求)**：涉及輸入、輸出或控制器邏輯變更時，**必須**使用網頁抓取工具（Copilot：`fetch_webpage`；Gemini：`web_fetch`）擷取 `git-commit-safety.md` 中列出的第三方服務條款進行即時合規分析。
- **資源管理**：所有 IDisposable 資源必須遵循「原子化處置模式」。
- **合規性**：提交前須對照 `git-commit-safety.md` 檢查異動，避免觸發防弊系統。
