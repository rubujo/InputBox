using InputBox.Core.Configuration;
using InputBox.Core.Interop;

namespace InputBox.Core.Services;

/// <summary>
/// 全域快速鍵服務
/// </summary>
internal class GlobalHotKeyService
{
    /// <summary>
    /// 註冊應用程式的全域快速鍵
    /// </summary>
    /// <param name="windowHandle">視窗控制代碼（Handle）</param>
    /// <returns>註冊是否成功</returns>
#pragma warning disable CA1822 // 將成員標記為靜態
    public bool RegisterShowInputHotkey(nint windowHandle)
#pragma warning restore CA1822 // 將成員標記為靜態
    {
        // 從設定檔讀取修飾鍵的數值（預設為 7，即 Ctrl+Alt+Shift）
        uint modifiers = (uint)AppSettings.Current.HotKeyModifiers;

        // 從設定檔讀取按鍵字串（預設為 "I"），並轉換為 Keys 列舉的數值。
        uint vkCode;

        if (Enum.TryParse(typeof(Keys), AppSettings.Current.HotKeyKey, true, out object? parsedKey) && 
            parsedKey != null)
        {
            vkCode = (uint)(Keys)parsedKey;
        }
        else
        {
            // 防呆機制：如果設定檔裡的字串亂填，退回預設值 I。
            vkCode = (uint)Keys.I;
        }

        // 註冊全域快速鍵。
        return Win32.RegisterHotKey(
            windowHandle,
            HotKey.ShowInput,
            modifiers,
            vkCode);
    }

    /// <summary>
    /// 註銷應用程式的全域快速鍵
    /// </summary>
    /// <param name="windowHandle">視窗控制代碼（Handle）</param>
#pragma warning disable CA1822 // 將成員標記為靜態
    public void UnregisterShowInputHotkey(nint windowHandle)
#pragma warning restore CA1822 // 將成員標記為靜態
    {
        Win32.UnregisterHotKey(windowHandle, HotKey.ShowInput);
    }
}