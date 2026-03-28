# 輸入框（InputBox）專案開發規範

本文件為 **輸入框（InputBox）** 專案的最高指導原則。所有 AI 輔助開發（審查、重構、功能擴充）必須絕對優先遵循以下指令。

---

## 0. 開發環境規範

- **預設作業系統**：Microsoft Windows。
- **編碼規範**：執行指令時必須確保環境使用 **UTF-8（Code Page 65001）**，避免使用舊有的 CP950（Big5），以確保非 ASCII 字元顯示正確。
  - **PowerShell（pwsh／powershell）**：在執行任何命令前，必須先執行：
    ```powershell
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    [Console]::InputEncoding = [System.Text.Encoding]::UTF8
    ```
  - **Command Prompt（cmd）**：在執行任何命令前，必須先執行 `chcp 65001`。
- **Shell 使用優先順序**：
  1. **PowerShell 7+（pwsh）**：優先使用跨平台版本。
  2. **Windows PowerShell 5.1（powershell）**：僅在無 pwsh 時使用。
  3. **Command Prompt（cmd）**。
- **指令相容性**：
  - 執行 `run_shell_command` 時，必須優先使用與上述環境相容的內建指令（例如優先使用 `dir` 或 `Get-ChildItem` 而非 `ls`，除非在 PowerShell 環境下）。
  - 當需要調用任何 CLI 工具或命令（如 `git`、`dotnet` 等）時，必須優先使用預設開發環境所支援且已驗證的指令版本。

---

## 1. 核心工程與架構標準

- **目標框架**：基於 `.NET 10（net10.0-windows）`。
- **現代 C# 特性**：
  - 優先使用 `PeriodicTimer`（替代 Timer）。
  - **執行緒鎖定嚴格規範**：必須使用 C# 13 的 `System.Threading.Lock` 物件。
    - 嚴禁直接鎖定集合本身（如 `lock(_dictionary)`），必須獨立宣告（如 `private readonly Lock _cacheLock = new();`）並鎖定該專用物件，杜絕舊版 Monitor 降級與潛在死結風險。
  - 使用 `LibraryImport` 進行高效能 P/Invoke。
- **非同步規範**：
  - 嚴格遵守 `async/await`。除事件處理器外，禁止使用 `async void`。
  - 事件處理器內必須包含完備的 `try-catch` 以防程式異常崩潰。
  - **UI 執行緒安全（UI Thread Safety）**：跨執行緒操作 UI 必須調用 `ControlExtensions.cs` 中的 `SafeInvoke` 或 `SafeBeginInvoke` 系列方法。若是背景執行緒涉及單一執行緒單元（STA）的 COM 存取（如剪貼簿），必須實作嚴謹的 `InvokeRequired` 與 `try-catch` 檢查以防視窗關閉時引發崩潰。
    - **非同步調度準則**：在 `async` 任務內，**必須優先使用** .NET 10 原生的 `await control.InvokeAsync(...)` 方法以避免阻塞背景執行緒。
      - **`InvokeAsync(Action)`**：用於同步的 UI 屬性賦值或簡單方法調用。
      - **`InvokeAsync(Func<Task>)`**：用於包含 `await` 的一連串非同步 UI 邏輯。
  - **權杖安全（Token Safety）**：存取 `CancellationTokenSource?` 欄位的 `.Token` 時，必須使用 `?.Token ?? CancellationToken.None` 模式，杜絕併發下的 `NullReferenceException`。
- **資源管理**：確保所有實作 `IDisposable` 的物件（如 `CancellationTokenSource`、`IGamepadController`、動態快取字型）在類別釋放（特別是 `OnFormClosing`）時被原子化處置並歸零，杜絕 GDI Handle 洩漏與多執行緒競態。
  - **原子化處置模式（Atomic Dispose Pattern）**：必須使用 `Interlocked.Exchange(ref _resource, null)?.Dispose()` 模式，確保「先交換歸零、後執行處置」，防止多執行緒下的重複釋放或空引用異常。
  - **權杖處置絕對紅線**：終止非同步任務時，嚴禁單獨呼叫 `?.Cancel()` 然後遺棄。必須使用擴充方法 `Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();` 以徹底清理底層控制代碼。
  - **共享資源歸零規範（Shared Resource Nullification）**：對於從全域快取池（如 `GetSharedA11yFont`）取得或由多個組件共享且「禁止由個別視窗手動處置」的資源，在類別釋放（`Dispose` 或 `OnFormClosing`）時，必須將其欄位設為 `null`（建議使用 `Interlocked.Exchange(ref _field, null)`）。這能防止已釋放的視窗繼續持有快取資源的引用，最佳化 GC 回收效率，並杜絕因誤用已失效引用而產生的邏輯錯誤。
  - **字體快取池（Font Pool）**：`A11yFont` 必須透過基於 DPI 的快取池（`MainForm.GetSharedA11yFont`）取得，禁止在未受管理下直接建立 `Font` 實例。快取池支援自定義倍率（Multiplier）參數以適應特殊 UI 組件。
  - **資源回收桶（Trash Can）與共享安全**：
    - **非共享實例**：更換視窗私有的字體實例（非來自快取池者）時，舊字體必須進入 `AddFontToTrashCan` 回收桶並延遲釋放。
    - **共享實例（安全紅線）**：來自全域快取池（`GetSharedA11yFont`）的字體實例**絕對禁止**由個別視窗手動處置或放入回收桶。處置共享字體會導致其他視窗引發 `ObjectDisposedException`。快取池生命週期由 `Program.cs` 統一管控。
  - **全域處置**：程式結束前（`Program.cs`）必須調用 `DisposeCaches()` 徹底清理快取池。
- **DPI 適應性規範**：
  - 視窗必須實作**佈局約束與最小尺寸更新邏輯**（如 `UpdateLayoutConstraints` 或 `UpdateMinimumSize`），並在 `OnHandleCreated` 與 `OnDpiChanged` 中調用，確保在高 DPI 或縮放變更時佈局不崩潰、文字不被裁剪。
  - **計算一致性**：計算佈局尺寸與縮放比例時，必須確保除數或被除數包含顯式的浮點數標記（如 `96.0f`）或進行 `(float)` 強制轉型，杜絕因整數除法截斷導致的高 DPI 佈局計算誤差。
  - **智慧重定位（Smart Positioning）**：視窗在 DPI 變更、語系切換引發佈局擴張、或初始顯示時，必須執行螢幕邊界檢查（如 `ApplySmartPosition`），確保視窗內容不超出目前顯示器的可視區域（Working Area）。
- **自定義對話框控制器標準**：
  - 所有自定義對話框（如 `NumericInputDialog`）必須具備 `GamepadController` 屬性。
  - 必須實作控制器按鍵映射：`A／Start` 為確認、`B／Back` 為取消、`X` 為重設。
  - 視窗字型必須繼承或共享主視窗的 A11y 字型設定（基準為 **14f** 放大字型），確保視覺一致性與易讀性。
  - 核心輸入框（`TBInput`）維持 **28f** 基準大小，作為視覺層級頂點。

## 2. A11y 無障礙與視覺安全規範

- **廣播機制**：
  - 動態通知**優先**透過 `MainForm.AnnounceA11y` 發送，該機制內部使用 `Channel` 進行排隊。
  - **標準化路徑**：開發者**禁止**繞過標準廣播器直接調用 UIA API，以確保 ZWSP／ZWNJ 交替補償與 Audio Ducking 延遲始終生效。
  - **本地備援機制**：自定義對話框在無法訪問主廣播器時，允許實作基於 `Interlocked` 序號檢查的「防抖（Debounce）」廣播，以確保即時性。
  - **避讓延遲規範**：廣播前保留 200ms 延遲以避開系統音效干擾與 Audio Ducking。**此延遲必須在非 UI 執行緒執行**，禁止阻塞 UI 訊息迴圈以維持介面流暢度。
  - **重複訊息處理**：廣播重複訊息時，結尾應交替附加 `\u200B`（ZWSP）或 `\u200C`（ZWNJ）強迫 UIA 識別內容變動，確保連續觸發時螢幕閱讀器能正確重讀。
  - **詳細程度控制（Verbosity Control，WCAG 2.2.4 AAA）**：應用程式必須在設定中提供使用者可控制的廣播詳細程度選項，允許將 `interrupt: true` 的非緊急廣播降級為 Polite（`interrupt: false`），滿足 WCAG 2.2.4 要求使用者能延後或抑制非緊急中斷的 AAA 準則。
- **視覺穩定性與防閃爍**：
  - **雙重緩衝（Double Buffered）**：`MainForm` 與所有自定義對話框必須啟用 `DoubleBuffered = true`。
- **分離式回饋原則（Separated Feedback）**：
  - **鍵盤焦點**：當控制項透過鍵盤（如 Tab 鍵）獲得焦點時，僅執行「強烈靜態視覺回饋」（如明暗反轉、字體加粗），禁止啟動耗時的填滿動畫。
  - **注視／懸停**：當視線進入或滑鼠懸停時，啟動「線性填滿（Linear Fill）」動畫，提供明確的動作預期感。
  - **預設動作引導（Default Action Guidance）**：
    - **規範**：對話框的 `AcceptButton` 在焦點位於非按鈕控制項（如輸入框）時，必須顯示與「焦點框」相同的視覺特徵（如 Cyan／RoyalBlue 邊框），指引 Enter 鍵的預設動作。
    - **雙焦點衝突防護**：當使用者將焦點移至其他按鈕時，原預設按鈕的「焦點色」邊框必須自動移除並轉為基礎邊框，確保畫面上只有一個「焦點色」區域。
  - **基礎可辨識性（Base Recognizability）**：
    - **規範**：所有自定義繪製的按鈕在非活動狀態下必須具備基礎邊框，嚴禁呈現為無邊界的懸浮文字，確保在不同背景下的物理辨識度。
    - **DPI 適應性**：邊框厚度必須隨 DPI 縮放調整（建議至少為 `Math.Max(1, scale)`），確保在高解析度螢幕下仍具備清晰的物理邊界感。
    - **邊框停用原則**：若使用自定義 `Paint` 接管，必須將 `FlatAppearance.BorderSize` 設為 `0` 以消除原生邊框產生的粗細不均偽影。
- **語意與導覽**：  - 佈局容器必須設定 `AccessibleRole = Grouping` 並補足 `AccessibleName` 與 `Description`。
- **說明機制（Help Mechanism，WCAG 3.3.5 AAA）**：必須提供視覺上可操作的脈絡式說明入口（如 **F1** 快速鍵開啟說明對話框）。說明對話框必須包含鍵盤快速鍵一覽與遊戲控制器按鍵映射，並同時可由右鍵選單觸達，確保使用者在任何互動模式下皆能取得操作說明。
- **色覺與視覺安全規範**：
  - **眼動儀友善（Eye Tracker Optimized）**：
    - **動態回饋場景區隔（Feedback Scenario Separation）**：
      - **注視回饋（Dwell Feedback）**：屬於「使用者互動導向」。1000ms 線性填滿後應保持**靜態高亮**，**禁止持續律動**，以減少使用者分心並防止眼動儀「視線追逐」。
      - **視覺警示（Flash Alert）**：屬於「系統狀態導向」。發生邊界錯誤時，必須執行 **1Hz 平滑脈衝**（心跳變幻）以提供顯著提醒，直至錯誤狀態解除或焦點轉移。
    - **失焦重置**：當視線離開控制項時，必須立即重置進度條並終止背景任務。
    - **懸停進度條對比規範（Dwell Bar Contrast，WCAG 1.4.11）**：進度條填色必須以**懸停狀態底色**（而非焦點反轉底色）為基準計算對比，確保非文字 UI 組件對比達 ≥ 3:1。
      - **淺色模式**（懸停底色 `#DCDCDC`）：底色 `Green`（3.75:1）✅ + CVD 補償紋理 `PaleGreen`（4.06:1 on Green）。全類型 CVD 最低對比 3.50:1。
      - **深色模式**（懸停底色 `#3C3C3C`）：底色 `LimeGreen`（5.21:1）✅ + CVD 補償紋理 `DarkGreen`（3.51:1 on LimeGreen）。全類型 CVD 最低對比 3.45:1。
      - 配色選用綠色系，與焦點藍、警示橘形成「🔵靜態焦點／🟢動態進度／🟠錯誤警示」三色語意分工。
      - ⚠️ Flash Alert 的警示色是針對**焦點反轉底色**（黑／白）設計，不可直接套用於懸停進度條，兩者底色完全不同。
    - **非同步中止機制**：所有 UI 動畫任務必須實作基於 `Interlocked` 原子序號（animationId）的檢查機制，確保當焦點快速切換時，舊任務能立即中止。
    - **抗抖動寬度鎖定（Anti-Jitter Lock）**：為防止字體加粗引發佈局抖動導致眼動儀「追逐目標」，必須在初始化時預先計算 **Bold** 狀態的最大寬度並鎖定為 `MinimumSize`。
    - **對齊規範**：多控制項互動區（如數值對話框）優先採用 3 欄位網格佈局，達成垂直貫穿的對齊感。
  - **色覺友善（CVD）**：UI 狀態變更禁止僅依賴顏色。
    - **形狀補償**：必須結合形狀變化（如 Padding 加粗、邊框脈衝、字型加粗、心跳變幻）。
    - **全色盲支援（Achromatopsia）**：關鍵狀態變更（如獲得焦點、警示）必須包含「亮度對比」與「明暗反轉」。
      - **主題感知反轉（Theme-Aware Inversion）**：
        - **淺色模式**：反轉配色應為「黑底白字」。
        - **深色模式**：反轉配色應為「白底黑字」或具備極高亮度對比的亮色。
        - 禁止在深色模式下僅使用 `Color.Black` 作為焦點回饋，以免對比度不足。
      - **情境感知焦點邊框色（Context-Aware Focus Border Color）**：自定義按鈕的焦點邊框色，必須依控制項 `BackColor` **實際值**動態選取，確保在強視覺（反轉底色）與中性（懸停灰 / `isDefault` 系統色）兩種狀態下皆達 WCAG AA（≥ 4.5:1）以上：
        - `BackColor == Color.Black`（淺色強視覺反轉底色）→ `Cyan`（16.75:1 AAA）
        - `BackColor == Color.White`（深色強視覺反轉底色）→ `MediumBlue`（11.16:1 AAA）
        - 深色模式中性 / 懸停灰（`Color.Empty`）→ `LightBlue`（≥ 7.2:1 AAA）
        - 淺色模式中性 / 懸停灰（`Color.Empty`）→ `MediumBlue`（8.14:1 AAA）
        - **絕對禁令**：嚴禁在中性背景上固定使用 `Cyan`（對系統淺灰 ≈ 1.1:1 ❌）或 `RoyalBlue`（對系統深灰 #3C3C3C ≈ 2.28:1 ❌）或 `DeepSkyBlue`（對 #3C3C3C ≈ 5.2:1，未達 AAA ❌）作為焦點邊框色。邊框色**必須**基於 `btn.BackColor` 動態決定，而非全域 `isDark` 旗標。
        - **一致性要求**：所有自定義繪製的按鈕（`BtnCopy`、`NumericInputDialog` 各按鈕、`HelpDialog` 關閉按鈕）必須使用相同的情境感知邏輯，確保視覺一致性。
    - **色盲友善警示色**：在一般主題下，優先選用暖橘色（如 DarkOrange）作為警示色，以獲得跨 CVD 類型（Protan／Deutan／Tritan）的最佳對比。
    - **插值基色中性化（Interpolation Neutrality）**：
      - **規範**：執行閃爍動畫時，過渡起點（Base）必須固定為該控制項在該模式下的**焦點反轉底色**（深色模式用 `Color.White`，淺色模式用 `Color.Black`）。
      - **警報色配對原則**：
        - **淺色模式（黑底反轉）**：配對 **`DarkOrange`**。視覺效果為「由黑轉橘」的**發光呼吸感**，提供最佳物理辨識度。
        - **深色模式（白底反轉）**：配對 **`Firebrick`（磚紅）**。視覺效果為「由白轉紅」的**緊急縮減感**，在純白背景下對比度為 6.68:1（大文字 AAA），具備高度可辨識度。
      - **文字對比動態連動（Dynamic ForeColor Synchronization）**：
        - **規範**：執行視覺脈衝（Flash Alert）時，文字顏色（ForeColor）**禁止**固定。必須依據當前背景色（BackColor）的知覺亮度（Perceptual Luminance）動態切換為黑色或白色，確保在脈衝的任何階段（尤其是波峰時），文字對比度皆能維持在 WCAG AA 等級（4.5:1）以上。
        - **亮度計算標準**：判定 ForeColor 切換時，必須使用 **WCAG 相對亮度公式**（sRGB 線性化 `C_lin = ((C/255+0.055)/1.055)^2.4`，再 `L = 0.2126R + 0.7152G + 0.0722B`）並以 **L > 0.1791** 為切換閾值（WCAG crossover = √(1.05 × 0.05) − 0.05 ≈ 0.1791）。此精確切換確保動畫全程文字對比 ≥ 4.64:1 AA，14f bold 大型文字全程 ≥ 4.5:1 AAA。⚠️ **舊版 YUV 公式（0.299R + 0.587G + 0.114B > 128）已廢棄**，其近似誤差在 Black→DarkOrange 插值路徑中段（intensity ≈ 0.75）會造成切換延遲，導致文字對比跌至 3.5:1，低於 AA 標準。
        - ⚠️ **對比度測量標準**：若需測量 WCAG 合規對比比值（如 3:1 / 4.5:1 / 7:1），必須使用**完整相對亮度公式**（先 sRGB 線性化：`C_lin = ((C+0.055)/1.055)^2.4`，再 `L = 0.2126R + 0.7152G + 0.0722B`，最後 `Contrast = (L_hi+0.05)/(L_lo+0.05)`）。YUV 直接比值與 WCAG 對比比值差距可達 2 倍以上，不可混用。
      - **禁令**：禁止在兩個高飽和度色彩（如 Cyan 焦點色與 DarkOrange 警示色）之間進行線性插值。這會導致插值中點產生髒濁色（泥綠色或暗紫色）。
    - **警示作用域隔離（Alert Scoping）**：
      - **規範**：邊界觸頂／觸底警示應僅作用於**數據內容區域**（如輸入框背景或數值顯示區）。
      - **解耦原則**：與該數據互動的操作按鈕（如數值加減按鈕）即使具備焦點，也**禁止**參與同步背景閃爍。按鈕應維持其靜態視覺狀態（焦點邊框，顏色依情境感知選取），以維持「分離式回饋（Separated Feedback）」的資訊清晰度。
    - **遞歸背景更新（Recursive Visual Sync）**：
      - **規範**：當動態變更複雜控制項（如 `NumericUpDown`）的 `BackColor` 或 `ForeColor` 時，必須遞歸更新其內部的所有子控制項（特別是 `TextBox` 編輯區），確保無殘留底色（Ghosting）影響視覺一致性。
  - **光敏性癲癇防護（Photosensitive Epilepsy）**：
    - **脈衝定義（Pulse Definition）**：所有視覺警示（Flash Alert）必須以「平滑脈衝（Smooth Pulse）」形式實作，嚴禁使用具有突變轉折點的「線性脈衝（Linear Pulse）」或劇烈「閃爍（Flicker）」。
    - **頻率控制**：視覺律動頻率必須嚴格鎖定在 **1Hz**（1000ms 週期），遠低於 3Hz 的風險閾值。
    - **平滑漸變**：亮度與色彩變化必須使用**正弦波（Sine Wave）**過渡，確保在波峰與波底的變化率平滑流暢，減少對大腦視覺皮層的突發性刺激。
    - **系統動畫服從性**：必須主動感測 `SystemInformation.UIEffectsEnabled`。若動畫被關閉，則禁止執行循環閃爍或 Dwell 動畫，必須改為「靜態顯著提醒」。
    - **視覺凍結與抗抖動原則（Visual Freezing／Zero-Jitter）**：
      - **規範**：為了保護眼動儀使用者，所有動態視覺狀態變更（包含 **Flash Alert** 視覺警示與 **Focus State** 焦點切換）必須**嚴格禁止變動控制項的物理尺寸、Margin 或 Padding**。
      - **實作原則**：焦點高亮應透過「背景與前景色彩反轉」與「固定厚度（建議為 3px）的背景色區域」實現，確保文字內容與游標在狀態變更期間保持絕對位移靜止（Zero-Jitter）。
    - **高對比支援（High Contrast）**：變更顏色前必須檢查 `SystemInformation.HighContrast`。若開啟，則禁止使用自訂染色（如黑色背景），必須採用系統預設的高亮顏色（如 `SystemColors.Highlight`）。
- **系統偏好同步與主題管理（System Sync & Theme Management）**：
  - 必須透過 `SystemEvents.UserPreferenceChanged` 監控 `UserPreferenceCategory.General` 與 `Accessibility`，確保當使用者變更 Windows 動畫、色彩或主題設定時，UI 視覺行為能立即同步。
  - **動態主題管理（Dynamic Theme Management）**：
    - **禁止硬編碼預設色**：在執行階段（Runtime）還原控制項顏色時，禁止將 `BackColor` 或 `ForeColor` 賦值為特定的 `SystemColors` 靜態屬性（如 `SystemColors.Control`），這在 .NET 10 深色模式下會導致還原回錯誤的淺灰色。
    - **優先使用屬性重設**：還原預設配色時，應將顏色屬性設為 `Color.Empty`（或 `default`）。這能觸發 .NET 10 的原生主題引擎，自動根據當前系統配色（深色／淺色／高對比）套用正確的環境顏色。
    - **主題重啟判定**：判定是否需要重啟的基準應基於啟動時的初始狀態（`initialIsDarkMode`, `initialHighContrast`），確保精確判定主題變更。
- **⚠️ 核心限制：設計工具（Designer）保護與硬編碼原則**：
  - **設計時視覺化**：為了在 Visual Studio 設計工具內能直觀預覽文字與佈局（因 L10N 為非標準實作），允許且建議在 Designer 中硬編碼正體中文文字與 A11y 屬性。
  - **屬性疊加**：Designer 內設定的值為基礎，執行階段（Runtime）必須透過 `ApplyLocalization` 或分部類別（如 `MainForm.A11y.cs`）再次賦值以確保多語系正確性。
  - **禁止破壞結構**：嚴禁手動修改 `MainForm.Designer.cs` 中由設計工具生成的自動化佈局結構。

## 3. 遊戲控制器輸入 API 指引（XInput & GameInput）

- **自動退避機制**：當使用者設定使用 `GameInput` 但初始化失敗時，系統必須自動退避至 `XInput` 並告知使用者（AnnounceA11y）。應用程式之預設提供者應設定為相容性最高之 `XInput`。
- **搖桿死區與遲滯（Hysteresis）防抖規範**：
  - 在處理任何搖桿（包含虛擬方向鍵映射或連續邊界判定）時，**必須**實作雙閾值遲滯機制。
  - **動態閾值**：必須根據當前方向的啟動狀態，動態切換 `Enter`（觸發）與 `Exit`（重置）閾值，確保搖桿在磨損或輕微回彈時不會發生抖動、重複觸發或卡鍵。
- **震動安全性**：
  - **震動令牌檢查（Vibration Token）**：**各遊戲控制器實作類別（如 XInput／GameInput）內部**必須獨立實作 `_vibrationToken`（Interlocked 遞增長整數）機制，解決非同步停止震動時的競態條件，確保僅有最新的震動請求能控制馬達。**禁止在全域或服務層級共享此令牌**，以支援多控制器獨立運行的隔離性。
  - **同步停止能力**：控制器實作必須具備同步的 `StopVibration()` 方法，以支援應用程式關閉或崩潰時的緊急清理。
  - **連結權杖（Linked Token）**：震動延遲必須結合外部取消權杖與內部覆蓋權杖，確保在視窗關閉時馬達能立即停止。
  - 程式崩潰或結束前必須執行 `EmergencyStopAllActiveControllers()` 強制停止所有馬達。
- **效能標準**：
  - 輪詢必須使用 `PeriodicTimer` 鎖定在 60 FPS（約 16.6ms），且必須受 `CancellationToken` 監控。
  - **幀與毫秒換算基準（Frame-to-ms Standard）**：為了確保控制器長按連發（Auto-Repeat）在不同 API 下的體驗一致，所有以「幀（Frames）」為單位的設定值，在轉換為非同步延遲（Task.Delay）時，必須統一以 **1 幀 = 16.6 毫秒** 為基準進行精確換算（例如：30 幀預設為 500ms）。

## 4. 合規性與安全性紅線

### 外部合規基準

**代理人必須定期或在變更核心邏輯前，擷取並分析以下網址的最新內容，確保程式設計不違反其服務條款：**
- **FINAL FANTASY XIV 繁體中文版**：[使用者合約](https://www.ffxiv.com.tw/web/user_agreement.html)、[授權條款](https://www.ffxiv.com.tw/web/license.html)
- **宇峻奧汀（UserJoy）**：[隱私權政策](https://www.userjoy.com/mp/privacy.aspx)、[使用者授權合約（EULA）](https://www.userjoy.com/mp/eula.aspx)、[免責聲明](https://www.uj.com.tw/uj/service/service_user_disclaimer.aspx)

### 開發行為邊界

為了符合上述合約精神，嚴禁實作以下功能：
1. **零互動／注入**：禁止互動、修改、注入任何第三方應用程式之記憶體、封包或電磁紀錄。禁止還原工程。
2. **零模擬／同步**：禁止模擬輸入至其他視窗（行為僅止於「複製至剪貼簿」）；禁止同步操作多組帳號。
3. **零自動化**：嚴禁實作自動化遊戲行為邏輯（如自動連點、自動施法）。
4. **零偵測性**：禁止主動偵測特定第三方應用程式，防範防護軟體（Anti-Cheat）誤判。

### 系統整合安全性

- **P/Invoke**：必須套用 `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`。
- **單一執行個體**：Mutex 絕對禁止重複獲取。Mutex 必須具備 GUID 與 `Local\` 前綴以隔離使用者工作階段。
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
| Async／Thread | **非同步／執行緒** | 異步／線程 |

### 5.2 簡體中文（zh-Hans）術語表

應遵循大陸地區技術慣例，避免單純的「繁轉簡」。

| 英文術語 | 標準簡體中文翻譯 | 禁用詞彙 |
| :--- | :--- | :--- |
| Hotkey | **快捷键** | 快速键 |
| Clipboard | **剪贴板** | 剪贴簿 |
| History | **历史记录** | 历程记录 |
| Settings | **设置** | 设定 |
| Optimization | **优化** | 最佳化 |
| Async／Thread | **异步／线程** | 非同步／执行绪 |

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
3.  **助記鍵（Mnemonics）**：所有語系的按鈕助記鍵字母必須儘可能保持一致（如確認為 `(A)`、取消為 `(B)`），以維持與控制器按鍵映射的連動直覺。
    - **助記鍵冪等性**：動態生成助記鍵提示時必須執行冪等性檢查，確保若資源檔內容已手動包含快捷標記時，不會產生重複後綴。
4.  **資源註釋（Resource Comments）**：在 `.resx` 檔案中新增任何資源時，**必須**同時填寫 `<comment>` 標籤，且註釋內容必須使用與該資源**相同的語系**，以提供精確的開發與翻譯語境（Context）。

## 6. Git 提交規範

為了確保版本紀錄的可讀性與自動化相容性，所有提交必須遵循以下標準。

- **格式標準**：必須採用 [慣例式提交（Conventional Commits）v1.0.0](https://www.conventionalcommits.org/zh-hant/v1.0.0/) 規範。
  - 格式範例：`<type>(<scope>): <description>`。
  - 常見類型：`feat`（新功能）, `fix`（修補）, `docs`（文件）, `style`（格式）, `refactor`（重構）, `perf`（效能）, `test`（測試）, `chore`（例行事務）。
- **訊息完整性**：嚴禁僅使用 `git commit -m` 方式提交簡短主旨。必須提供完整的提交訊息，除 **Subject**（主旨）外，還必須包含 **Body**（說明內容），詳盡描述異動的原因、背景與具體實作細節。
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
