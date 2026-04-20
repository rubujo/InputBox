using InputBox.Core.Configuration;
using InputBox.Core.Extensions;

namespace InputBox.Core.Utilities;

/// <summary>
/// 管理全域共享 A11y 字體快取與延遲回收桶
/// </summary>
public static class FontResourceManager
{
    /// <summary>
    /// 字體回收桶緊急清理門檻（字體數量超過此值時立即全部清除）
    /// </summary>
    private const int FontTrashCanEmergencyThreshold = 50;

    /// <summary>
    /// 待回收的舊字體資源池（防止跨視窗引用時發生 ObjectDisposedException）
    /// </summary>
    private static readonly List<Font> _fontTrashCan = [];

    /// <summary>
    /// 回收桶專用鎖（用於保護靜態字體回收池）
    /// </summary>
    private static readonly Lock _trashCanLock = new();

    /// <summary>
    /// 全域共享的 A11y 標準字型快取（依據 DPI、字型家族與尺寸倍率儲存，支援 PerMonitorV2）
    /// </summary>
    private static readonly Dictionary<(int DpiSize, string Family), Font> _regularFontCache = [];

    /// <summary>
    /// 全域共享的 A11y 標準字型快取鎖（用於保護靜態字型快取）
    /// </summary>
    private static readonly Lock _regularFontCacheLock = new();

    /// <summary>
    /// 全域共享的 A11y 加粗字型快取（依據 DPI、字型家族與尺寸倍率儲存，支援 PerMonitorV2）
    /// </summary>
    private static readonly Dictionary<(int DpiSize, string Family), Font> _boldFontCache = [];

    /// <summary>
    /// 全域共享的 A11y 加粗字型快取鎖（用於保護靜態字型快取）
    /// </summary>
    private static readonly Lock _boldFontCacheLock = new();

    /// <summary>
    /// 將不再使用的私有字體加入回收桶，延遲處置
    /// </summary>
    /// <param name="font">要延遲回收的字體實例。</param>
    public static void AddFontToTrashCan(Font font)
    {
        if (font == null)
        {
            return;
        }

        lock (_trashCanLock)
        {
            _fontTrashCan.Add(font);

            // 防護機制：若回收桶堆積超過門檻，立即執行一次緊急清理。
            if (_fontTrashCan.Count > FontTrashCanEmergencyThreshold)
            {
                foreach (Font f in _fontTrashCan)
                {
                    try
                    {
                        f.Dispose();
                    }
                    catch
                    {
                    }
                }

                _fontTrashCan.Clear();
            }

            // 啟動延遲清理任務，避免字體堆積引發 GDI 資源洩漏。
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000);

                    lock (_trashCanLock)
                    {
                        if (_fontTrashCan.Contains(font))
                        {
                            font.Dispose();
                            _fontTrashCan.Remove(font);
                        }
                    }
                }
                catch
                {
                    // 忽略背景清理的任何錯誤。
                }
            }).SafeFireAndForget();
        }
    }

    /// <summary>
    /// 判斷目前字體是否為全域共享快取字體（絕對禁止在視窗中手動處置）
    /// </summary>
    /// <param name="font">要檢查的字體實例</param>
    /// <returns>若為共享字體則傳回 true</returns>
    public static bool IsSharedFont(Font font)
    {
        if (font == null)
        {
            return false;
        }

        lock (_regularFontCacheLock)
        {
            if (_regularFontCache.ContainsValue(font))
            {
                return true;
            }
        }

        lock (_boldFontCacheLock)
        {
            if (_boldFontCache.ContainsValue(font))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 取得全域共享的 A11y 放大字型
    /// </summary>
    /// <param name="dpi">目前的 DeviceDpi</param>
    /// <param name="style">字體樣式（預設為 Regular）</param>
    /// <param name="family">字體家族（選用）</param>
    /// <param name="sizeMultiplier">尺寸倍率（預設為 1.0）</param>
    /// <returns>從快取取出或新建立的共享 A11y 字型執行個體。</returns>
    public static Font GetSharedA11yFont(
        int dpi,
        FontStyle style = FontStyle.Regular,
        FontFamily? family = null,
        float sizeMultiplier = 1.0f)
    {
        Dictionary<(int DpiSize, string Family), Font> cache = style.HasFlag(FontStyle.Bold) ?
            _boldFontCache :
            _regularFontCache;

        Lock cacheLock = style.HasFlag(FontStyle.Bold) ?
            _boldFontCacheLock :
            _regularFontCacheLock;

        int dpiSize = (int)(dpi * sizeMultiplier * 1000);
        string familyName = (family ?? FontFamily.GenericSansSerif).Name;
        (int, string) cacheKey = (dpiSize, familyName);

        lock (cacheLock)
        {
            if (!cache.TryGetValue(cacheKey, out Font? font))
            {
                const float baseA11ySize = 14.0f;
                float finalSize = baseA11ySize * sizeMultiplier * (dpi / AppSettings.BaseDpi);

                family ??= FontFamily.GenericSansSerif;
                font = new Font(family, finalSize, style);

                cache[cacheKey] = font;
            }

            return font;
        }
    }

    /// <summary>
    /// 處置所有快取的字體資源，防止程式結束後的 GDI 洩漏
    /// </summary>
    public static void DisposeCaches()
    {
        lock (_regularFontCacheLock)
        {
            foreach (Font font in _regularFontCache.Values)
            {
                try
                {
                    font.Dispose();
                }
                catch
                {
                }
            }

            _regularFontCache.Clear();
        }

        lock (_boldFontCacheLock)
        {
            foreach (Font font in _boldFontCache.Values)
            {
                try
                {
                    font.Dispose();
                }
                catch
                {
                }
            }

            _boldFontCache.Clear();
        }

        lock (_trashCanLock)
        {
            foreach (Font font in _fontTrashCan)
            {
                try
                {
                    font.Dispose();
                }
                catch
                {
                }
            }

            _fontTrashCan.Clear();
        }
    }
}