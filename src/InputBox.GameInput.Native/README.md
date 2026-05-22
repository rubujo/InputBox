# InputBox.GameInput.Native

`InputBox.GameInput.Native` 是 InputBox 專用的 Windows 原生 shim，用來把 Microsoft GameInput 執行階段轉成專案內部可控的窄版 C ABI。

這個專案不是通用 GameInput 包裝層，也不提供對外相容層。原生 shim 與 `src/InputBox/Core/Input/GameInput*.cs` 視為同版、同包、一起發佈。

## 範圍

目前只支援 InputBox 需要的 Gamepad-only 子集合：

- GameInput 執行階段載入與診斷 probe。
- Gamepad 裝置列舉、狀態讀取、VID/PID、顯示名稱與能力中繼資料。
- Gamepad reading/device 回呼註冊。
- Gamepad 震動。
- Shim ABI 與跨邊界結構大小診斷。

明確不支援：

- keyboard
- mouse
- sensors
- raw report
- force feedback
- aggregate device
- 1:1 GameInput API 包裝層

若要擴張範圍，必須先更新 `docs/engineering/gamepad-api.md`，重新評估安全邊界、測試覆蓋與發佈授權影響。

## ABI 規則

- C ABI 結構必須與 `src/InputBox/Core/Input/GameInputPrimitives.cs` 的受控端 P/Invoke 結構保持版面相容。
- 任何跨邊界結構欄位增刪或順序調整，都必須同步更新 `InputBoxGameInputShimAbiVersion` 與受控端大小檢查。
- `InputBoxGameInputGetShimInfo` 必須回報 ABI 版本、`GAMEINPUT_API_VERSION`、指標大小與結構大小。
- 受控端 `GameInput.Create()` 會驗證原生端回報的大小；不符時會視為 shim 載錯並退避 XInput。

## 執行階段載入

DLL 載入規則必須維持保守：

- 優先從 System32 載入 `GameInput.dll`。
- 再嘗試 System32 的 `GameInputRedist.dll`。
- 最後才使用登錄檔 `RedistDir` 內的 `GameInputRedist.dll`。
- 登錄檔 redist 絕對路徑必須搭配 `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32`。
- 不得從目前工作目錄或未限制搜尋路徑載入 `gameinput.dll`。

`InputBoxGameInputProbeRuntime` 是建立正式 context 前的診斷入口。它只能建立短生命週期 `IGameInput` 以回報 LoadLibrary、GetProcAddress、GameInputInitialize 的 HRESULT / Win32 error，不得留下回呼或長生命週期狀態。

## 執行緒與回呼

- `InputBoxGameInputContext::lock` 使用 SRW lock 保護 `gameInput`、`devices`、`callbacks` 與診斷計數器。
- refresh/read/unregister/destroy 可能與回呼註冊交錯，必須維持鎖定紀律。
- 不得在持有 context lock 時呼叫受控端回呼。
- 回呼只可複製 POD event 或作為喚醒/診斷輔助通道；正式輸入仍由受控端 60 FPS MTA 輪詢迴圈消費。
- 任何新增回呼路徑都必須避免傳遞 COM pointer 給 C#。

## 診斷資料

目前診斷資料包含：

- 執行階段 probe 的模組類型/路徑、HRESULT 與 Win32 錯誤碼。
- 字串截斷旗標。
- missing reading 計數器。
- repeated/backward timestamp 計數器。
- device unavailable refresh 計數器。
- 最後讀取的 HRESULT/status/timestamp。

這些資料只供 log、測試與未來過期 reading 分析，不得直接改變邊緣偵測、Pause/Resume 中立閘門或任何 UI 命令。

## 建置與發佈

- 原生 shim 由 `InputBox.GameInput.Native.vcxproj` 建置。
- `src/InputBox/InputBox.csproj` 會把 Release shim 以原生程式庫形式納入單檔發佈。
- CI 與 release 在原生 shim 建置後必須執行 `tools/Validate-GameInputNativeShim.ps1`，確認 `GameInputNativeMethods` 使用的 `InputBoxGameInput*` exports 全部存在。
- 同一支驗證腳本也會呼叫 `InputBoxGameInputProbeRuntime` 做無硬體 smoke test；GameInput runtime 初始化可成功或失敗，但 probe 必須可安全回報 ABI，且 native 回報的指標大小與跨 ABI 結構大小必須與受控端 mirror 相符。
- Release ZIP 不應包含可見的 `InputBox.GameInput.Native.dll` 側載 DLL，也不應包含 `gameinput.dll`。
- ZIP 可包含 `redist/GameInputRedist.msi` 供使用者手動安裝；InputBox 不會自動執行安裝程式。

## 驗證

常用驗證：

```powershell
dotnet build src/InputBox/InputBox.csproj --configuration Debug
dotnet test --project tests/InputBox.Tests/InputBox.Tests.csproj
.\tools\Validate-GameInputNativeShim.ps1 -NativeShimPath .\src\InputBox.GameInput.Native\bin\x64\Debug\InputBox.GameInput.Native.dll -ManagedSourcePath .\src\InputBox\Core\Input\GameInputNative.cs
```

修改 shim ABI、執行階段載入或發佈打包時，還要做 Release 發佈 / ZIP 試跑，確認單檔發佈、redist、授權聲明與禁止項目仍符合工作流程。
