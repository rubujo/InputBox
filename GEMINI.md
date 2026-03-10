# InputBox 專案開發規範 (Foundational Mandates)

本文件為 **InputBox** 專案的最高指導原則。所有 AI 輔助開發（審查、重構、功能擴充）必須絕對優先遵循以下指令。

---

## 1. 核心工程與架構標準 (Core Engineering)

- **目標框架**：基於 `.NET 10 (net10.0-windows)`。
- **現代 C# 特性**：
  - 優先使用 `PeriodicTimer` (替代 Timer)。
  - 優先使用 C# 13 的 `System.Threading.Lock` 物件 (替代 `Monitor` 或 `lock(object)`)。
  - 使用 `LibraryImport` 進行高效能 P/Invoke。
- **非同步規範**：
  - 嚴格遵守 `async/await`。除事件處理器外，禁止使用 `async void`。
  - 事件處理器內必須包含完備的 `try-catch` 以防程式異常崩潰。
- **UI 執行緒安全**：跨執行緒操作 UI 必須調用 `ControlExtensions.cs` 中的 `SafeInvoke` 系列方法。
- **資源管理**：確保所有實作 `IDisposable` 的物件（如 `CancellationTokenSource`, `Mutex`, `Channel`）在類別釋放時被正確處置，確保無記憶體洩漏。

## 2. A11y 無障礙開發規範 (Accessibility)

- **廣播機制 (Live Region)**：
  - 動態通知**必須**透過 `MainForm.AnnounceA11y` 發送，該機制內部使用 `Channel` 進行排隊。
  - 必須實作基於 `Interlocked` 的「序號檢查 (Sequence ID)」，確保在高頻率廣播下能自動捨棄過時訊息。
  - 廣播前保留 200ms 延遲以避開系統音效干擾與 Audio Ducking。
  - 廣播重複訊息時，結尾應交替附加 `\u200B` (ZWSP) 或 `\u200C` (ZWNJ) 強迫 UIA 識別內容變動。
- **語意與導覽**：
  - 佈局容器必須設定 `AccessibleRole = Grouping` 並補足 `AccessibleName` 與 `Description`。
- **⚠️ 核心限制：設計工具 (Designer) 保護原則**：
  - **嚴禁修改 `MainForm.Designer.cs` 中的 `InitializeComponent` 方法內容。**
  - 所有 A11y 屬性與動態 UI 邏輯必須在分部類別（如 `MainForm.A11y.cs`）中設定。

## 3. 控制器 API 指引 (XInput & GameInput)

- **自動退避 (Fallback)**：預設優先嘗試 `GameInput`；若初始化失敗，必須自動退避至 `XInput` 並告知使用者。
- **震動安全性 (Vibration Safety)**：
  - 必須實作 `VibrationToken` (Interlocked 遞增整數) 機制，解決非同步停止震動時的競態條件 (Race Condition)。
  - 程式崩潰或結束前必須執行 `EmergencyStopAllActiveControllers()` 強制停止所有馬達。
- **效能標準**：輪詢必須使用 `PeriodicTimer` 鎖定在 60 FPS (約 16.6ms)，且必須受 `CancellationToken` 監控。

## 4. 合規性與安全性紅線 (Compliance & Security)

### 外部合規基準 (Compliance Baselines)
**代理人必須定期或在變更核心邏輯前，擷取並分析以下網址的最新內容，確保程式設計不違反其服務條款：**
- **FFXIV 繁體中文版**：[使用者合約](https://www.ffxiv.com.tw/web/user_agreement.html)、[授權條款](https://www.ffxiv.com.tw/web/license.html)
- **宇峻奧汀 (UserJoy)**：[隱私權政策](https://www.userjoy.com/mp/privacy.aspx)、[使用者授權合約 (EULA)](https://www.userjoy.com/mp/eula.aspx)、[免責聲明](https://www.uj.com.tw/uj/service/service_user_disclaimer.aspx)

### 開發行為邊界 (Safety Redlines)
為了符合上述合約精神，嚴禁實作以下功能：
1. **零互動/注入**：禁止互動、修改、注入任何第三方程式之記憶體、封包或電磁紀錄。禁止還原工程。
2. **零模擬/同步**：禁止模擬輸入至其他視窗（行為僅止於「複製至剪貼簿」）；禁止同步操作多組帳號。
3. **零自動化**：嚴禁實作自動化遊戲行為邏輯（如自動連點、自動施法）。
4. **零偵測性**：禁止主動偵測特定第三方應用程式，防範防護軟體 (Anti-Cheat) 誤判。

### 系統整合安全性
- **P/Invoke**：必須套用 `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`。
- **單一執行個體**：Mutex 必須具備 GUID 與 `Local\` 前綴以隔離使用者工作階段。
- **原子操作**：儲存設定檔必須使用原子寫入機制（暫存檔 + `File.Move`）。

## 5. 在地化術語規範 (L10N Standard)

預設語系為 `zh-TW`。**嚴禁使用大陸用語或非標準技術翻譯。**

| 英文術語 | 標準繁體中文翻譯 (台灣) | 禁用詞彙 |
| :--- | :--- | :--- |
| Hotkey | **快速鍵** | 快捷鍵 |
| Clipboard | **剪貼簿** | 剪貼板 |
| History | **歷程記錄** | 歷史紀錄、歷史記錄 |
| Settings | **設定** | 設置 |
| Screen | **螢幕** | 屏幕 |
| Optimization | **最佳化** | 優化 |
| Async / Thread | **非同步 / 執行緒** | 異步 / 線程 |

## 6. 驗證協議與審查清單 (Validation & Audit)

在完成任何任務或提交變更前，代理人必須執行以下 **標準審查工作流**：

1.  **程式碼品質審查**：
    - 檢查是否符合微軟 .NET 開發指導原則（例外處理、非同步安全性、資源釋放）。
    - 修正潛在風險、效能瓶頸或不符合現代 C# 慣例的錯誤。
2.  **A11y 實作校閱**：
    - 確保所有新控制項具備正確的 `AccessibleName/Role/Description`。
    - 驗證廣播機制是否遵循「序號檢查」與「音訊避讓」規範。
3.  **語系術語校對**：
    - 檢查所有支援語系（特別是 `Strings.zh-Hant.resx`）是否符合微軟標準與台灣在地化習慣（詳見第 5 點）。
4.  **建置與修復**：
    - 執行 `dotnet build`。
    - 根據建置結果修復所有錯誤；針對警告（Warnings），除特定豁免外，應盡可能修正（如資源釋放 CA2213）。
5.  **溝通準則**：
    - 所有的總結、報告與回覆必須使用 **正體中文**。
