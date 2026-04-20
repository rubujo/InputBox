using System.Globalization;

namespace InputBox.Core.Utilities;

/// <summary>
/// Wine / Proton 環境下的宿主 locale 橋接器
/// </summary>
/// <remarks>
/// Steam 會將 <c>LC_ALL</c> 強制設為 <c>"C"</c>，並透過 <c>HOST_LC_ALL</c> 保留宿主真實 locale。
/// Proton 啟動腳本（proton_3.x 起，commit 2ae0d898）在進程建立前已將 <c>HOST_LC_ALL</c> 複製至 <c>LC_ALL</c>，
/// 因此本類別只需讀取 <c>LC_ALL</c>，無需直接處理 <c>HOST_LC_ALL</c>。
/// 僅在 <see cref="SystemHelper.IsRunningOnWine"/> 回傳 <see langword="true"/> 時啟用；
/// 原生 Windows 執行時完全跳過，不對 <see cref="CultureInfo"/> 做任何異動。
/// </remarks>
internal static class WineLocaleBootstrapper
{
    /// <summary>
    /// 將宿主 POSIX locale 橋接為 .NET <see cref="CultureInfo"/> 並套用至所有執行緒。
    /// </summary>
    /// <remarks>
    /// 必須在 <c>Main()</c> 最前端、任何資源字串存取之前呼叫，
    /// 以確保衛星資源組件（<c>zh-Hant</c>、<c>zh-Hans</c> 等）在啟動時即以正確語系載入。
    /// 讀取優先順序：<c>LC_ALL</c> → <c>LANG</c>。
    /// 若 Wine 偵測失敗、環境變數不存在或 locale 無法對應至有效 <see cref="CultureInfo"/>，
    /// 則靜默略過並保留系統預設。
    /// </remarks>
    internal static void Apply()
    {
        if (!SystemHelper.IsRunningOnWine())
        {
            return;
        }

        string? raw =
            Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } lcAll ? lcAll :
            Environment.GetEnvironmentVariable("LANG") is { Length: > 0 } lang ? lang :
            null;

        if (raw is null)
        {
            return;
        }

        string? tag = NormalizePosixToTag(raw);

        if (tag is null)
        {
            return;
        }

        TryApplyCulture(tag);
    }

    /// <summary>
    /// 將 POSIX locale 字串正規化為 .NET culture tag，並映射到本專案支援的 culture。
    /// </summary>
    /// <param name="posixLocale">
    /// 原始 POSIX locale 字串，例如 <c>"zh_TW.UTF-8"</c>、<c>"ja_JP.UTF-8"</c> 或 <c>"C"</c>。
    /// </param>
    /// <returns>
    /// 正規化並映射後的 .NET culture tag（例如 <c>"zh-Hant"</c>、<c>"ja-JP"</c>）；
    /// 若輸入為空白、<c>"C"</c> 或 <c>"POSIX"</c> 等不應套用的值則回傳 <see langword="null"/>。
    /// </returns>
    internal static string? NormalizePosixToTag(string posixLocale)
    {
        if (string.IsNullOrWhiteSpace(posixLocale))
        {
            return null;
        }

        ReadOnlySpan<char> span = posixLocale.AsSpan().Trim();

        // 去除編碼後綴：zh_TW.UTF-8 → zh_TW
        int dot = span.IndexOf('.');
        if (dot > 0)
        {
            span = span[..dot];
        }

        // 去除修飾詞：en_US@euro → en_US
        int at = span.IndexOf('@');
        if (at > 0)
        {
            span = span[..at];
        }

        string tag = span.ToString().Replace('_', '-');

        // "C" 與 "POSIX" 代表 Steam 強制設定的中性值，不應套用
        if (tag is "C" or "POSIX")
        {
            return null;
        }

        return MapToSupportedCulture(tag);
    }

    /// <summary>
    /// 將 culture tag 映射到本專案資源目錄中存在的 culture 名稱。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 繁體中文各地區的 2 組件與 3 組件 ICU 格式均統一映射至 <c>zh-Hant</c>；
    /// 簡體中文各地區的 2 組件與 3 組件 ICU 格式均統一映射至 <c>zh-Hans</c>。
    /// </para>
    /// <para>
    /// 香港與澳門在 .NET 10 <c>SpecificCultures</c> 同時存在 <c>zh-Hans-*</c>（簡體）
    /// 與 <c>zh-Hant-*</c>（繁體）兩種規格；3 組件格式可精確分流，
    /// 2 組件 <c>zh-HK</c> / <c>zh-MO</c> 依 Linux glibc 慣例映射至繁體。
    /// </para>
    /// <para>
    /// 馬來西亞（<c>zh-MY</c>）的 2 組件形式 parent 為中性 <c>zh</c>（非 <c>zh-Hans</c>），
    /// 若直接 pass-through 則 resource fallback 會落回 Invariant 而非 <c>zh-Hans</c>，
    /// 因此須明確映射至 <c>zh-Hans</c>（馬來西亞華人社群以簡體為主）。
    /// </para>
    /// <para>
    /// 其他語系（<c>de-DE</c>、<c>ja-JP</c> 等）直接回傳原值，
    /// 由 .NET CultureInfo 的內建 fallback chain 自動處理衛星組件查找。
    /// </para>
    /// </remarks>
    /// <param name="tag">
    /// 已正規化的 .NET culture tag，
    /// 例如 <c>"zh-TW"</c>、<c>"zh-Hant-TW"</c>、<c>"zh-MY"</c> 或 <c>"ja-JP"</c>。
    /// </param>
    /// <returns>
    /// 映射後的 culture tag；若無需重映射則回傳原始 <paramref name="tag"/>。
    /// </returns>
    internal static string MapToSupportedCulture(string tag)
    {
        return tag switch
        {
            // 2 組件形式（POSIX 正規化後）—— 繁體
            "zh-TW" or "zh-HK" or "zh-MO" => "zh-Hant",

            // 2 組件形式（POSIX 正規化後）—— 簡體
            // zh-MY 的 parent 為中性 zh，須明確映射至 zh-Hans 避免 fallback 落回 Invariant
            "zh-CN" or "zh-SG" or "zh-MY" => "zh-Hans",

            // 3 組件 ICU 正式形式（.NET 10 SpecificCultures 的 canonical name）—— 繁體
            "zh-Hant-TW" or "zh-Hant-HK" or "zh-Hant-MO" or "zh-Hant-MY" => "zh-Hant",

            // 3 組件 ICU 正式形式（.NET 10 SpecificCultures 的 canonical name）—— 簡體
            "zh-Hans-CN" or "zh-Hans-HK" or "zh-Hans-MO" or "zh-Hans-SG" or "zh-Hans-MY" => "zh-Hans",

            _ => tag
        };
    }

    /// <summary>
    /// 將指定 culture tag 套用至目前執行緒與所有後續建立的執行緒。
    /// </summary>
    /// <param name="tag">
    /// 有效的 .NET culture tag，例如 <c>"zh-Hant"</c> 或 <c>"ja-JP"</c>。
    /// </param>
    private static void TryApplyCulture(string tag)
    {
        try
        {
            CultureInfo ci = CultureInfo.GetCultureInfo(tag);

            CultureInfo.CurrentCulture = ci;
            CultureInfo.CurrentUICulture = ci;
            CultureInfo.DefaultThreadCurrentCulture = ci;
            CultureInfo.DefaultThreadCurrentUICulture = ci;
        }
        catch
        {
            // Wine NLS 不支援或 tag 無效時，靜默略過，保留系統預設
        }
    }
}
