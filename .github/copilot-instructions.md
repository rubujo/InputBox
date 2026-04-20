# InputBox gh-pages - GitHub Copilot 指引

本檔提供 GitHub Copilot 使用，特別是 **Visual Studio 的 Copilot Chat**。由於不同 Copilot 客戶端對 `AGENTS.md` 的支援程度不同，本檔保留為 Copilot 的穩定入口；共同規範仍以根目錄 [AGENTS.md](../AGENTS.md)、`.agents/skills/inputbox-web-dev/SKILL.md` 與 `docs/engineering/` 為準。

## 1. 讀取順序

開始任何任務前，請依序參考：

1. 本檔
2. 根目錄 `AGENTS.md`
3. `.agents/skills/inputbox-web-dev/SKILL.md`
4. `docs/engineering/` 下與任務相關的規範文件

## 2. Copilot 必守事項

- 嚴格遵守 `docs/engineering/web-architecture.md`、`docs/engineering/web-a11y-safety.md` 與 `docs/engineering/web-git.md`。
- 禁止 JavaScript、圖片資產與 Inline CSS。
- 所有互動必須以純 CSS 實作，且 `body:has()` 規則必須附帶 `@supports selector()` 防禦與退化方案。
- 修改文字時必須同步更新七語內容，且不得以任一語系回退替代其他語系。
- 所有異動都必須遵循 repo 根目錄 `.editorconfig`、`.gitattributes` 與 `.prettierrc.json`。
- Git 提交必須使用使用者既有的 GPG 簽章設定；不得修改任何 GPG 或 gpg-agent 設定檔。

## 3. Copilot Chat 與 Agent 的適用策略

- 在 **Visual Studio** 內，請以本檔作為 Copilot Chat 的主要入口。
- 在 **VS Code** 與 **Copilot cloud agent** 內，除本檔外，也應讀取 `AGENTS.md`。
- 若任務涉及 HTML 結構、多語系、A11y、CSS 狀態切換、格式化或 Git 提交，必須主動展開對應的 `docs/engineering/web-*.md`。

## 4. 常用驗證

```powershell
npm run format:check
npm test
```
