# Git 提交與安全性紅線 (Git & Safety)

## 1. 安全性紅線 (Safety Boundaries)
- **零互動/注入**：禁止修改第三方程式記憶體、封包或電磁紀錄。
- **零模擬/同步**：行為僅止於「複製至剪貼簿」，不模擬輸入至其他視窗。
- **零自動化**：禁止實作自動化遊戲行為 (如自動連點、自動施法)。
- **零偵測性**：禁止主動偵測特定第三方應用程式。

## 2. 外部合規基準與 ToS 驗證 (ToS Verification)
**代理人必須在變更任何核心輸入/輸出邏輯前，執行 `web_fetch` 擷取並分析以下網址的最新內容，確保設計不違反服務條款：**

- **FINAL FANTASY XIV 繁體中文版**：
  - [使用者合約](https://www.ffxiv.com.tw/web/user_agreement.html)
  - [授權條款](https://www.ffxiv.com.tw/web/license.html)
- **宇峻奧汀 (UserJoy)**：
  - [隱私權政策](https://www.userjoy.com/mp/privacy.aspx)
  - [使用者授權合約 (EULA)](https://www.userjoy.com/mp/eula.aspx)
  - [免責聲明](https://www.uj.com.tw/uj/service/service_user_disclaimer.aspx)

## 3. Git 提交規範
- 遵循 **Conventional Commits v1.0.0**。
- 格式：`<type>(<scope>): <description>`。
- 提交訊息必須包含 **Subject** 與 **Body**。
- 訊息預設使用 **正體中文**。

## 4. 法律合規工作流
1. 若任務涉及「按鍵處理」、「剪貼簿自動化」或「控制器映射」。
2. 使用 `web_fetch` 訪問上述連結。
3. 比對異動是否符合「非自動化」、「非模擬」原則。
4. 在計畫書 (/plan) 中聲明合規性分析結果。
