# Git 提交與安全性紅線 (Git & Safety)

## 1. 安全性紅線 (Safety Boundaries)
- **零互動/注入**：禁止修改第三方程式記憶體、封包或電磁紀錄。
- **零模擬/同步**：行為僅止於「複製至剪貼簿」，不模擬輸入至其他視窗。
- **零自動化**：禁止實作自動化遊戲行為 (如自動連點、自動施法)。
- **零偵測性**：禁止主動偵測特定第三方應用程式。

## 2. 外部合規基準與 ToS 驗證 (ToS Verification)
**代理人必須在變更任何核心輸入/輸出邏輯前，使用網頁抓取工具擷取並分析以下網址的最新內容（例如 Copilot：`fetch_webpage`；Gemini：`web_fetch`），確保設計不違反服務條款：**

- **FINAL FANTASY XIV 繁體中文版**：
  - [遊戲使用者合約](https://www.ffxiv.com.tw/web/user_agreement.html)
  - [第三方授權聲明](https://www.ffxiv.com.tw/web/license.html)
- **宇峻奧汀 (UserJoy)**：
  - [隱私權政策](https://www.userjoy.com/mp/privacy.aspx)
  - [使用條款](https://www.userjoy.com/mp/eula.aspx)
  - [免責聲明](https://www.uj.com.tw/uj/service/service_user_disclaimer.aspx)

## 3. Git 提交規範
- 遵循 **Conventional Commits v1.0.0**。
- 格式：`<type>(<scope>): <description>`。
- 提交訊息必須包含 **Subject** 與 **Body**。
- 訊息預設使用 **正體中文**。
- **所有 Git 提交預設必須使用 GPG 簽章**，並在提交後以 `git log --show-signature -1` 或 `git verify-commit HEAD` 驗證為有效簽章。
- **不得** 為了繞過本機 pinentry、gpg-agent 或終端互動問題，而使用 `--no-gpg-sign`、`git -c commit.gpgsign=false` 等方式提交；除非維護者明確授權，否則應先修復簽章環境再提交。

## 4. 法律合規工作流
1. 若任務涉及「按鍵處理」、「剪貼簿自動化」或「控制器映射」。
2. 使用對應代理可用的網頁抓取工具訪問上述連結（例如 Copilot：`fetch_webpage`；Gemini：`web_fetch`）。
3. 比對異動是否符合「非自動化」、「非模擬」原則。
4. 在計畫書 (/plan) 中聲明合規性分析結果。
