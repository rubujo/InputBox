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
- **簽章流程僅可使用使用者既有的 Git、GPG、pinentry 與 gpg-agent 設定**；Agent 不得假設、重建或覆寫使用者的本機簽章環境。
- **嚴禁 Agent 自行建立、修改或覆寫 GPG / gpg-agent 設定檔**（例如 `~/.gnupg/gpg.conf`、`gpg-agent.conf`、`common.conf`），也不得為了排除簽章問題而擅自匯入、切換或重設金鑰與相關設定。
- **若簽章失敗**，Agent 應回報錯誤並提醒使用者自行檢查或授權其簽章環境；**不得** 自動修改設定檔案作為修復手段。
- **不得** 為了繞過本機 pinentry、gpg-agent 或終端互動問題，而使用 `--no-gpg-sign`、`git -c commit.gpgsign=false` 等方式提交；除非維護者明確授權，否則應先由使用者調整其簽章環境後再提交。

## 4. 分支策略與合併守門
- **採用 main-first 工作流**：`main` 必須隨時保持為可發布版本；每次新功能、修補或熱修開始前，`dev` 必須先同步或重設到目前 `main` 的最新提交，確保兩者起點一致。
- **合併路徑固定**：功能完成後，必須由 `dev` 提出 Pull Request 合併回 `main`；不得直接將應用程式變更推入 `main`。
- **CI 為必要守門**：Pull Request 在合併前，必須先確認 `.github/workflows/ci.yml` 全部必要檢查為成功狀態，未通過不得合併。
- **保留分支但每輪重對齊**：`dev` 分支可保留且 Pull Request 合併完成後**不得刪除**，但下一輪開發開始前必須重新對齊 `main`；不得讓 `dev` 長期累積與 `main` 不一致的未發布差異。
- **發版來源限制**：建立正式版本標籤時，只能以 `main` 分支最新提交為基準；不得從 `dev`、舊提交或其他分支直接打 tag 發布。

## 5. 法律合規工作流
1. 若任務涉及「按鍵處理」、「剪貼簿自動化」或「控制器映射」。
2. 使用對應代理可用的網頁抓取工具訪問上述連結（例如 Copilot：`fetch_webpage`；Gemini：`web_fetch`）。
3. 比對異動是否符合「非自動化」、「非模擬」原則。
4. 在計畫書 (/plan) 中聲明合規性分析結果。
