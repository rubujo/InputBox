using InputBox.Core.Utilities;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// <see cref="WineLocaleBootstrapper"/> 的 POSIX locale 正規化與 culture 映射邏輯單元測試。
/// </summary>
/// <remarks>
/// Wine 偵測（<c>IsRunningOnWine</c>）與 <see cref="System.Globalization.CultureInfo"/> 實際套用屬於整合行為，
/// 無法在原生 Windows CI 環境中可靠驗證，故本套件僅覆蓋可純函數測試的邏輯層。
/// </remarks>
public sealed class WineLocaleBootstrapperTests
{
    // ── NormalizePosixToTag：繁體中文 2 組件形式 ─────────────────────────────

    /// <summary>
    /// zh_TW.UTF-8 應正規化後映射為 zh-Hant（台灣繁體）。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_ZhTwUtf8_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.NormalizePosixToTag("zh_TW.UTF-8"));
    }

    /// <summary>
    /// zh_HK.UTF-8 應映射為 zh-Hant（香港繁體，依 Linux glibc 慣例）。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_ZhHkUtf8_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.NormalizePosixToTag("zh_HK.UTF-8"));
    }

    /// <summary>
    /// zh_MO.UTF-8 應映射為 zh-Hant（澳門繁體，依 Linux glibc 慣例）。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_ZhMoUtf8_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.NormalizePosixToTag("zh_MO.UTF-8"));
    }

    // ── NormalizePosixToTag：簡體中文 2 組件形式 ─────────────────────────────

    /// <summary>
    /// zh_CN.UTF-8 應正規化後映射為 zh-Hans（中國大陸簡體）。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_ZhCnUtf8_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.NormalizePosixToTag("zh_CN.UTF-8"));
    }

    /// <summary>
    /// zh_SG.UTF-8 應映射為 zh-Hans（新加坡簡體）。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_ZhSgUtf8_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.NormalizePosixToTag("zh_SG.UTF-8"));
    }

    /// <summary>
    /// zh_MY.UTF-8 應映射為 zh-Hans（馬來西亞；zh-MY parent 為中性 zh，須明確映射至 zh-Hans）。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_ZhMyUtf8_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.NormalizePosixToTag("zh_MY.UTF-8"));
    }

    // ── NormalizePosixToTag：其他語系 ────────────────────────────────────────

    /// <summary>
    /// ja_JP.UTF-8 應正規化為 ja-JP，不需特殊映射。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_JaJpUtf8_ReturnsJaJp()
    {
        Assert.Equal("ja-JP", WineLocaleBootstrapper.NormalizePosixToTag("ja_JP.UTF-8"));
    }

    /// <summary>
    /// de_DE.UTF-8 應正規化為 de-DE，不需特殊映射。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_DeDeUtf8_ReturnsDeDE()
    {
        Assert.Equal("de-DE", WineLocaleBootstrapper.NormalizePosixToTag("de_DE.UTF-8"));
    }

    /// <summary>
    /// fr_FR.UTF-8 應正規化為 fr-FR，不需特殊映射。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_FrFrUtf8_ReturnsFrFr()
    {
        Assert.Equal("fr-FR", WineLocaleBootstrapper.NormalizePosixToTag("fr_FR.UTF-8"));
    }

    /// <summary>
    /// ko_KR.UTF-8 應正規化為 ko-KR，不需特殊映射。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_KoKrUtf8_ReturnsKoKr()
    {
        Assert.Equal("ko-KR", WineLocaleBootstrapper.NormalizePosixToTag("ko_KR.UTF-8"));
    }

    // ── NormalizePosixToTag：無效與中性輸入 ──────────────────────────────────

    /// <summary>
    /// Steam 強制設定的 "C" 應回傳 null，不套用任何 culture。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_C_ReturnsNull()
    {
        Assert.Null(WineLocaleBootstrapper.NormalizePosixToTag("C"));
    }

    /// <summary>
    /// "POSIX" locale 應回傳 null，不套用任何 culture。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_POSIX_ReturnsNull()
    {
        Assert.Null(WineLocaleBootstrapper.NormalizePosixToTag("POSIX"));
    }

    /// <summary>
    /// 空字串應回傳 null。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_Empty_ReturnsNull()
    {
        Assert.Null(WineLocaleBootstrapper.NormalizePosixToTag(""));
    }

    /// <summary>
    /// 空白字串應回傳 null。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_Whitespace_ReturnsNull()
    {
        Assert.Null(WineLocaleBootstrapper.NormalizePosixToTag("   "));
    }

    /// <summary>
    /// 帶有 @ 修飾詞的 locale 應去除修飾詞後正規化，例如 de_DE@euro → de-DE。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_WithAtModifier_StripsModifier()
    {
        Assert.Equal("de-DE", WineLocaleBootstrapper.NormalizePosixToTag("de_DE@euro"));
    }

    /// <summary>
    /// 同時有編碼後綴與修飾詞時兩者皆應去除，例如 de_DE.UTF-8@euro → de-DE。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_WithEncodingAndModifier_StripsBoth()
    {
        Assert.Equal("de-DE", WineLocaleBootstrapper.NormalizePosixToTag("de_DE.UTF-8@euro"));
    }

    /// <summary>
    /// 不含地區的純語系 locale（例如 ja）應直接回傳語系代碼。
    /// </summary>
    [Fact]
    public void NormalizePosixToTag_LanguageOnly_ReturnsLanguage()
    {
        Assert.Equal("ja", WineLocaleBootstrapper.NormalizePosixToTag("ja"));
    }

    // ── MapToSupportedCulture：繁體中文 2 組件形式 ───────────────────────────

    /// <summary>
    /// zh-TW 應映射為 zh-Hant。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhTW_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.MapToSupportedCulture("zh-TW"));
    }

    /// <summary>
    /// zh-HK 應映射為 zh-Hant（依 Linux glibc 慣例）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHK_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.MapToSupportedCulture("zh-HK"));
    }

    /// <summary>
    /// zh-MO 應映射為 zh-Hant（依 Linux glibc 慣例）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhMO_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.MapToSupportedCulture("zh-MO"));
    }

    // ── MapToSupportedCulture：簡體中文 2 組件形式 ───────────────────────────

    /// <summary>
    /// zh-CN 應映射為 zh-Hans。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhCN_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-CN"));
    }

    /// <summary>
    /// zh-SG 應映射為 zh-Hans。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhSG_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-SG"));
    }

    /// <summary>
    /// zh-MY 應映射為 zh-Hans（馬來西亞；2 組件 parent 為中性 zh，須明確映射避免 fallback 落回 Invariant）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhMY_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-MY"));
    }

    // ── MapToSupportedCulture：繁體中文 3 組件 ICU 形式 ─────────────────────

    /// <summary>
    /// zh-Hant-TW 應映射為 zh-Hant（.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHantTW_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hant-TW"));
    }

    /// <summary>
    /// zh-Hant-HK 應映射為 zh-Hant（.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHantHK_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hant-HK"));
    }

    /// <summary>
    /// zh-Hant-MO 應映射為 zh-Hant（.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHantMO_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hant-MO"));
    }

    /// <summary>
    /// zh-Hant-MY 應映射為 zh-Hant（馬來西亞繁體，.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHantMY_ReturnsZhHant()
    {
        Assert.Equal("zh-Hant", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hant-MY"));
    }

    // ── MapToSupportedCulture：簡體中文 3 組件 ICU 形式 ─────────────────────

    /// <summary>
    /// zh-Hans-CN 應映射為 zh-Hans（.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHansCN_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hans-CN"));
    }

    /// <summary>
    /// zh-Hans-HK 應映射為 zh-Hans（香港簡體，.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHansHK_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hans-HK"));
    }

    /// <summary>
    /// zh-Hans-MO 應映射為 zh-Hans（澳門簡體，.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHansMO_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hans-MO"));
    }

    /// <summary>
    /// zh-Hans-SG 應映射為 zh-Hans（.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHansSG_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hans-SG"));
    }

    /// <summary>
    /// zh-Hans-MY 應映射為 zh-Hans（馬來西亞簡體，.NET 10 SpecificCultures canonical 形式）。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_ZhHansMY_ReturnsZhHans()
    {
        Assert.Equal("zh-Hans", WineLocaleBootstrapper.MapToSupportedCulture("zh-Hans-MY"));
    }

    // ── MapToSupportedCulture：非 zh 語系 pass-through ──────────────────────

    /// <summary>
    /// 非 zh 的 culture tag（例如 ja-JP）應原樣回傳，交由 .NET 內建 fallback 處理衛星組件查找。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_NonZh_ReturnsOriginal()
    {
        Assert.Equal("ja-JP", WineLocaleBootstrapper.MapToSupportedCulture("ja-JP"));
    }

    /// <summary>
    /// de-DE 應原樣回傳，交由 .NET 內建 fallback chain 找到 de 衛星組件。
    /// </summary>
    [Fact]
    public void MapToSupportedCulture_DeDE_ReturnsDeDE()
    {
        Assert.Equal("de-DE", WineLocaleBootstrapper.MapToSupportedCulture("de-DE"));
    }
}
