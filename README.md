# 輸入框（InputBox）- 專案網頁

本分支為輸入框專案的 GitHub Pages 靜態網站來源。
網站主要內容在首頁，README 僅提供快速導覽與維護說明。

## 入口連結

- 專案網頁：<https://rubujo.github.io/InputBox/>
- 下載最新版本：<https://github.com/rubujo/InputBox/releases>
- 原始碼倉庫：<https://github.com/rubujo/InputBox>

## 網站內容摘要

- 專案簡介與適用環境。
- 鍵盤與遊戲控制器操作對照。
- 設定項目與常見問題。
- 多語系內容（正體中文、英文、德文、法文、日文、韓文、簡體中文）。
- 授權與第三方元件聲明。

## 維護說明

- 本分支以靜態頁面為主，首頁檔案為 `index.html`。
- 規範入口文件為 `ENGINEERING_GUIDELINES.md`，其下連結 `docs/engineering/` 原子化規範。
- README 僅作為倉庫說明文件，不屬於最終 Pages 發佈內容。
- 第三方函式庫清單與授權內容請以 `main` 分支 `README.md` 為權威來源，本分支內容須與其同步。
- 更新網站文案時，請以首頁內容為準，README 只保留摘要與入口資訊。
- 提交前需執行 `npm test`（Playwright + axe-core）並確認通過。
