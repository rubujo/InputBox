using InputBox.Core.Configuration;
using System.Text.Json;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// FloatingPointFormatConverter 的 JSON 序列化行為測試
/// <para>確保整數型浮點數序列化時強制保留小數點（如 1.0），避免 JSON 還原時因型別推斷誤判。</para>
/// </summary>
public class FloatingPointFormatConverterTests
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        Converters = { new FloatingPointFormatConverter() }
    };

    // ── CanConvert ─────────────────────────────────────────────────────────

    /// <summary>
    /// CanConvert 對 float 型別應回傳 true。
    /// </summary>
    [Fact]
    public void CanConvert_Float_ReturnsTrue()
    {
        var converter = new FloatingPointFormatConverter();
        Assert.True(converter.CanConvert(typeof(float)));
    }

    /// <summary>
    /// CanConvert 對 double 型別應回傳 true。
    /// </summary>
    [Fact]
    public void CanConvert_Double_ReturnsTrue()
    {
        var converter = new FloatingPointFormatConverter();
        Assert.True(converter.CanConvert(typeof(double)));
    }

    /// <summary>
    /// CanConvert 對 decimal 型別應回傳 true。
    /// </summary>
    [Fact]
    public void CanConvert_Decimal_ReturnsTrue()
    {
        var converter = new FloatingPointFormatConverter();
        Assert.True(converter.CanConvert(typeof(decimal)));
    }

    /// <summary>
    /// CanConvert 對 string 型別應回傳 false（不應攔截非浮點型別）。
    /// </summary>
    [Fact]
    public void CanConvert_String_ReturnsFalse()
    {
        var converter = new FloatingPointFormatConverter();
        Assert.False(converter.CanConvert(typeof(string)));
    }

    // ── float 序列化 ───────────────────────────────────────────────────────

    /// <summary>
    /// 整數型 float（如 1.0f）序列化後應包含小數點（"1.0"），而非純整數 "1"。
    /// </summary>
    [Fact]
    public void Serialize_IntegerFloat_ContainsDecimalPoint()
    {
        string json = JsonSerializer.Serialize(1.0f, _opts);
        Assert.Equal("1.0", json);
    }

    /// <summary>
    /// 小數型 float（如 1.5f）序列化後應保留原始數值。
    /// </summary>
    [Fact]
    public void Serialize_FractionalFloat_PreservesValue()
    {
        float value = 1.5f;
        string json = JsonSerializer.Serialize(value, _opts);
        float deserialized = JsonSerializer.Deserialize<float>(json, _opts);
        Assert.Equal(value, deserialized, precision: 5);
    }

    // ── double 序列化 ──────────────────────────────────────────────────────

    /// <summary>
    /// 整數型 double（如 2.0）序列化後應包含小數點（"2.0"）。
    /// </summary>
    [Fact]
    public void Serialize_IntegerDouble_ContainsDecimalPoint()
    {
        string json = JsonSerializer.Serialize(2.0, _opts);
        Assert.Equal("2.0", json);
    }

    /// <summary>
    /// 小數型 double（如 2.5）序列化後應保留原始數值。
    /// </summary>
    [Fact]
    public void Serialize_FractionalDouble_PreservesValue()
    {
        double value = 2.5;
        string json = JsonSerializer.Serialize(value, _opts);
        double deserialized = JsonSerializer.Deserialize<double>(json, _opts);
        Assert.Equal(value, deserialized, precision: 10);
    }

    // ── decimal 序列化 ─────────────────────────────────────────────────────

    /// <summary>
    /// 整數型 decimal（如 3m）序列化後應包含小數點（"3.0"）。
    /// </summary>
    [Fact]
    public void Serialize_IntegerDecimal_ContainsDecimalPoint()
    {
        string json = JsonSerializer.Serialize(3m, _opts);
        Assert.Equal("3.0", json);
    }

    /// <summary>
    /// 小數型 decimal（如 3.14m）序列化後應保留原始數值。
    /// </summary>
    [Fact]
    public void Serialize_FractionalDecimal_PreservesValue()
    {
        decimal value = 3.14m;
        string json = JsonSerializer.Serialize(value, _opts);
        decimal deserialized = JsonSerializer.Deserialize<decimal>(json, _opts);
        Assert.Equal(value, deserialized);
    }

    // ── 還原（Deserialize）─────────────────────────────────────────────────

    /// <summary>
    /// 從 JSON 數字 token 還原 float 應正確解析數值。
    /// </summary>
    [Fact]
    public void Deserialize_NumberToken_Float_ReturnsCorrectValue()
    {
        float result = JsonSerializer.Deserialize<float>("1.5", _opts);
        Assert.Equal(1.5f, result, precision: 5);
    }

    /// <summary>
    /// 從字串 token 還原 double 應正確解析數值。
    /// </summary>
    [Fact]
    public void Deserialize_StringToken_Double_ReturnsCorrectValue()
    {
        double result = JsonSerializer.Deserialize<double>("\"3.14\"", _opts);
        Assert.Equal(3.14, result, precision: 10);
    }

    /// <summary>
    /// 從無效字串還原 float 應回傳 0（防護性回退值）。
    /// </summary>
    [Fact]
    public void Deserialize_InvalidStringToken_Float_ReturnsFallbackZero()
    {
        float result = JsonSerializer.Deserialize<float>("\"not_a_number\"", _opts);
        Assert.Equal(0f, result);
    }

    /// <summary>
    /// 從無效字串還原 decimal 應回傳 0（防護性回退值）。
    /// </summary>
    [Fact]
    public void Deserialize_InvalidStringToken_Decimal_ReturnsFallbackZero()
    {
        decimal result = JsonSerializer.Deserialize<decimal>("\"not_a_number\"", _opts);
        Assert.Equal(0m, result);
    }

    // ── 往返一致性 ─────────────────────────────────────────────────────────

    /// <summary>
    /// float 往返序列化（序列化後再還原）應保持數值不變。
    /// </summary>
    [Fact]
    public void RoundTrip_Float_PreservesValue()
    {
        float original = 42.0f;
        string json = JsonSerializer.Serialize(original, _opts);
        float restored = JsonSerializer.Deserialize<float>(json, _opts);
        Assert.Equal(original, restored, precision: 5);
    }

    /// <summary>
    /// double 往返序列化應保持數值不變。
    /// </summary>
    [Fact]
    public void RoundTrip_Double_PreservesValue()
    {
        double original = 99.9;
        string json = JsonSerializer.Serialize(original, _opts);
        double restored = JsonSerializer.Deserialize<double>(json, _opts);
        Assert.Equal(original, restored, precision: 10);
    }
}
