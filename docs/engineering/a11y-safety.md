# A11y 無障礙與視覺安全 (A11y & Visual Safety)

- **廣播與避讓 (Announce)**：
  - 廣播前保留 200ms 延遲 (於非 UI 執行緒執行)，避開 Audio Ducking。
  - 重複訊息末尾交替附加 `\u200B` (ZWSP) 或 `\u200C` (ZWNJ)。
  - **Clear() 字元規範**：廣播元件的 `Clear()` 操作必須使用 `\u200B` (ZWSP) 填充，**禁止**使用 `\u00A0` (NBSP)。NBSP 部分版本的 JAWS／NVDA 會朗讀為「blank（空白）」，ZWSP 為零寬字元，AT 不會朗讀，同樣能觸發 UIA `LiveRegionChanged` 事件。
  - **詳細程度控制 (WCAG 2.2.4 AAA)**：提供選項將 `interrupt: true` 的非緊急廣播降級為 Polite。
  - **Interrupt 過期機制**：`AnnouncementService` 以 `_latestInterruptId`（`Interlocked.Exchange` 寫入、`Interlocked.Read` 讀取）追蹤最新 interrupt 序號。消費端於**兩處**補檢：①處理 polite 請求前，若 `_latestInterruptId > request.Id` 則丟棄；②ducking 延遲結束後，同樣補檢一次。此機制替代原本的 `TryPeek + currentLatestId` 比較，修正了 polite 訊息在更新 interrupt 後仍被播出的競態。
  - **AnnouncerLabel 初始化規範**：所有 `AnnouncerLabel` 實例**必須**設定以下屬性，確保螢幕閱讀器行為一致且不污染 Tab 順序：
    ```csharp
    new AnnouncerLabel
    {
        AccessibleName = "\u200B", // ZWSP：AT 不朗讀但定義有效名稱，觸發 live region
        TabStop       = false,
        BackColor     = Color.Empty,
        ForeColor     = Color.Empty,
        // ...
    }
    ```
- **視覺回饋標準 (Visual Feedback)**：
  - **分離式回饋**：
    - 鍵盤焦點：強烈靜態視覺回饋 (反轉、字體加粗)。
    - 按壓狀態：獨立於焦點的第三層級。深色模式使用飽和暖色 (琥珀色 `255,200,120`)，亮度差 **ΔL*≥13**。
    - **Tritanopia 調整亮度公式**：$L_{tritan} = (R_{lin} \times 0.2126 + G_{lin} \times 0.7152) / 0.9278$。
  - **預設動作引導**：當焦點在輸入框時，`AcceptButton` 必須顯示與焦點框相同的視覺特徵 (Cyan/RoyalBlue 邊框)。
- **眼動儀優化與視覺穩定性**：
  - **視覺凍結 (Zero-Jitter)**：狀態變更嚴禁變動物理尺寸、Margin 或 Padding。
  - **抗抖動鎖定 (Anti-Jitter Lock)**：初始化時預先計算 **Bold** 狀態最大寬度並鎖定為 `MinimumSize`。
  - **懸停進度條對比 (WCAG 1.4.11)**：填色須以按鈕「**實際**底色」為基準，使用 `btn.BackColor.GetBrightness() > 0.5f`（`Color.Empty` 時回退至 `!isDark`）動態選色，**禁止**直接使用 `isDark` 旗標（懸停時 `ApplyStrongVisual` 會反轉底色，`isDark` 與實際底色脫節）：
    - 底色為淺色（亮度 > 0.5，含懸停反轉後的 White）→ **Green** + CVD 紋理 PaleGreen。
    - 底色為深色（亮度 ≤ 0.5，含懸停反轉後的 Black）→ **LimeGreen** + CVD 紋理 DarkGreen。
    - 自然狀態對比：淺色模式 Green vs `#DCDCDC` = 3.75:1 ✅；深色模式 LimeGreen vs `#3C3C3C` = 5.21:1 ✅。
    - 懸停反轉對比：深色模式 bg→White，Green vs White = 5.14:1 ✅；淺色模式 bg→Black，LimeGreen vs Black = 9.91:1 ✅。
- **色覺友善 (CVD) 與亮度計算**：
  - **情境感知焦點邊框色**：依 `BackColor` 實際值與主題動態選取（完整 4 情境）：
    - `BackColor == Black`（淺色反轉，強視覺）→ `Cyan` (16.75:1 AAA)
    - `BackColor == White`（深色反轉，強視覺）→ `MediumBlue` (11.16:1 AAA)
    - 深色中性（未反轉，深色主題）→ `LightBlue` (≥7.2:1 AAA)
    - 淺色中性（未反轉，淺色主題）→ `MediumBlue` (8.14:1 AAA)
  - **視覺脈衝 (Flash Alert)**：
    - 頻率：1Hz 平滑正弦波脈衝。
    - 基色：固定為焦點反轉底色 (深色用 White，淺色用 Black)。
    - **動態 ForeColor 連動**：背景亮度 **L > 0.1791** 時切換文字顏色。
    - **sRGB 線性化公式**：`f = C/255; C_lin = f <= 0.04045 ? f/12.92 : ((f+0.055)/1.055)^2.4`；`L = 0.2126R + 0.7152G + 0.0722B`。
- **遞歸與隔離**：
  - **遞歸更新**：更新 `NumericUpDown` 顏色時必須遞歸更新內部 `TextBox` 編輯區。
  - **作用域隔離**：邊界警示僅作用於數據內容區，互動按鈕禁止參與背景閃爍。
