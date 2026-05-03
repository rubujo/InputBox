# InputBox - OpenAI Codex 工作區指引

本檔案供 OpenAI Codex、GitHub Copilot Agent、VS Code GitHub Copilot Chat 與其他支援 `AGENTS.md` 的工具使用。開始任何修改前，應先讀取 `.agents/skills/inputbox-dev/SKILL.md`，再依任務需要載入 `docs/engineering/` 下的對應規範。

## 0. 是否需要建立 Codex 專屬 Skill

目前 **不建議另建一份 Codex 專屬 Skill**，原因如下：

- 專案已存在可重用的 `inputbox-dev` 技能，內容已涵蓋安全紅線、工程規範、A11y、在地化、測試與 Git 提交要求。
- 若再建立一份僅服務 Codex 的 Skill，會與既有技能形成雙份維護，長期更容易漂移。
- 對 Codex 而言，較合理的做法是：
  - 以本檔作為 repo 根目錄入口說明。
  - 以 `inputbox-dev` 作為唯一權威技能。
  - 以 `docs/engineering/` 作為原子化規範來源。

只有在下列情況，才建議另外建立新 Skill：

- 需要封裝 **Codex 專屬工作流程**，例如固定的審查指令碼、專用驗證命令、或跨專案共用的 Codex 操作模板。
- 需要把 `inputbox-dev` 拆分為可獨立安裝、可跨 repo 復用的技能模組。

## 0.1 多工具檔案策略

本專案採用下列入口分工：

- `AGENTS.md`：共用的主要 agent 指引入口，供 Codex、Copilot Agent、VS Code GitHub Copilot Chat 與其他支援 `AGENTS.md` 的工具使用。
- `CLAUDE.md`：Claude Code 相容入口，內容應導向本檔，不應維護第二套完整規範。
- `GEMINI.md`：Gemini CLI 相容入口，內容應導向本檔，不應維護第二套完整規範。
- `.github/copilot-instructions.md`：提供 Visual Studio GitHub Copilot Chat 與其他仍依賴 Copilot repository instructions 的客戶端使用。

若多個入口檔同時存在，應避免互相矛盾；細節規範始終以 `.agents/skills/inputbox-dev/SKILL.md` 與 `docs/engineering/` 為準。

## 1. 必讀規範索引

至少依任務類型讀取下列文件：

| 任務類型 | 必讀文件 |
|---|---|
| 任何程式碼異動 | `docs/engineering/environment.md`、`docs/engineering/core-engineering.md` |
| Steam Deck、Wine、Proton、Gamescope、支援平台或 UI 技術方向 | `docs/engineering/environment.md` |
| UI、WinForms、DPI、版面、視覺回饋、螢幕報讀 | `docs/engineering/a11y-safety.md` |
| 控制器輸入、XInput、GameInput、按鍵映射 | `docs/engineering/gamepad-api.md` |
| 使用者可見文字、`.resx`、術語、助記鍵 | `docs/engineering/localization.md` |
| 測試、xUnit v3、冒煙測試、覆蓋率 | `docs/engineering/testing.md` |
| Git 工作流、安全紅線、合規 | `docs/engineering/git-commit-safety.md` |

## 2. 安全性紅線

以下限制為硬性要求：

- 禁止記憶體注入、封包攔截、電磁紀錄、或修改第三方程式行為。
- 禁止模擬輸入到其他視窗；輸出邊界僅限於「複製到剪貼簿」。
- 禁止實作任何自動化遊戲行為，例如自動連點、自動施法、輪播或掛機控制。
- 禁止主動偵測特定第三方應用程式。
- 所有 Git 提交預設必須使用使用者既有的 GPG 簽章設定；不得修改 `gpg.conf`、`gpg-agent.conf`、`common.conf` 或其他相關設定。若簽章失敗，應停止並回報使用者。

詳細內容以 `docs/engineering/git-commit-safety.md` 為準。

## 3. ToS 與合規檢查

若任務涉及下列核心行為：

- 輸入流程
- 剪貼簿流程
- 快速鍵
- 控制器映射
- 輸出或返回前景視窗邏輯

則 Codex 在實作前或最遲於交付前，必須依 `docs/engineering/git-commit-safety.md` 的要求，抓取並檢視列出的第三方服務條款，確認異動仍符合「非自動化、非模擬、非注入」原則，並在交付摘要中簡述合規結論。

## 4. 環境與編碼

- 預設作業系統：Windows
- 預設 Shell：PowerShell
- 執行 PowerShell 指令前先設定 UTF-8：

```powershell
[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
```

- 所有異動必須遵循 repo 根目錄 `.editorconfig`。
- 新增或修改 `*.cs`、`*.resx` 時，必須使用 UTF-8 with BOM 與 CRLF。
- 其他文字檔使用 UTF-8 與 CRLF。

## 5. 核心工程規則

- 目標框架：`.NET 10 (net10.0-windows)`
- 命名空間使用 file-scoped namespace。
- 私有欄位採 `_camelCase`。
- 若 `CancellationTokenSource` 欄位需要原子替換或釋放，禁止宣告為 `readonly`。
- 釋放資源時必須遵循原子化處置模式：

```csharp
Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
Interlocked.Exchange(ref _resource, null)?.Dispose();
```

- 讀取 `CancellationToken` 時使用安全模式：

```csharp
_cts?.Token ?? CancellationToken.None
```

- 涉及 UI 執行緒調度時，優先沿用既有 `SafeInvoke`、`SafeInvokeAsync` 或 `InvokeAsync` 模式。
- 涉及 DPI、版面與最小尺寸時，必須遵循 `docs/engineering/core-engineering.md` 的 `UpdateLayoutConstraints`、`UpdateMinimumSize` 與智慧定位規範。
- 修改 `*.cs` 後，完成前必須清除新增的 IDE 與 CS 診斷。

## 6. A11y 與視覺安全

只要變更 UI、公告廣播、焦點回饋、驗證提示或動畫效果，必須先閱讀 `docs/engineering/a11y-safety.md`。

關鍵要求：

- 不得引入超出規範的閃爍或高刺激視覺效果。
- 不得破壞 live region 與螢幕閱讀器行為。
- `AnnouncerLabel.Clear()` 必須使用 ZWSP，不可改成 NBSP。
- 焦點、按壓、懸停、驗證狀態不得造成版面抖動。
- 必須維持深色、淺色、高對比與色覺友善條件下的對比與色彩規則。

## 7. 在地化規則

- 所有執行階段使用者可見文字，都必須定義在 `src/InputBox/Resources/Strings.resx` 與對應語系 `.resx`。
- 禁止在執行階段程式碼中硬式編碼顯示文字。
- `{0}` 等預留位置不可翻譯或改序，除非語序調整本身有明確需求。
- 助記鍵需維持跨語系一致性，並避免重複後綴。
- 禁止手動修改 `MainForm.Designer.cs` 的自動生成版面配置結構。

## 8. 測試要求

- 測試框架：xUnit v3
- 測試專案：`tests/InputBox.Tests/InputBox.Tests.csproj`
- 常用驗證指令：

```powershell
dotnet build src/InputBox/InputBox.csproj --configuration Debug
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj
```

- 若測試會讀寫 `%AppData%` 相關資料，必須依 `docs/engineering/testing.md` 採用檔案系統隔離模式。
- 新增測試時，方法命名必須遵循 `Method_Condition_ExpectedResult`。
- 每個 `[Fact]` 方法都應有繁體中文 XML `summary`。
- 若新增、刪除或明顯調整測試，必須同步更新 `tests/InputBox.Tests/README.md`。

## 9. 專案結構速覽

```text
src/InputBox/
  Core/
    Configuration/
    Controls/
    Extensions/
    Feedback/
    Input/
    Interop/
    Services/
    Utilities/
  Resources/
  MainForm.cs
  MainForm.Events.cs
  MainForm.A11y.cs
  MainForm.ContextMenu.cs
  MainForm.Gamepad.cs
  Program.cs
tests/InputBox.Tests/
docs/engineering/
```

## 10. Git 工作流

- 提交格式遵循 Conventional Commits：`<type>(<scope>): <description>`
- 提交訊息預設使用正體中文。
- 應以 `dev` 為工作分支，透過 PR 合併回 `main`；不得直接推送應用程式變更到 `main`。
- 新一輪開發前，`dev` 應先對齊最新 `main`。
- 提交後應以 `git log --show-signature -1` 或 `git verify-commit HEAD` 驗證簽章。

## 11. 完成前檢查清單

交付前至少確認：

1. `dotnet build src/InputBox/InputBox.csproj --configuration Debug`
2. 若異動可測，執行 `dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj`
3. 修改過的 `*.cs` 沒有新增 IDE 或 CS 診斷
4. 若有使用者可見文字異動，已同步更新對應 `.resx`
5. 若有輸入、輸出、剪貼簿、快速鍵或控制器邏輯異動，已重新檢查 `docs/engineering/git-commit-safety.md`
