# InputBox - Agent 工作區總入口

本檔案是 InputBox 的 repo root agent 指引入口，供 Codex CLI、GitHub Copilot CLI、Antigravity CLI 與其他支援 `AGENTS.md` 的工具使用。Claude Code 與其他工具專屬入口檔只保留薄相容層，不維護第二份完整規範。開始任何修改前，應先讀取 `.agents/skills/inputbox-dev/SKILL.md`，再依任務需要載入 `docs/engineering/` 下的對應規範。

## 0. Agent 支援結構

### 0.1 單一權威鏈

本專案採用「共用入口 + 專案技能 + 原子化工程規範」三層結構：

1. `AGENTS.md`：跨工具的共同入口，描述載入順序、安全紅線與任務索引。
2. `.agents/skills/inputbox-dev/SKILL.md`：InputBox 專案唯一權威技能，封裝工程規範、A11y、在地化、測試與 Git 提交要求。
3. `docs/engineering/`：原子化細節規範；任務涉及哪個領域，就讀取對應文件。

工具專屬入口檔只能導向這條權威鏈，不得複製完整規範：

- `CLAUDE.md`：Claude Code 專案記憶入口，使用 `@AGENTS.md` 匯入本檔。
- `GEMINI.md`：Antigravity CLI / Gemini 相容 context 入口，導向本檔與 `inputbox-dev`。
- `.github/copilot-instructions.md`：GitHub Copilot CLI 與 Copilot repository-wide instructions 入口，導向本檔。

### 0.2 官方文件查核（2026-05-24）

本結構依下列官方資料調整：

- Codex CLI：OpenAI Codex 文件指出 Codex 會在工作前讀取 `AGENTS.md`，並由全域到 repo root 再到目前目錄建立 instruction chain。來源：[Custom instructions with AGENTS.md](https://developers.openai.com/codex/guides/agents-md)。
- Claude Code：Claude Code 文件指出 project instructions 可放在 `./CLAUDE.md` 或 `./.claude/CLAUDE.md`，且 `CLAUDE.md` 可用 `@path/to/import` 匯入其他檔案。來源：[How Claude remembers your project](https://code.claude.com/docs/en/memory)。
- GitHub Copilot CLI：GitHub 文件指出 Copilot CLI 支援 `.github/copilot-instructions.md`、`.github/instructions/**/*.instructions.md` 與 `AGENTS.md`；root `AGENTS.md` 會被視為 primary instructions。來源：[Adding custom instructions for GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-custom-instructions)。
- Antigravity CLI：Google Antigravity 遷移文件指出 Antigravity CLI 讀取與 Gemini CLI 相同的 context files，workspace 會讀 `GEMINI.md` 與 `AGENTS.md`，workspace skills 使用 `.agents/skills`；Antigravity Rules 目前預設放在 `.agents/rules`。來源：[Migrating from Gemini CLI](https://antigravity.google/docs/gcli-migration)、[Rules and Workflows](https://antigravity.google/docs/rules-workflows)。

### 0.3 支援矩陣

| 工具 | 主要入口 | 本 repo 策略 |
|---|---|---|
| Codex CLI | `AGENTS.md` | 直接使用本檔，不建立 `CODEX.md` 或另一份 Codex 專屬規範。 |
| Claude Code | `CLAUDE.md` | `CLAUDE.md` 僅用 `@AGENTS.md` 匯入共同規範，並保留少量 Claude 專屬說明。 |
| GitHub Copilot CLI | `.github/copilot-instructions.md` + `AGENTS.md` | 兩者都存在，但 `.github/copilot-instructions.md` 只做導向，避免和本檔衝突。 |
| Antigravity CLI | `AGENTS.md` + `GEMINI.md` + `.agents/skills` | `AGENTS.md` 是共同規範，`GEMINI.md` 是薄相容層，`inputbox-dev` 留在 `.agents/skills`。若日後需要 Antigravity workspace rules，新增於 `.agents/rules`，不要使用舊的 `.agent/rules`。 |

### 0.4 是否需要建立工具專屬 Skill

目前不建議為 Codex CLI、Claude Code、Copilot CLI 或 Antigravity CLI 各自建立重複技能，原因如下：

- 專案已存在可重用的 `inputbox-dev` 技能，內容已涵蓋安全紅線、工程規範、A11y、在地化、測試與 Git 提交要求。
- 多份工具專屬技能會與 `.agents/skills/inputbox-dev/SKILL.md` 形成雙份維護，長期更容易漂移。
- 若未來需要工具專屬能力，應只新增薄包裝或工具設定，實際工程規範仍回到 `inputbox-dev` 與 `docs/engineering/`。

只有在下列情況，才建議新增獨立 skill 或 rules：

- 需要封裝可被多工具重用的固定工作流程，例如審查指令碼、專用驗證命令或跨專案模板。
- 需要將 `inputbox-dev` 拆分為可獨立安裝、可跨 repo 復用的技能模組。
- Antigravity 需要 workspace rule 的啟用模式、glob 或 model-decision 設定；此時應放在 `.agents/rules`，並保持只引用共同規範。

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
