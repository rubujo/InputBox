---
name: update-inputweave-gameinput
description: 更新 InputBox 內嵌的 InputWeave.GameInput GitHub release 套件，並同步來源 commit、SHA-256、授權、CI、release workflow、測試文件與 gh-pages 七語系內容。使用者要求升級、重新封裝、替換同版號資產、查核 InputWeave.GameInput 發布內容，或同步 dev 與 gh-pages 的 GameInput 第三方資訊時使用。
---

# InputWeave.GameInput 更新流程

以 GitHub release 資產、sidecar 雜湊與套件 nuspec 為三方證據完成更新。將應用程式分支與 `gh-pages` 視為兩個獨立交付面，分別驗證、提交與推送。

## 載入規範

1. 先讀取 `.agents/skills/inputbox-dev/SKILL.md`。
2. 依該 skill 載入：
   - `docs/engineering/environment.md`
   - `docs/engineering/gamepad-api.md`
   - `docs/engineering/gameinput-hardware-verification.md`
   - `docs/engineering/testing.md`
   - `docs/engineering/git-commit-safety.md`
3. 若修改跨 Agent 入口或橋接，再讀取 `docs/engineering/agent-support.md`。
4. 切換或建立 `gh-pages` worktree 後，讀取該分支自己的 `AGENTS.md`、project skill 與網站工程規範；不要假設 `dev` 規範可取代它們。

## 1. 建立發布證據

1. 從 `https://github.com/rubujo/InputWeave.GameInput/releases` 查核使用者指定或最新 release。
2. 優先使用 `gh release view` 取得 tag、發布時間、資產名稱、大小、下載 URL 與 GitHub digest。
3. 下載 `.nupkg` 與 `.sha256` 至 repo 內唯一命名的 `.tmp` 子目錄。
4. 驗證：
   - 實際 SHA-256 等於 sidecar。
   - 實際 SHA-256 等於 GitHub asset digest（若 GitHub 提供）。
   - nuspec 的版本與 `repository commit`。
   - 套件內容沒有意外納入 Microsoft GameInput redist 或 native shim。
5. 從目前 repo 中繼資料找出舊來源 commit，檢視舊、新 commit 間的 upstream diff。不要只依 tag 或套件版號判斷是否相同；同一 tag 的資產可能重新發布。
6. 記錄 InputWeave wrapper 版本、來源 commit、套件 SHA-256，以及其 Microsoft.GameInput 依賴版本。

任何一項證據不一致時停止，不要替換 repo 內套件。

## 2. 更新應用程式分支

先以 `rg` 搜尋所有 `InputWeave.GameInput`、舊 commit、舊雜湊與舊 Microsoft.GameInput 版本。至少檢查並視實際搜尋結果更新：

- `eng/nuget/InputWeave.GameInput.<version>.nupkg`
- `eng/nuget/InputWeave.GameInput.<version>.nupkg.sha256`
- `eng/nuget/InputWeave.GameInput_LICENSE.txt`
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `README.md`
- `tests/InputBox.Tests/README.md`
- 專案檔、NuGet 設定及其他搜尋命中的權威中繼資料

若 package version 改變，更新檔名與所有 package reference；若只是同版號資產重發，仍須替換 binary 並更新 commit、雜湊及授權來源。

修改後：

1. 確認舊 commit、舊雜湊與已淘汰的依賴版本不再出現在有效來源中；排除 `.git`、`.tmp`、`bin` 與 `obj`。
2. 確認所有文字檔符合 `.editorconfig`；本專案非 C#／resx 文字檔使用 UTF-8 無 BOM 與 CRLF。
3. 用新的唯一 `NUGET_PACKAGES` 暫存目錄執行 restore，避免同版號舊套件快取污染驗證。
4. 執行：

```powershell
dotnet restore src/InputBox/InputBox.csproj --force-evaluate
dotnet build src/InputBox/InputBox.csproj --configuration Debug --no-restore
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj
```

5. 若 upstream 只更新依賴或封裝中繼資料且沒有改變 InputBox 的輸入、輸出或映射語意，記錄不需重新做遊戲 ToS 查核。若實際行為有變，依 `git-commit-safety.md` 完成即時合規查核。
6. 依 upstream diff 判斷是否還需要 `gameinput-hardware-verification.md` 的實機矩陣；不要把未執行的硬體測試宣稱為通過。

## 3. 同步 gh-pages

1. `git fetch origin gh-pages`，確認本機與遠端沒有未處理的分歧。
2. 使用獨立 worktree 操作 `gh-pages`，不要在應用程式工作樹反覆切換分支。
3. 讀取 `gh-pages` 分支自身的 agent 與網站規範。
4. 在 `index.html` 的所有語系區塊同步：
   - InputWeave 來源 commit 與連結
   - 套件 SHA-256
   - Microsoft.GameInput 版本與授權連結
5. 搜尋舊值，並核對所有支援語系的出現次數；不可只更新單一語言。
6. 依該分支規範執行：

```powershell
npm ci
npm run format
npm run format:check
npm test
```

若 Playwright 內建瀏覽器版本不存在，可下載該 revision，或建立不提交的暫時 config 使用已安裝的 Chrome 完成同一測試集合。回報實際使用的瀏覽器，並刪除暫時 config、測試產物、下載殘片與 `node_modules`。

## 4. 提交與推送

1. 對兩個工作樹分別執行 `git diff --check`、`git status --short` 與必要測試。
2. `dev` 與 `gh-pages` 必須各自建立 Conventional Commit，包含正體中文 Subject 與 Body。
3. 使用既有 GPG 設定簽章；不得修改 GPG、gpg-agent、pinentry 或 Git signing 設定，也不得停用簽章。
4. 每次提交後執行 `git verify-commit HEAD` 或 `git log --show-signature -1`。簽章失敗時停止並回報。
5. 先確認 branch 與 upstream，再分別推送：

```powershell
git push origin dev
git push origin gh-pages
```

6. 推送後以 `git ls-remote --heads origin dev gh-pages` 或等效唯讀命令確認遠端 commit。
7. 僅在兩個分支都成功後回報完整完成；若其中一個失敗，明確列出已成功與尚未成功的分支。

## 5. 清理與交付

- 只刪除本流程建立且已驗證路徑位於 repo `.tmp` 或專用 worktree 下的暫存內容。
- 不刪除使用者既有 worktree、未追蹤檔或其他分支變更。
- 交付摘要列出 release/tag、來源 commit、SHA-256、依賴版本、兩邊測試、簽章驗證及遠端 commit。
