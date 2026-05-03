# 開發環境與編碼規範 (Environment & Encoding)

- **預設作業系統**：Microsoft Windows。
- **編碼規範**：環境必須使用 **UTF-8 (Code Page 65001)**。
  - **PowerShell**：執行指令前必先設定 `[Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8`。
  - **CMD**：執行 `chcp 65001`。
  - **編輯或新增 `*.cs` 檔案**：文字編碼一律使用 **UTF-8 with BOM**，換行一律使用 **CRLF**。
  - **編輯或新增 `*.resx` 檔案**：文字編碼一律使用 **UTF-8 with BOM**，換行一律使用 **CRLF**。
  - **編輯或新增其他文字檔案**：文字編碼一律使用 **UTF-8**，換行一律使用 **CRLF**。
- **Shell 優先順序**：
  1. PowerShell 7+ (pwsh)
  2. Windows PowerShell 5.1
  3. Command Prompt (cmd)
- **指令相容性**：優先使用與環境相容的內建指令 (如 `dir` 或 `Get-ChildItem`)。

## Steam Deck / Wine / Proton 支援邊界

- **支援層級**：Steam Deck、SteamOS 3、Wine、Proton 與 Gamescope 僅屬於 best-effort compatibility；本專案不承諾原生 Linux 桌面應用程式支援。
- **現有相容性保留**：應保留既有 Wine / Proton / Gamescope 偵測、Steam 虛擬鍵盤喚起、Gamescope surface recovery 與遊戲模式保守視窗行為。
- **桌面模式與遊戲模式分流**：Steam Deck 桌面模式 (KDE Plasma) 應盡量維持 Windows 桌面功能；Gamescope 遊戲模式可套用保守限制，避免干擾合成器控管的全螢幕表面。
- **投入原則**：僅在實機回報、既有功能回歸或相容性保護失效時繼續修補；不為了擴大 Steam Deck 支援而主動遷移至 WPF、WinUI 3、Avalonia 或 .NET MAUI。
- **UI 技術方向**：現階段維持 WinForms 為最佳選擇，因核心功能依賴 Win32、TabTip、全域快速鍵、前景視窗恢復、XInput 與 GameInput。
