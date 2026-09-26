---
name: update-inputweave-gameinput
description: 更新或重新封裝 InputBox 內嵌的 InputWeave.GameInput 套件，並同步來源 commit、SHA-256、授權、CI、release workflow 與 gh-pages 第三方資訊時使用。本檔為 Claude Code 橋接，權威流程在 .agents/skills/update-inputweave-gameinput/SKILL.md。
---

# InputWeave.GameInput Claude Code Skill Bridge

本檔只負責讓 Claude Code discovery 找到跨 Agent project skill。權威 skill 位於 `.agents/skills/update-inputweave-gameinput/SKILL.md`。

開始工作前：

1. 讀取 `.agents/skills/update-inputweave-gameinput/SKILL.md`。
2. 依該 skill 載入 `.agents/skills/inputbox-dev/SKILL.md` 與任務相關工程規範。
3. 依 `AGENTS.md` 確認安全紅線與 Git 簽章要求。

不要在本檔維護第二份更新流程。
