# Git 提交與安全性紅線 (Git & Safety)

## 1. 安全性紅線 (Safety Boundaries)
- **零互動/注入**：禁止修改第三方程式記憶體、封包或電磁紀錄。
- **零模擬/同步**：行為僅止於「複製至剪貼簿」，不模擬輸入至其他視窗。
- **零自動化**：禁止實作自動化遊戲行為 (如自動連點、自動施法)。
- **零偵測性**：禁止主動偵測特定第三方應用程式。
- **前景視窗返回的允許範圍**：`WindowFocusService` 只把焦點還給使用者呼叫 InputBox 前的前景視窗；目標由當下前景視窗捕捉，唯一的程序判斷是排除 InputBox 自己，不依程序名稱、視窗標題或類別辨識任何特定應用程式。
  - 允許的 Win32 呼叫僅限 `ShowWindow`（且只在視窗最小化時還原）、`BringWindowToTop`、`SetForegroundWindow`、`SetFocus`，以及切換瞬間暫時使用 `AttachThreadInput` 連結執行緒輸入佇列，並必須在 `finally` 中解除。
  - `AttachThreadInput` 只用於取得切換前景的權限，不送出任何按鍵、滑鼠或視窗訊息，也不讀寫對方程序的記憶體，因此不屬於模擬輸入或修改第三方程式行為。
  - 不得在此路徑加入 `SendInput`、`keybd_event`、`PostMessage`／`SendMessage` 輸入訊息、剪貼簿自動貼上或任何依應用程式身分分流的邏輯。
  - 程式內重啟只可對新啟動的 InputBox 程序呼叫 `AllowSetForegroundWindow(PID)`，不得使用 `ASFW_ANY` 開放給所有程序（無法啟動新程序的退回路徑除外）。

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

## 4. Trunk-based 分支策略與合併守門

### 4.1 分支角色與開始條件

- **`main` 是唯一主幹**，必須隨時保持為可發布版本；`dev` 是保留名稱、每輪重新對齊的開發分支，不是另一條長期整合主幹。
- 開始工作前先執行 `git fetch origin`，確認工作區、`origin/dev` 與 `origin/main` 的最新狀態。未提交變更或其他人的新提交不得被覆蓋。
- **`dev` 必須先 rebase 到最新 `origin/main`**：有待發布提交時使用 `git rebase --gpg-sign origin/main`，以使用者既有金鑰簽署重建的提交；沒有待發布差異時依 4.5 對齊。不得以 `git merge origin/main`、`git pull` 產生的 merge 或 GitHub 的 Update branch 合併操作代替。
- PR 開啟期間若 `main` 前進，必須再次 fetch、簽章 rebase、驗證並更新 `dev`，重新等待最新 SHA 的 CI；不能因先前 CI 已通過便略過同步。

### 4.2 線性歷史與 GPG 簽章

- **禁止多餘的 merge commit**：不得為同步 `main` 到 `dev` 或將 `dev` 整合回 `main` 新增 merge commit；不得使用 `--no-ff`、GitHub Create a merge commit 或 `--rebase-merges` 保留本輪不必要的分叉。檢查範圍是本輪 `origin/main..dev`，不要求重寫既有主幹歷史。
- **保留既有 GPG 簽章環境與簽章要求**：新提交使用 `git commit -S`；rebase 會改變提交物件，舊簽章不能原封不動移植，必須用既有金鑰重新簽署，而不是讓重建提交變成未簽章。
- rebase 後逐筆執行 `git verify-commit <SHA>`，驗證本輪所有提交；`git rev-list --merges origin/main..dev` 必須沒有輸出。任何簽章失敗都依第 3 節停止，不得修改 GPG 設定或關閉簽章。
- 發布到 `main` 時必須保留已驗證的提交 SHA 與 GPG 簽章；僅有相同檔案樹、只在 `dev` 留下原簽章，或只看到 GitHub 的 Verified 標籤，均不足以證明符合使用者既有金鑰的簽章要求。

### 4.3 推送與 PR 守門

- 功能、修補與文件變更均由 `dev` 提出 PR 回到 `main`；不得繞過 PR、必要 CI 或分支保護直接推送主幹。
- 首次正常推送不需強制；已推送的 `dev` 因 rebase 或對齊而改寫時，先記錄 fetch 後的遠端 SHA，經既有授權後使用 `git push --force-with-lease=refs/heads/dev:<已記錄的遠端 SHA> origin dev`。不得使用不帶保護的 `--force`，不得遇到 lease 失敗就改用新的預期值覆蓋遠端。
- 合併前必須確認：`origin/main` 是 `dev` 的祖先、本輪無 merge commit、每筆簽章有效，且 PR 最新 head SHA 的全部必要檢查成功；不得使用舊 SHA、取消、仍執行中或僅工作流外層成功的結果代替實際測試通過。
- `.github/workflows/ci.yml` 所要求的建置與測試均須通過。若某步驟允許 `continue-on-error`，仍須讀取實際日誌，確認測試失敗數為零；MSBuild 不得有警告或錯誤。

### 4.4 合併方法與遠端限制

- **預設整合語意是 fast-forward**：經 PR 審查與 CI 守門後，讓 `main` 前移到已驗證的 `dev` 提交，不建立額外提交、不改寫 SHA，也不丟失既有 GPG 簽章。
- **不得直接選用 GitHub Rebase and merge**：GitHub 會重建提交，不能保留原提交的簽章驗證。Squash and merge 也會建立新的提交，不能視為保留原 GPG 簽章的等價方式。一般 Create a merge commit 則違反本節的線性歷史要求。
- 執行前必須確認實際平台與 repository ruleset 是否提供同時滿足 PR 守門、fast-forward 與保留簽章的整合方式；不得假設 GitHub 標準合併按鈕提供 fast-forward-only。
- 若現有遠端規則無法同時滿足上述要求，**停止合併並向維護者具體回報衝突**，取得對整合方式或規則調整的明確授權後才可繼續。不得自行改成 merge commit、丟棄簽章、繞過保護或直接推送 `main`。
- 合併後確認 PR 已 merged、`main` 新增的提交沒有 merge commit、簽章仍有效，並核對合併內容與通過 CI 的版本一致。追蹤合併後實際觸發的所有必要檢查至完成；若建置僅由 PR 觸發，須如實說明，不能聲稱 main push 已重跑建置與測試。

### 4.5 合併後立即重新對齊 dev

- **保留 `dev`，不得刪除；每次 PR 合併完成後就立即對齊 `main`**，不得延後到下一輪開發才處理。
- 重新 fetch，確認 PR 已合併、工作區沒有未提交變更，且 `dev` 沒有其他尚未發布或他人新增的工作；如有則停止重設並回報，不能直接捨棄。
- 在已授權重設與推送的範圍內，切到 `dev`，執行 `git reset --hard origin/main`，再以 4.3 的精確 `--force-with-lease` 更新遠端 `dev`。若尚無破壞性操作或外部寫入授權，先取得授權再執行。
- 完成後驗證本機 `dev`、`origin/dev` 與最新遠端 `main` SHA 完全相同，且工作區乾淨；若主幹又前進或遠端 dev 已變動，重新檢查，不能盲目重設或重試強制推送。

### 4.6 發版來源

- 正式版本標籤只能從最新遠端 `main` 建立；不得從未整合的 `dev`、舊提交或其他分支打 tag 發布。
- 標籤依第 3 節使用既有 GPG 環境簽署並驗證，推送後追蹤實際發佈結果；不得只看到 tag 已推送便聲稱 Release 成功。

參考：[Git rebase 簽章選項](https://git-scm.com/docs/git-rebase)、[GitHub rebase and merge 的簽章限制](https://docs.github.com/en/authentication/managing-commit-signature-verification/about-commit-signature-verification)、[GitHub 合併方法](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/about-merge-methods-on-github)。

## 5. 法律合規工作流
1. 若任務涉及「按鍵處理」、「剪貼簿自動化」或「控制器映射」。
2. 使用對應代理可用的網頁抓取工具訪問上述連結（例如 Copilot：`fetch_webpage`；Gemini：`web_fetch`）。
3. 若條款頁面僅回傳 JavaScript shell 或無法直接取得條款本文，必須改用可執行 JavaScript 的瀏覽器工具、官方網站中可抓取的同等條款頁，或明確記錄「未能擷取完整本文」並停止進行高風險核心輸入／輸出異動。
4. 比對異動是否符合「非自動化」、「非模擬」原則。
5. 在計畫書 (/plan) 或交付摘要中聲明合規性分析結果。
