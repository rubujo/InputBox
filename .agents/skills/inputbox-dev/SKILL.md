---
name: inputbox-dev
description: InputBox 專案的權威工程技能。修改程式碼、UI、控制器邏輯、在地化、測試、工程規範或 Git 工作流時使用；一般只讀問答不需啟用，除非問題直接涉及專案工程規範。
---

# InputBox 工程規範指引

本技能負責把修改任務路由到相關工程規範。`AGENTS.md` 只維護跨工具入口、授權邊界與安全紅線；工程細節只維護在 `docs/engineering/`。Claude Code 若透過 `.claude/skills/inputbox-dev/SKILL.md` 進入，也必須回到本技能與對應工程文件。

## 任務路由

只讀取目前任務需要的文件：

| 任務類型 | 必讀文件 |
|---|---|
| 任何程式碼異動 | `docs/engineering/environment.md`、`docs/engineering/core-engineering.md` |
| Steam Deck、Wine、Proton、Gamescope、支援平台或 UI 技術方向 | `docs/engineering/environment.md` |
| UI、WinForms、DPI、版面、視覺回饋、螢幕報讀 | `docs/engineering/a11y-safety.md` |
| 控制器輸入、XInput、GameInput、按鍵映射 | `docs/engineering/gamepad-api.md` |
| GameInput 套件、硬體路徑、連線、callback、rumble、redist 或正式發佈驗證 | `docs/engineering/gameinput-hardware-verification.md` |
| 更新 InputWeave.GameInput release 資產、雜湊、授權或 gh-pages 第三方資訊 | `.agents/skills/update-inputweave-gameinput/SKILL.md` |
| 使用者可見文字、`.resx`、術語、助記鍵 | `docs/engineering/localization.md` |
| 測試、xUnit v3、冒煙測試、覆蓋率 | `docs/engineering/testing.md` |
| Git 工作流、輸入或輸出邏輯、剪貼簿、快速鍵、控制器映射、安全與合規 | `docs/engineering/git-commit-safety.md` |
| Agent 規範、支援工具、權威鏈或 Claude Code 橋接 | `docs/engineering/agent-support.md` |

## 工作流程

1. 根據任務路由讀取必要文件；不要為無關任務載入整個 `docs/engineering/`。
2. UI 或並行處理異動須同時套用 `a11y-safety.md` 與 `core-engineering.md`。
3. 輸入、輸出、剪貼簿、快速鍵或控制器邏輯異動，須依 `git-commit-safety.md` 使用官方網頁來源完成即時 ToS 合規分析。
4. 執行相關規範要求的建置、測試、診斷、在地化或人工驗證，並回報結果與未執行項目。
5. 若要提交 Git，完全依 `git-commit-safety.md` 使用既有 GPG 設定；簽章失敗時停止並回報。
