using InputBox.Core.Configuration;
using InputBox.Core.Interop;

namespace InputBox.Core.Services;

/// <summary>
/// 全域快速鍵服務
/// </summary>
internal class GlobalHotKeyService
{
    /// <summary>
    /// 鎖物件，用於保護全域快速鍵註冊與註銷的執行緒安全
    /// </summary>
    private static readonly Lock HotKeyLock = new();

    /// <summary>
    /// 註冊應用程式的全域快速鍵
    /// </summary>
    /// <param name="windowHandle">視窗控制代碼（Handle）</param>
    /// <returns>註冊是否成功</returns>
    public static bool RegisterShowInputHotkey(nint windowHandle)
    {
        lock (HotKeyLock)
        {
            // 註冊前先確保舊的已註銷，避免因 ID 衝突導致註冊失敗。
            UnregisterShowInputHotkey(windowHandle);

            // 從設定檔讀取修飾鍵的數值（預設為 7，即 Ctrl+Alt+Shift）
            uint modifiers = (uint)AppSettings.Current.HotKeyModifiers;

            string keyStr = AppSettings.Current.HotKeyKey;

            // 如果按鍵設定為 None，代表暫不註冊。
            if (string.IsNullOrEmpty(keyStr) ||
                keyStr == "None")
            {
                return true;
            }

            // 從設定檔讀取按鍵字串（預設為 "I"），並轉換為 Keys 列舉的數值。
            uint vkCode;

            if (Enum.TryParse(typeof(Keys), keyStr, true, out object? parsedKey) &&
                parsedKey != null)
            {
                // Keys 列舉高位元組含有修飾鍵旗標（如 Keys.Control = 0x20000），
                // 必須以 Keys.KeyCode 遮罩，取得純虛擬鍵碼（VK code），避免傳入無效的高位數值。
                vkCode = (uint)((Keys)parsedKey & Keys.KeyCode);

                if (vkCode == 0)
                {
                    // 防呆機制：遮罩後若為 0，退回預設值 I。
                    vkCode = (uint)Keys.I;
                }
            }
            else
            {
                // 防呆機制：如果設定檔裡的字串亂填，退回預設值 I。
                vkCode = (uint)Keys.I;
            }

            // 註冊全域快速鍵。
            return User32.RegisterHotKey(
                windowHandle,
                HotKey.ShowInput,
                modifiers,
                vkCode);
        }
    }

    /// <summary>
    /// 註銷應用程式的全域快速鍵
    /// </summary>
    /// <param name="windowHandle">視窗控制代碼（Handle）</param>
    public static void UnregisterShowInputHotkey(nint windowHandle)
    {
        lock (HotKeyLock)
        {
            User32.UnregisterHotKey(windowHandle, HotKey.ShowInput);
        }
    }

    /// <summary>
    /// 取得目前快速鍵組合的可讀字串表示
    /// </summary>
    /// <param name="separator">修飾鍵與主要按鍵的分隔符號</param>
    /// <returns>字串，例如 "Ctrl+Alt+I"</returns>
    public static string GetHotKeyDisplayString(string separator = "+")
    {
        List<string> keys = [];

        User32.KeyModifiers mods = AppSettings.Current.HotKeyModifiers;

        // 檢查位元遮罩包含哪些修飾鍵。
        if (mods.HasFlag(User32.KeyModifiers.Control))
        {
            keys.Add(Resources.Strings.Mod_Ctrl);
        }

        if (mods.HasFlag(User32.KeyModifiers.Alt))
        {
            keys.Add(Resources.Strings.Mod_Alt);
        }

        if (mods.HasFlag(User32.KeyModifiers.Shift))
        {
            keys.Add(Resources.Strings.Mod_Shift);
        }

        if (mods.HasFlag(User32.KeyModifiers.Win))
        {
            keys.Add(Resources.Strings.Mod_Win);
        }

        // 取得主要按鍵。
        string keyStr = AppSettings.Current.HotKeyKey ?? "I";

        keys.Add(keyStr.ToUpper());

        return string.Join(separator, keys);
    }
}