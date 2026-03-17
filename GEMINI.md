# 輸入框（InputBox）專案開發規範

本文件為 **輸入框（InputBox）** 專案的最高指導原則。所有 AI 輔助開發（審查、重構、功能擴充）必須絕對優先遵循以下指令。

---

## 1. 核心工程與架構標準

- **目標框架**：基於 `.NET 10（net10.0-windows）`。
- **現代 C# 特性**：
  - 優先使用 `PeriodicTimer`（替代 Timer）。
  - 優先使用 C# 13 的 `System.Threading.Lock` 物件（替代 `Monitor` 或 `lock(object)`）。
  - 使用 `LibraryImport` 進行高效能 P/Invoke。
- **非同步規範**：
  - 嚴格遵守 `async/await`。除事件處理器外，禁止使用 `async void`。
  - 事件處理器內必須包含完備的 `try-catch` 以防程式異常崩潰。
- **UI 執行緒安全**：跨執行緒操作 UI 必須調用 `ControlExtensions.cs` 中的 `SafeInvoke` 系列方法。
- **資源管理**：確保所有實作 `IDisposable` 的物件（如 `CancellationTokenSource`、`Mutex`、`Channel`）在類別釋放時被正確處置，確保無記憶體洩漏。
- **DPI 適應性規範**：
  - 視窗必須實作 `UpdateMinimumSize` 邏輯，並在 `OnHandleCreated` 與 `OnDpiChanged` 中調用，確保在高 DPI 或縮放變更時佈局不崩潰。
  - 計算佈局尺寸時，必須基於 `DeviceDpi` 與 96.0f 的比例進行縮放（Scale）。
- **自定義對話框控制器標準**：
  - 所有自定義對話框（如 `NumericInputDialog`）必須具備 `GamepadController` 屬性。
  - 必須實作手把按鍵映射：`A/Start` 為確認、`B/Back` 為取消、`X` 為重設。
  - 視窗字型必須繼承或共享主視窗的 A11y 字型設定（預設為 11f 放大字型），確保視覺一致性與易讀性。

## 2. A11y 無障礙與視覺安全規範

- **廣播機制**：
  - 動態通知**必須**透過 `MainForm.AnnounceA11y` 發送，該機制內部使用 `Channel` 進行排隊。
  - 必須實作基於 `Interlocked` 的「序號檢查」，確保在高頻率廣播下能自動捨棄過時訊息。
  - 廣播前保留 200ms 延遲以避開系統音效干擾與 Audio Ducking。
  - **重複訊息處理**：廣播重複訊息時，結尾應交替附加 `\u200B`（ZWSP）或 `\u200C`（ZWNJ）強迫 UIA 識別內容變動，確保連續觸發時螢幕閱讀器能正確重讀。
- **分離式回饋原則（Separated Feedback）**：
  - **鍵盤焦點**：當控制項透過鍵盤（如 Tab 鍵）獲得焦點時，僅執行「強烈靜態視覺回饋」（如明暗反轉、字體加粗），禁止啟動耗時的填滿動畫。
  - **注視／懸停**：當視線進入或滑鼠懸停時，啟動「線性填滿（Linear Fill）」動畫，提供明確的動作預期感。
- **語意與導覽**：
  - 佈局容器必須設定 `AccessibleRole = Grouping` 並補足 `AccessibleName` 與 `Description`。
- **色覺與視覺安全規範**：
  - **眼動儀友善（Eye Tracker Optimized）**：
    - **注視回饋（Dwell Feedback）**：必須實作 1000ms 的**線性填滿**進度條。填滿後應保持靜態高亮，禁止持續律動以減少使用者分心。
    - **失焦重置**：當視線離開控制項時，必須立即重置進度條並終止背景任務。
    - **非同步中止機制**：所有 UI 動畫任務必須實作基於 `Interlocked` 原子序號（animationId）的檢查機制，確保當焦點快速切換時，舊任務能立即中止。
    - **抗抖動寬度鎖定（Anti-Jitter Lock）**：為防止字體加粗引發佈局抖動導致眼動儀「追逐目標」，必須在初始化時預先計算 **Bold** 狀態的最大寬度並鎖定為 `MinimumSize`。
    - **對齊規範**：多控制項互動區（如數值對話框）優先採用 3 欄位網格佈局，達成垂直貫穿的對齊感。
  - **色覺友善（CVD）**：UI 狀態變更禁止僅依賴顏色。
    - **形狀補償**：必須結合形狀變化（如 Padding 加粗、邊框脈衝、字型加粗、心跳變幻）。
    - **全色盲支援（Achromatopsia）**：關鍵狀態變更（如獲得焦點、警示）必須包含「亮度對比」與「明暗反轉」（如背景變黑／文字變白），確保在無色彩模式下具備高辨識度。
    - **色盲友善警示色**：在一般主題下，優先選用暖橘色（如 DarkOrange）作為警示色，以獲得跨 CVD 類型（Protan／Deutan／Tritan）的最佳對比。
  - **光敏性癲癇防護（Photosensitive Epilepsy）**：
    - **頻率控制**：視覺律動頻率必須嚴格鎖定在 **1Hz**（1000ms 週期），遠低於 3Hz 的風險閾值。
    - **平滑漸變**：亮度變化必須使用正弦波（Sine Wave）或平滑過渡，禁止劇烈閃爍。
    - **系統動畫服從性**：必須主動感測 `SystemInformation.UIEffectsEnabled`。若動畫被關閉，則禁止執行循環閃爍或 Dwell 動畫，必須改為「靜態顯著提醒」。
  - **高對比支援（High Contrast）**：變更顏色前必須檢查 `SystemInformation.HighContrast`。若開啟，則禁止使用自訂染色（如黑色背景），必須採用系統預設的高亮顏色（如 `SystemColors.Highlight`）。
- **系統偏好同步（System Sync）**：
  - 必須透過 `SystemEvents.UserPreferenceChanged` 監控 `UserPreferenceCategory.General` 與 `Accessibility`，確保當使用者變更 Windows 動畫、色彩或主題設定時，UI 視覺行為能立即同步。
- **⚠️ 核心限制：設計工具（Designer）保護與硬編碼原則**：
  - **設計時視覺化**：為了在 Visual Studio 設計工具內能直觀預覽文字與佈局（因 L10N 為非標準實作），允許且建議在 Designer 中硬編碼正體中文文字與 A11y 屬性。
  - **屬性疊加**：Designer 內設定的值為基礎，執行階段（Runtime）必須透過 `ApplyLocalization` 或分部類別（如 `MainForm.A11y.cs`）再次賦值以確保多語系正確性。
  - **禁止破壞結構**：嚴禁手動修改 `MainForm.Designer.cs` 中由設計工具生成的自動化佈局結構。

## 3. 控制器 API 指引（XInput＆GameInput）

- **自動退避**：預設優先嘗試 `GameInput`；若初始化失敗，必須自動退避至 `XInput` 並告知使用者。
- **震動安全性**：
  - 必須實作 `VibrationToken`（Interlocked 遞增整數）機制，解決非同步停止震動時的競態條件。
  - 程式崩潰或結束前必須執行 `EmergencyStopAllActiveControllers()` 強制停止所有馬達。
- **效能標準**：輪詢必須使用 `PeriodicTimer` 鎖定在 60 FPS（約 16.6ms），且必須受 `CancellationToken` 監控。

## 4. 合規性與安全性紅線

### 外部合規基準

**代理人必須定期或在變更核心邏輯前，擷取並分析以下網址的最新內容，確保程式設計不違反其服務條款：**
- **FFXIV 繁體中文版**：[使用者合約](https://www.ffxiv.com.tw/web/user_agreement.html)、[授權條款](https://www.ffxiv.com.tw/web/license.html)
- **宇峻奧汀（UserJoy）**：[隱私權政策](https://www.userjoy.com/mp/privacy.aspx)、[使用者授權合約（EULA）](https://www.userjoy.com/mp/eula.aspx)、[免責聲明](https://www.uj.com.tw/uj/service/service_user_disclaimer.aspx)

### 開發行為邊界

為了符合上述合約精神，嚴禁實作以下功能：
1. **零互動／注入**：禁止互動、修改、注入任何第三方應用程式之記憶體、封包或電磁紀錄。禁止還原工程。
2. **零模擬／同步**：禁止模擬輸入至其他視窗（行為僅止於「複製至剪貼簿」）；禁止同步操作多組帳號。
3. **零自動化**：嚴禁實作自動化遊戲行為邏輯（如自動連點、自動施法）。
4. **零偵測性**：禁止主動偵測特定第三方應用程式，防範防護軟體（Anti-Cheat）誤判。

### 系統整合安全性

- **P/Invoke**：必須套用 `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`。
- **單一執行個體**：Mutex 必須具備 GUID 與 `Local\` 前綴以隔離使用者工作階段。
- **原子操作**：儲存設定檔必須使用原子寫入機制（暫存檔 + `File.Move()`）。

## 5. 在地化術語規範

預設語系為 `zh-Hant`。**嚴禁在正體中文環境使用大陸地區用語或非標準技術翻譯。**

### 5.1 正體中文（zh-Hant）術語表

| 英文術語 | 標準繁體中文翻譯（臺灣） | 禁用詞彙 |
| :--- | :--- | :--- |
| Hotkey | **快速鍵** | 快捷鍵 |
| Clipboard | **剪貼簿** | 剪貼板 |
| History | **歷程記錄** | 歷史紀錄、歷史記錄 |
| Settings | **設定** | 設置 |
| Screen | **螢幕** | 屏幕 |
| Optimization | **最佳化** | 優化 |
| Async / Thread | **非同步／執行緒** | 異步／線程 |

### 5.2 簡體中文（zh-Hans）術語表

應遵循大陸地區技術慣例，避免單純的「繁轉簡」。

| 英文術語 | 標準簡體中文翻譯 | 禁用詞彙 |
| :--- | :--- | :--- |
| Hotkey | **快捷键** | 快速键 |
| Clipboard | **剪贴板** | 剪贴簿 |
| History | **历史记录** | 历程记录 |
| Settings | **设置** | 设定 |
| Optimization | **优化** | 最佳化 |
| Async / Thread | **异步／线程** | 非同步／执行绪 |

### 5.3 日文（ja）術語表

應優先使用外來語，並遵循遊戲介面慣例。

| 英文術語 | 標準日文翻譯 | 備註 |
| :--- | :--- | :--- |
| Hotkey | **ホットキー** | |
| Clipboard | **クリップボード** | |
| History | **履歴** | |
| Settings | **設定** | |
| Privacy Mode | **プライバシーモード** | |
| Vibration | **振動** | |

### 5.4 英文（en）術語表

應遵循美式英文（en-US）技術慣例。

| 英文術語 | 標準美式英文翻譯 | 禁用／非標準詞彙 |
| :--- | :--- | :--- |
| Hotkey | **Hotkey** | Shortcut key |
| Clipboard | **Clipboard** | Pasteboard |
| History | **History** | Log |
| Settings | **Settings** | Options |
| Optimization | **Optimization** | Optimisation |
| Privacy Mode | **Privacy Mode** | Private Mode |

### 5.5 翻譯安全性與一致性協議

1.  **UI 空間適應**：日文與西歐語系長度通常較長，所有動態佈局（如 `NumericInputDialog`）必須確保 `AutoSize` 與 `MaximumSize` 同時開啟，以觸發自動換行。
2.  **變數占位符**：嚴禁翻譯 `Strings.resx` 中的占位符（如 `{0}`），且必須確保不同語系中的占位符順序符合該語系語法（如「第 {0} 頁」對應「{0} ページ目」）。
3.  **助記鍵（Mnemonics）**：所有語系的按鈕助記鍵字母必須儘可能保持一致（如確認為 `(A)`、取消為 `(B)`），以維持與手把按鍵映射的連動直覺。
4.  **資源註釋（Resource Comments）**：在 `.resx` 檔案中新增任何資源時，**必須**同時填寫 `<comment>` 標籤，且註釋內容必須使用與該資源**相同的語系**，以提供精確的開發與翻譯語境（Context）。

## 6. Git 提交規範

為了確保版本紀錄的可讀性與自動化相容性，所有提交必須遵循以下標準。

- **格式標準**：必須採用 [慣例式提交（Conventional Commits）v1.0.0](https://www.conventionalcommits.org/zh-hant/v1.0.0/) 規範。
  - 格式範例：`<type>(<scope>): <description>`。
  - 常見類型：`feat`（新功能）, `fix`（修補）, `docs`（文件）, `style`（格式）, `refactor`（重構）, `perf`（效能）, `test`（測試）, `chore`（例行事務）。
- **語系要求**：所有 Git 提交訊息（Commit Message）**必須使用正體中文**，除非使用者明確指定使用其他語言。

## 7. 驗證協議與審查清單

在完成任何任務或提交變更前，代理人必須執行以下**標準審查工作流**：

1.  **程式碼品質審查**：
    - 檢查是否符合微軟 .NET 開發指導原則（例外處理、非同步安全性、資源釋放）。
    - 修正潛在風險、效能瓶頸或不符合現代 C# 慣例的錯誤。
2.  **A11y 與視覺安全校閱**：
    - 確保所有新控制項具備正確的 `AccessibleName／Role／Description`。
    - 驗證狀態回饋是否包含「非顏色相關」的視覺提示與「光敏安全」檢查。
3.  **語系術語校對**：
    - 檢查所有支援語系（特別是 `Strings.zh-Hant.resx`）是否符合微軟標準與臺灣在地化習慣。
4.  **建置與修復**：
    - 執行 `dotnet build`。
    - 根據建置結果修復所有錯誤；針對警告，除特定豁免外，應盡可能修正。
5.  **溝通準則**：
    - 所有的總結、報告與回覆必須使用**正體中文**。
