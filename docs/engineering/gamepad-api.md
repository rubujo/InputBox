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
