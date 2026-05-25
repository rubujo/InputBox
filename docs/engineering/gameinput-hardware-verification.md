# GameInput 手動硬體驗證矩陣

本文件定義 GameInput 相關變更在正式發佈或高風險修改前的手動硬體檢查表。它補足 CI 無法穩定覆蓋的實體控制器、藍牙、Windows GameInput 執行階段與 redist 情境。

這不是日常 PR 必跑項目，也不是 CI 關卡。若只是修改文件、測試或不影響 GameInput 硬體行為的程式碼，不需要執行本矩陣。

## 執行時機

建議在下列情境執行：

- 修改 `InputWeave.GameInput` 版本、repo-local nupkg，或 InputBox 直接使用的 GameInput client/device/reading/callback/rumble 路徑。
- 修改 GameInput 連線/斷線偵測、重新列舉、callback、輪詢或退避行為。
- 修改 GameInput rumble 或緊急停止流程。
- 升級 `InputWeave.GameInput` 或 `Microsoft.GameInput` NuGet 套件。
- 正式發佈前，作為發佈冒煙驗證的人工補充。

## 非目標

- 不取代 `dotnet build`、`dotnet test`、runtime probe 或發佈輸出驗證。
- 不要求每個 PR 都跑完整硬體矩陣。
- 不要求維護者購買或常備所有控制器。
- 不新增 keyboard、mouse、sensors、raw report、force feedback、aggregate device 或 1:1 GameInput wrapper 的驗證範圍。

## 驗證前置條件

- 使用 Windows 環境與本機 Debug 或 Release 組建。
- 在 InputBox 設定中手動選擇 GameInput 提供者；預設提供者仍維持 XInput。
- 保留輸出視窗、`InputBox.log` 或測試者可取得的診斷輸出，方便記錄 InputWeave runtime 初始化、裝置狀態與退避訊息。
- 若某項硬體或環境不可取得，結果標記為 `略過`，並記錄原因。

## 驗證矩陣

| 項目 | 硬體 / 環境 | 操作步驟 | 預期結果 | 發佈必跑 | 可接受缺測 |
|---|---|---|---|---|---|
| Xbox USB | Xbox One / Xbox Series 控制器，以 USB 連線 | 啟動 InputBox，確認 GameInput 已連線；拔除並重接 3 次 | A11y 連線 / 斷線公告與實際狀態一致；不會卡在 connected；重新連線後仍可輸入 | 是 | 若沒有 Xbox 控制器，需以其他 XInput 相容 USB 控制器替代並記錄 |
| Xbox Bluetooth | Xbox One / Xbox Series 控制器，以藍牙連線 | 配對後啟動 InputBox；關閉控制器；重新喚醒或重新連線 | 裝置清單會重新整理；不殘留舊裝置；重新連線後不需重啟應用程式 | 是 | 若測試機沒有藍牙或控制器不支援藍牙，可標記略過 |
| Sony / DualSense | DualSense 或可回報 Sony VID/PID 的 PlayStation 控制器 | 連線後切換 Face 鍵 Auto 模式，確認 GameInput 診斷資訊中的 VID/PID 與配置 | Auto Face Button 解析為 PlayStation 配置；不依賴 USB database 或顯示名稱關鍵字才成功 | 建議 | 若沒有 Sony 控制器，可略過；正式發佈需記錄缺測 |
| Nintendo / Switch Pro | Switch Pro 或可回報 Nintendo VID/PID 的控制器 | 連線後切換 Face 鍵 Auto 模式，確認 GameInput 診斷資訊中的 VID/PID 與配置 | Auto Face Button 解析為 Nintendo 配置；不依賴 USB database 或顯示名稱關鍵字才成功 | 建議 | 若沒有 Nintendo 控制器，可略過；正式發佈需記錄缺測 |
| Elite / Paddle | Xbox Elite 或其他具 paddle / extra controls 的控制器 | 連線後檢查 GameInput capabilities / diagnostics | extra button / axis metadata 可進入診斷；paddle 或 extra controls 不觸發任何新的 InputBox 指令 | 建議 | 若沒有此類控制器，可略過 |
| 執行階段缺失 / runtime 載入失敗 | 未安裝 GameInput redist，且系統沒有可用 GameInput runtime 的環境 | 啟動 InputBox 並手動選擇 GameInput 提供者 | GameInput 初始化失敗會公告並退避 XInput；沒有 crash；InputWeave probe / log 能指出載入或初始化失敗原因 | 是 | 若測試機已有系統 GameInput 且無法安全遮蔽，需記錄未測原因 |
| Release ZIP | Release workflow 產物或本機發佈試跑 ZIP | 解壓縮並檢查 ZIP 結構，啟動 `InputBox.exe` 做 GameInput 冒煙驗證 | ZIP 不包含 `gameinput.dll` 或可見 `InputBox.GameInput.Native.dll` sidecar；包含 `redist/GameInputRedist.msi` 與第三方授權聲明 | 是 | 不可缺測 |

## 結果紀錄格式

建議在發佈說明、PR 描述或內部測試紀錄中使用下列格式：

```text
GameInput hardware verification:
- Xbox USB: 通過 (Xbox Series Controller, USB-C)
- Xbox Bluetooth: 通過 (Xbox Series Controller, Bluetooth)
- Sony / DualSense: 略過 (無可用硬體)
- Nintendo / Switch Pro: 略過 (無可用硬體)
- Elite / Paddle: 略過 (無可用硬體)
- 執行階段缺失 / runtime 載入失敗: 通過 (退避 XInput)
- Release ZIP: 通過
```

若任一發佈必跑項目失敗，應先修正或明確延後發佈；若標記略過，必須說明原因與替代驗證。
