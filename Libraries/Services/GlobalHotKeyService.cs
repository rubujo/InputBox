using InputBox.Libraries.Interop;

namespace InputBox.Libraries.Services;

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
        // 註冊全域快速鍵：Ctrl + Alt + Shift + I。
        return Win32.RegisterHotKey(
            windowHandle,
            HotKey.ShowInput,
            (uint)Win32.KeyModifiers.Control |
            (uint)Win32.KeyModifiers.Alt |
            (uint)Win32.KeyModifiers.Shift,
            (uint)Keys.I);
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