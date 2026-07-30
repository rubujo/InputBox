---
name: update-inputweave-gameinput
description: Claude Code 對 InputWeave.GameInput 更新技能的橋接。更新 InputBox 內嵌套件、來源 commit、雜湊、授權、CI 或 gh-pages 第三方資訊時使用。
---

# InputWeave.GameInput Claude Code Skill Bridge

本檔只負責讓 Claude Code discovery 找到跨 Agent project skill。權威 skill 位於 `.agents/skills/update-inputweave-gameinput/SKILL.md`。

開始工作前：

1. 讀取 `.agents/skills/update-inputweave-gameinput/SKILL.md`。
2. 依該 skill 載入 `.agents/skills/inputbox-dev/SKILL.md` 與任務相關工程規範。
3. 依 `AGENTS.md` 確認安全紅線與 Git 簽章要求。

不要在本檔維護第二份更新流程。
