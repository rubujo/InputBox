# InputBox — Claude Code 工作區指引

本檔供 **Claude Code** 自動載入。規則以 `AGENTS.md` 與 `docs/engineering/` 為權威來源；本檔將最常用規則內嵌，以減少工具讀取次數。

---

## 快速驗證

```powershell
# PowerShell 編碼前置（執行任何指令前必須先設定）
[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8

# 建置
dotnet build src/InputBox/InputBox.csproj --configuration Debug

# 測試
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj
```

**完成前必做：** 建置零錯誤、零新警告；修改過的 `*.cs` 無新 IDE / CS 診斷。

---

## 安全性紅線（任何任務前必讀）

- **零注入**：禁止修改第三方程式記憶體、封包或電磁紀錄。
- **零模擬**：行為邊界僅止於「複製至剪貼簿」，禁止模擬輸入至其他視窗。
- **零自動化**：禁止實作自動化遊戲行為（自動連點、自動施法等）。
- **零偵測**：禁止主動偵測特定第三方應用程式。
- **GPG 簽章**：所有 Git 提交使用使用者既有 GPG 設定；禁止修改 `gpg.conf`、`gpg-agent.conf`；若簽章失敗回報使用者，**不得**用 `--no-gpg-sign` 繞過。

> 涉及輸入、輸出、剪貼簿、快速鍵或控制器邏輯時，必須依 `docs/engineering/git-commit-safety.md` 第 2 節完成 ToS 合規驗證（抓取並比對官方服務條款）。

---

## 規範索引（依任務展開）

| 任務 | 必讀規範 |
|---|---|
| 任何程式碼異動 | `docs/engineering/environment.md`、`docs/engineering/core-engineering.md` |
| UI、DPI、佈局、視覺回饋 | `docs/engineering/a11y-safety.md` |
| 控制器（XInput / GameInput） | `docs/engineering/gamepad-api.md` |
| 使用者可見文字、`.resx` | `docs/engineering/localization.md` |
| 測試（xUnit v3 / CI） | `docs/engineering/testing.md` |
| Git 工作流、合規驗證 | `docs/engineering/git-commit-safety.md` |

---

## C# 風格規則（高頻違反項）

```csharp
// ✗ 方法禁用運算式主體
public static bool IsEnabled() => _flag;

// ✓ 方法必須完整主體
public static bool IsEnabled()
{
    return _flag;
}

// ✗ var 在明確型別下禁用
var oldFont = Interlocked.Exchange(ref _font, null);
using var dlg = new GamepadMessageBox(...);

// ✓ 明確型別
Font? oldFont = Interlocked.Exchange(ref _font, null);
using GamepadMessageBox dlg = new GamepadMessageBox(...);

// ✓ out var 同樣必須明確
dict.TryGetValue(key, out ButtonVisualState? value);
```

- **Accessor / Property** 可用運算式主體；**方法與建構函式**必須完整主體。
- `var` 僅限 LINQ 查詢結果或匿名型別；其餘一律明確型別。
- 命名：私有欄位 `_camelCase`、常數 `PascalCase`、介面 `I` 前綴。
- 命名空間：file-scoped（`namespace Foo;`）。
- Nullable 全域啟用；API 邊界嚴格標注 `?`。

---

## 執行緒安全關鍵模式

```csharp
// CTS 欄位禁止 readonly
private CancellationTokenSource? _cts = new();

// 原子化處置
Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
Interlocked.Exchange(ref _field, null)?.Dispose();

// Token 安全存取
_cts?.Token ?? CancellationToken.None

// UI 執行緒調度
this.SafeInvoke(() => { ... });
await this.SafeInvokeAsync(() => { ... });

// Lock（C# 13）
private readonly Lock _lock = new();
```

---

## DPI 縮放

```csharp
float scale = DeviceDpi / AppSettings.BaseDpi;
int px = (int)(baseValue * scale);
```

除數/被除數必須包含 `96.0f` 或 `(float)` 強制轉型，杜絕整數截斷。

---

## 字串資源

所有執行階段使用者可見文字必須定義於 `Resources/Strings.resx`（及各語言對應 `.resx`），透過 `Strings.XxxKey` 存取；禁止硬編碼顯示文字。

---

## 專案結構速覽

```
src/InputBox/
  Core/
    Configuration/    AppSettings
    Controls/         GamepadMessageBox、NumericInputDialog 等
    Extensions/       SafeInvoke、SafeFireAndForget 等
    Feedback/         震動、音效
    Input/            控制器事件管理
    Interop/          P/Invoke（User32、Kernel32、Ole32）
    Services/         Logger、Clipboard、HotKey、History
    Utilities/        SystemHelper、InputBoxLayoutManager 等
  Resources/          .resx 多語系（zh-Hant、zh-Hans、ja、ko、de、fr）
  MainForm.cs         核心初始化
  MainForm.Events.cs  事件處理
  MainForm.A11y.cs    無障礙
  MainForm.ContextMenu.cs  右鍵選單
  MainForm.Gamepad.cs 控制器互動
  Program.cs          進入點
tests/InputBox.Tests/
docs/engineering/
```

---

## Git 提交規範

```
格式：<type>(<scope>): <description>
語言：正體中文
分支：dev → PR → main（禁止直接推送 main）
驗證：git verify-commit HEAD
```

常用 type：`feat`、`fix`、`refactor`、`docs`、`test`、`style`、`chore`

---

## 完整規範來源

細節規則以 `AGENTS.md` 與 `docs/engineering/` 為準；本檔與 `AGENTS.md` 有衝突時，以 `AGENTS.md` 為準。
