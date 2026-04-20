# InputBox — Claude Code 工作區指引

本檔案為 **Claude Code CLI** 專用。執行任何開發任務時，請依下方指引讀取 `docs/engineering/` 下的原子化規範。

---

## 快速指令

```bash
# 建置（主要驗證）
dotnet build src/InputBox/InputBox.csproj --configuration Debug

# 執行全部測試
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj

# 執行測試並產生覆蓋率報告
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj \
  --collect:"XPlat Code Coverage" \
  --results-directory TestResults
```

> **PowerShell 編碼前置步驟**（執行任何指令前必須先設定）：
> ```powershell
> [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
> ```

---

## 安全性紅線（硬性限制）

在執行**任何**任務前，必須遵守：

- **零互動/注入**：禁止修改第三方程式記憶體、封包或電磁紀錄。
- **零模擬**：行為僅止於「複製至剪貼簿」，不得模擬輸入至其他視窗。
- **零自動化**：禁止實作自動化遊戲行為（自動連點、自動施法等）。
- **GPG 簽章**：所有 Git 提交預設必須使用使用者既有的 GPG 簽章設定。Agent 嚴禁修改 `gpg.conf`、`gpg-agent.conf`；若簽章失敗，請提醒使用者自行處理，**不得** 以 `--no-gpg-sign` 繞過。

詳見 `docs/engineering/git-commit-safety.md`。

---

## 工程規範索引

| 任務類型 | 必讀規範 |
|----------|----------|
| UI 控制項、DPI、佈局 | `docs/engineering/environment.md`、`docs/engineering/core-engineering.md` |
| 非同步、Lock、資源管理 | `docs/engineering/core-engineering.md` |
| 無障礙 (A11y)、WCAG、閃爍安全 | `docs/engineering/a11y-safety.md` |
| 遊戲控制器 (XInput/GameInput) | `docs/engineering/gamepad-api.md` |
| 在地化、字串資源、術語 | `docs/engineering/localization.md` |
| Git 提交、分支、合規 | `docs/engineering/git-commit-safety.md` |
| 測試（xUnit v3、FlaUI） | `docs/engineering/testing.md` |

---

## 專案結構速覽

```
src/InputBox/
├── Core/
│   ├── Configuration/   # AppSettings
│   ├── Controls/        # 自訂控制項（GamepadMessageBox、NumericInputDialog 等）
│   ├── Extensions/      # 擴充方法（SafeInvoke、SafeFireAndForget 等）
│   ├── Feedback/        # 震動、音效回饋
│   ├── Input/           # 遊戲控制器事件管理
│   ├── Interop/         # P/Invoke（User32、Kernel32、Ole32 等）
│   ├── Services/        # 全域服務（Logger、Clipboard、HotKey、History）
│   └── Utilities/       # 工具函式（SystemHelper、DialogLayoutHelper 等）
├── Resources/           # .resx 多語系資源（zh-Hant、zh-Hans、ja、ko、de、fr）
├── MainForm.cs          # 核心初始化與公開介面
├── MainForm.Events.cs   # 事件處理
├── MainForm.A11y.cs     # 無障礙支援
├── MainForm.ContextMenu.cs  # 右鍵選單
├── MainForm.Gamepad.cs  # 控制器互動
└── Program.cs           # 進入點
tests/InputBox.Tests/    # xUnit v3 + FlaUI 冒煙測試
docs/engineering/        # 原子化工程規範（詳見上方索引）
```

---

## 核心規範摘要

### 命名規範
- **私有欄位**：`_camelCase`（例：`_gamepadController`、`_formCts`）
- **常數**：PascalCase（例：`LogFileName`、`MaxFileSize`）
- **介面**：`I` 前綴（例：`IGamepadController`）
- **方法**：PascalCase；`TryXxx` 回傳 `bool`；`HandleXxx` 處理事件；`ApplyXxx` 套用設定
- **`partial class` 分區**：依功能命名後綴（`Events`、`A11y`、`ContextMenu`、`Gamepad`）

### C# 風格
- **命名空間**：使用 file-scoped（`namespace Foo;`）
- **型別宣告**：明確型別優先，非必要不用 `var`
- **運算式主體**：Accessor 與 Property 可用；方法與建構函式使用完整主體
- **可為 null**：全域啟用（`Nullable = enable`），API 邊界嚴格標注 `?`

### 執行緒安全關鍵模式
```csharp
// 原子化處置
Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
Interlocked.Exchange(ref _field, null)?.Dispose();

// CancellationToken 安全存取
_formCts?.Token ?? CancellationToken.None

// UI 執行緒調度
this.SafeInvoke(() => { ... });
await this.SafeInvokeAsync(() => { ... });
```

### DPI 縮放
```csharp
float scale = DeviceDpi / AppSettings.BaseDpi;
int px = (int)(baseValue * scale);
```

### 字串資源
**所有使用者可見文字**必須定義於 `Resources/Strings.resx`（及各語言對應 `.resx`），透過 `Strings.XxxKey` 存取；禁止硬編碼顯示文字。

---

## Git 提交規範

- 格式：`<type>(<scope>): <description>`（Conventional Commits v1.0.0）
- 語言：訊息預設使用**正體中文**
- 分支：`dev` → PR → `main`（禁止直接推送 main）
- 每輪開發前：`dev` 必須先同步 `main`
- 提交後驗證：`git verify-commit HEAD`

**常用 type**：`feat`、`fix`、`refactor`、`docs`、`test`、`chore`

---

## 修改後必做清單

1. `dotnet build src/InputBox/InputBox.csproj --configuration Debug` — 確認零錯誤、零新警告
2. 確認 `.editorconfig` 格式（UTF-8 with BOM、CRLF、4 空格縮排）
3. 新增或修改 UI 文字 → 更新所有 `.resx` 語言檔
4. 新增公開 API → 確認 nullable 標注完整
5. 任何涉及輸入/輸出/控制器邏輯 → 參照 `docs/engineering/git-commit-safety.md` 第 2 節完成 ToS 合規驗證
