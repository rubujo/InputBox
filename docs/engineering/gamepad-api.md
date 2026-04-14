# 遊戲控制器 API 指引 (Gamepad API)

## 1. 跨 API 共通標準 (Shared Standards)
不論是 **XInput** 還是 **GameInput** 實作，皆必須遵循以下規範：

- **效能標準**：
  - 輪詢鎖定 60 FPS (約 16.6ms)，必須使用 `PeriodicTimer` 並受 `CancellationToken` 監控。
  - **幀與毫秒換算 (60 FPS Standard)**：長按連發 (Auto-Repeat) 等設定值，統一以 **1 幀 = 16.6 毫秒** 為換算基準。
- **搖桿死區與遲滯 (Hysteresis)**：
  - 必須實作雙閾值。根據啟動狀態動態切換 `Enter` (觸發) 與 `Exit` (重置) 閾值，杜絕抖動與重複觸發。
- **震動安全性 (Vibration Safety)**：
  - **令牌隔離**：實作類別內部必須獨立實作 `_vibrationToken` (Interlocked)，**禁止在服務層級共享**，以支援多控制器獨立運行的隔離性。
  - **同步停止**：必須具備同步 `StopVibration()` 方法，支援緊急清理。
  - **連結權杖 (Linked Token)**：震動延遲須結合外部取消權杖與內部覆蓋權杖，確保視窗關閉時馬達能立即停止。

## 2. API 選擇與退避機制 (Provider & Backoff)
- **預設提供者**：應用程式之預設控制器提供者應設定為相容性最高之 **XInput**。
- **自動退避**：當使用者手動設定使用 `GameInput` 但初始化失敗（如系統不支援）時，系統必須**自動退避至 XInput** 並透過 `AnnounceA11y` 告知使用者。
- **緊急停止**：程式結束前必須執行 `EmergencyStopAllActiveControllers()` 強制停止所有控制器馬達。

## 3. P/Invoke 安全性
- 所有控制器相關的 DLL 調用 (XInput.dll 等) 必須套用 `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` 安全邊界。

## 4. 搖桿飄移補償 (Drift Compensation)

兩個後端（XInput / GameInput）均須實作自適應 EMA 飄移補償，且必須遵循以下規範：

### 4.1 演算法結構

- **自適應 EMA**：學習率公式為 `α = base + (max − base) × clamp(|error| / BiasAdaptiveErrorRange, 0, 1)`，誤差越大學習率越高，越快追蹤到真實偏移。
- **四軸追蹤**：必須對左搖桿 X/Y 與右搖桿 X/Y 共四軸分別維護 `_leftStickBiasX/Y`、`_rightStickBiasX/Y`。
- **右搖桿 Y 軸**：右搖桿 Y 偏移會被學習並**校正用於診斷**（`correctedRightThumbY` 出現於 Health Log 與 Ghost Log），但**不觸發任何導航事件**，亦不納入 `hasSignificantInput` 或 `ShouldForceReleaseDirectionalRepeat` 判斷。

### 4.2 常數縮放規則

XInput 以 `short` 範圍 (±32767) 運作；GameInput 以 float `[-1.0, 1.0]` 運作。
兩者閾值概念必須等效，換算關係如下：

| 常數 | XInput (short) | GameInput (float) |
|---|---|---|
| `BiasAdaptiveErrorRange` | `1638f` (≈ 0.05 × 32767) | `0.05f` |
| `LeftStickBiasLearningThreshold` | `9000` (≈ 0.275 × 32767) | `0.28f` |
| EMA 平滑係數 | 與 GameInput **完全相同** | 與 XInput **完全相同** |

### 4.3 D-Pad 機械耦合防污閘門

- D-Pad 按下期間，左搖桿因機械耦合可能產生 ±0.15–0.25 的偏移，落在學習閾值內，若不處理會污染 EMA。
- **規範**：`isDPadActive == true` 時，必須跳過左搖桿 X/Y 兩軸的 bias 更新；右搖桿不受此限。

### 4.4 連線暖機 (Bias Warm-up)

- 裝置連線後立即以第一幀快照重複執行 **50 次** EMA，使 bias 在第一幀即收斂至約 99%，避免連線初期因 bias ≈ 0 造成方向誤判。
- 暖機時固定傳入 `isDPadActive: false`。

### 4.5 右向映射保護（各後端可採不同機制，效果須等效）

右向映射較易受到正偏噪聲影響（尤其筆電環境），各後端須各自防止以下場景：
「DPad-Right 方向觸發後，左搖桿殘餘偏移立即重觸發右向」

- **XInput**：以 `_suppressMappedRightFromLeftStick` 旗標實作，重置方向狀態時若上一方向為 Right，則啟動抑制，直到左搖桿回到中立區。另配合非對稱退出閾值 `max(exit, enter × 0.75f)` 提高黏滯防護。
- **GameInput**：以 `ResetDirectionalRepeatState` 清除 `_previousProcessedButtons` 的 DPad 位元，強制下次觸發須重新穿越 Enter 閾值，配合硬體校準輸出達到等效保護。

## 5. Dialog 層級控制器整合
- **ConnectionChanged 強制訂閱**：任何持有或使用 `IGamepadController` 的 Dialog（對話框），在 `BindGamepadEvents` 中**必須**同時訂閱 `ConnectionChanged`，並在 `UnbindGamepadEvents` 中解除。
- **連線廣播**：`ConnectionChanged` 處理器必須透過對話框自身的 A11y 廣播機制（如 `AnnouncerLabel`）告知使用者連線狀態變更，不可依賴主視窗的廣播路徑。
- **處置順序**：對話框的 `OnFormClosing` 必須先呼叫 `UnbindGamepadEvents()` 再處置其他資源，確保事件解除先於控制器存取。
- **提示列即時同步**：像 `GamepadMessageBox` 這類會顯示 A／B 提示的 Dialog，在指派控制器實例時就必須依 `IsConnected` 立即同步提示列可見狀態，不能只等待後續 `ConnectionChanged` 事件，否則不同後端在對話框初次顯示時可能出現提示有無不一致。
- **子模態（GamepadMessageBox）模式**：當 Dialog 內部需要顯示 `GamepadMessageBox` 時，必須遵循以下順序，確保控制器輸入不被下層對話框截奪：
  1. 顯示 `GamepadMessageBox` 前呼叫 `UnsubscribeGamepadEvents()`，停止接收手把事件。
  2. 呼叫 `GamepadMessageBox.Show(...)`（阻塞直到使用者確認）。
  3. 在 `finally` 區塊中呼叫 `SubscribeGamepadEvents()`，恢復事件訂閱。
  4. 若 `ActiveForm == this`，追加呼叫 `_gamepadController?.Resume()`，防止因焦點競態導致控制器殘留在 Pause 狀態。
- **GameInput Resume 執行緒限制**：GameInput 的恢復流程若需要預同步當前快照或解除中立閘門，必須在背景 **MTA polling thread** 內進行；不得在 UI 執行緒直接呼叫底層讀取 API，否則可能觸發 `InvalidCastException`，並讓第一個有效按鍵被當成解除閘門而吞掉。
