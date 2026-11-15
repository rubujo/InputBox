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
  - **Standard／Latest 與必要疊層**：共用回饋使用 `FeedbackButton`，一般、懸停及停用沿用原生 Standard 表面；不得將系統強調色假設為藍色，不以固定黑／白文字取代原生選色。
    - 焦點反轉、粗體、按壓及提示框使用必要疊層，不寫入按鈕 BackColor／ForeColor，不切換 VisualStylesMode、不重建 HWND。保留 System 色彩模式、Deferred 顯示與必要重啟提示。
    - 高對比焦點使用 Highlight／HighlightText；按壓交換此系統色對並加入固定位置的內框。兩狀態的文字與內框都須通過實際相鄰像素對比，不只依賴色相區分。
    - 視窗未啟用時仍提供成對原生懸停與提示，不啟用視窗或移動焦點。滑鼠與 Space 按壓獨立管理；MouseLeave／Capture 取消只清除滑鼠來源，失焦或停用清除所有按壓。
    - Dwell 使用控制項自己的 UI Timer。更新字型、DPI、語系或系統色只更新設定與繪圖，不重建互動狀態／重啟進度；取消、表單權杖結束、HandleDestroyed 與 Dispose 終止計時。進度不作為懸停狀態的替代旗標。權杖取消必須有獨立 UI 通知，滿格停止 Timer 或停用 UI 特效時也須清除提示；更換權杖及 Dispose 時解除舊註冊，已排程的舊通知不可影響新生命週期。
    - 點擊後接合不得覆蓋更新的互動或明確取消，也不得自動產生點擊。測試涵蓋三主題、啟用／未啟用視窗、預設／一般按鈕、完整過渡與滑鼠／鍵盤交錯。
  - **預設動作引導**：當焦點在輸入框時，`AcceptButton` 必須顯示與焦點框一致的情境感知邊框色；強視覺反轉情境使用 **Cyan / MediumBlue**，中性情境使用 **LightBlue / MediumBlue**。
    - 輸入框與預設動作提示須共用情境選色，並在輸入框底色／焦點變更時重繪。Latest 的強調色按鈕不得使提示對比下降；可在提示邊框緊鄰處繪製對比襯底，保留按鈕本體與原生主題外觀，不得改變尺寸、Margin 或 Padding。
    - 測試須量測實際渲染的邊框與相鄰背景，不得只比對顏色名稱；同時覆蓋反轉及中性情境、三種主題與進度條主填色的對比。文件中對比數值為四捨五入顯示，不取代未取整的最低門檻。
- **眼動儀優化與視覺穩定性**：
  - **視覺凍結 (Zero-Jitter)**：狀態變更嚴禁變動物理尺寸、Margin 或 Padding。
  - **抗抖動鎖定 (Anti-Jitter Lock)**：初始化時預先計算 **Bold** 狀態最大寬度並鎖定為 `MinimumSize`。AutoSize 的偏好高度須依 Bold 文字及 DPI 預留原生置中文字上下的固定空間，確保完整 Dwell 襯底與字形不重疊；不得等懸停才擴高或改 Padding。
  - **懸停進度條對比 (WCAG 1.4.11)**：以實際相鄰的可見襯底為基準，不以原生按鈕 BackColor 屬性推測已渲染的強調色。填滿至 100% 時襯底仍須在四邊可見（至少 1 個實體像素，依 DPI 縮放）。
    - 淺色使用 #DCDCDC 襯底、Green 主填色、PaleGreen 色覺紋理，主填色對比約 3.75:1。
    - 深色使用 #3C3C3C 襯底、LimeGreen 主填色、DarkGreen 色覺紋理，主填色對比約 5.21:1。
    - 高對比使用 Highlight 襯底與 HighlightText 填色，不硬編碼替代系統色。
    - 主填色對襯底須 ≥3:1；紋理不能取代主填色門檻，亦不把裝飾混色像素誤當成主填色。襯底與進度皆在固定範圍內繪製，不變動控制項尺寸、Margin 或 Padding。
- **色覺友善 (CVD) 與亮度計算**：
  - **情境感知焦點邊框色**：依目前視覺表面／輸入情境與主題動態選取（完整 4 情境）：
    - `BackColor == Black`（淺色反轉，強視覺）→ `Cyan` (16.75:1 AAA)
    - `BackColor == White`（深色反轉，強視覺）→ `MediumBlue` (11.16:1 AAA)
    - 深色中性（未反轉，深色主題）→ `LightBlue` (≥7.2:1 AAA)
    - 淺色中性（未反轉，淺色主題）→ `MediumBlue` (8.14:1 AAA)
  - **視覺脈衝 (Flash Alert)**：
    - **唯一實作**：警示色、每幀配色與動畫節奏集中於 `FlashAlertAnimator`；各視窗只負責套用目標控制項與結束後的視覺還原，不得自行複製脈衝或配色邏輯。
    - 頻率：1Hz 平滑正弦波脈衝。
    - 基色：固定為焦點反轉底色 (深色用 White，淺色用 Black)。
    - **動態 ForeColor 連動**：背景亮度 **L > 0.1791** 時切換文字顏色。
    - **sRGB 線性化公式**：`f = C/255; C_lin = f <= 0.04045 ? f/12.92 : ((f+0.055)/1.055)^2.4`；`L = 0.2126R + 0.7152G + 0.0722B`。
- **遞迴與隔離**：
  - **遞迴更新**：更新 `NumericUpDown` 顏色時必須遞迴更新內部 `TextBox` 編輯區。
  - **作用域隔離**：邊界警示僅作用於資料內容區，互動按鈕禁止參與背景閃爍。
