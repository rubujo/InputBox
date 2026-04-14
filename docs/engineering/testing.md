# 測試規範 (Testing)

## 1. 框架與專案結構

- **框架**：[xUnit v3](https://xunit.net/)（選用理由：每個 `[Fact]` 獨立實例化、`IDisposable` 自動呼叫、語法精簡）。
- **測試專案路徑**：`tests/InputBox.Tests/InputBox.Tests.csproj`
- **目標框架**：`net10.0-windows`（與主專案一致，僅支援 Windows）。
- **InternalsVisibleTo**：主專案透過 `<AssemblyAttribute>` 授予測試專案存取 `internal` 類別的權限，無需建立手動的 `AssemblyInfo.cs`。

## 2. 執行測試

```powershell
# 從 repo 根目錄執行
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj

# 加入詳細輸出
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj --logger "console;verbosity=detailed"

# 收集 Code Coverage（MTP 原生模式，同 CI）
dotnet test tests/InputBox.Tests/InputBox.Tests.csproj -c Release --no-build `
    --coverage `
    --coverage-output-format cobertura `
    --coverage-output coverage.cobertura.xml
```

> **前置條件**：`global.json` 必須設定 `"test": { "runner": "Microsoft.Testing.Platform" }`，Coverage 套件使用 `Microsoft.Testing.Extensions.CodeCoverage`（非 coverlet.collector，兩者與 xUnit v3 MTP 不相容）。

## 3. 檔案系統隔離模式（Filesystem Isolation）

凡是被測目標的方法會**讀寫使用者資料目錄**（如 `%AppData%\InputBox\*.json`），測試類別必須實作 `IDisposable` 的備份/還原模式：

```csharp
public sealed class ExampleServiceTests : IDisposable
{
    private static readonly string DataPath = Path.Combine(
        AppSettings.ConfigDirectory, "data.json");

    private static readonly string BackupPath = DataPath + ".testbackup";

    public ExampleServiceTests()
    {
        Directory.CreateDirectory(AppSettings.ConfigDirectory);
        if (File.Exists(DataPath))
            File.Copy(DataPath, BackupPath, overwrite: true);
    }

    public void Dispose()
    {
        if (File.Exists(BackupPath))
            File.Move(BackupPath, DataPath, overwrite: true);
        else if (File.Exists(DataPath))
            File.Delete(DataPath);
    }
}
```

**規則**：
- 備份路徑統一為 `原路徑 + ".testbackup"`。
- 建構子負責備份；`Dispose()` 負責還原或清除。
- xUnit v3 在每個 `[Fact]` 後自動呼叫 `Dispose()`，各測試之間完全隔離。

## 4. 測試命名規範

採用 `Method_Condition_ExpectedResult` 格式：

```
Add_EmptyName_ReturnsFalse
ResolveDirection_Int_WasPositive_StaysPositiveAboveExitThreshold
Validate_InitialDelayFramesZero_Throws
```

每個 `[Fact]` 方法**必須**加上繁體中文 `/// <summary>` XML 文件，說明該測試驗證的行為意圖，而非僅重述方法名稱。

## 5. xUnit Analyzer 規則

以下 analyzer 規則在 CI 中視為錯誤，必須遵守：

| 規則 | 說明 | 正確做法 |
|---|---|---|
| **xUnit1051** | 非同步測試不得使用 `CancellationToken.None` | 改用 `TestContext.Current.CancellationToken` |
| **xUnit1031** | 不得在測試中使用阻塞式 Task 操作（`.Result`、`.Wait()`、`.GetAwaiter().GetResult()`） | 改用 `await` |

```csharp
// ✗ 違規
await Task.Delay(100, CancellationToken.None);

// ✓ 正確
await Task.Delay(100, TestContext.Current.CancellationToken);
```

## 6. 測試文件同步規範

每次**新增、刪除或明顯調整測試案例**時，必須在同一個變更中同步更新 `tests/InputBox.Tests/README.md`，至少包含：

1. 測試類別／被測目標說明
2. 測試數量或總數變化
3. 若新增回歸測試，需註明對應的缺陷或風險類型

此規則是專案維護要求，避免 README 與實際測試覆蓋範圍脫節。

## 7. CI 整合

測試在 GitHub Actions 的 `ci.yml` 中自動執行，流程如下：

1. 建置主專案（`src/InputBox`）
2. 建置測試專案（`tests/InputBox.Tests`）
3. 執行測試並收集覆蓋率（`dotnet test -c Release --no-build --coverage ...`）
4. 使用 ReportGenerator 產生 HTML / Cobertura 報告並上傳 Artifact

每次 push 到任何分支，以及 PR 至 `main`，均會觸發。Runner 為 `windows-latest`。
