using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputBox.Core.Configuration;

/// <summary>
/// 針對所有浮點數型別（float、double、decimal）強制保留小數點的 JSON 轉換器
/// </summary>
public class FloatingPointFormatConverter : JsonConverterFactory
{
    /// <summary>
    /// 可以轉換的型別
    /// <para>告訴 JSON 序列化器，我們只攔截這三種浮點數型別</para>
    /// </summary>
    /// <param name="typeToConvert">Type</param>
    /// <returns>布林值</returns>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(float) ||
            typeToConvert == typeof(double) ||
            typeToConvert == typeof(decimal);
    }

    /// <summary>
    /// 動態產生對應的轉換器
    /// </summary>
    /// <param name="typeToConvert">Type</param>
    /// <param name="options">JsonSerializerOptions</param>
    /// <returns>JsonConverter</returns>
    /// <exception cref="NotSupportedException"></exception>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(float))
        {
            return new FloatConverter();
        }

        if (typeToConvert == typeof(double))
        {
            return new DoubleConverter();
        }

        if (typeToConvert == typeof(decimal))
        {
            return new DecimalConverter();
        }

        throw new NotSupportedException();
    }

    /// <summary>
    /// FloatConverter
    /// </summary>
    private class FloatConverter : JsonConverter<float>
    {
        /// <summary>
        /// 讀
        /// </summary>
        /// <param name="reader">Utf8JsonReader</param>
        /// <param name="typeToConvert">Type</param>
        /// <param name="options">JsonSerializerOptions</param>
        /// <returns>float</returns>
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetSingle();

        /// <summary>
        /// 寫
        /// </summary>
        /// <param name="writer">Utf8JsonWriter</param>
        /// <param name="value">float</param>
        /// <param name="options">JsonSerializerOptions</param>
        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        {
            if (value % 1 == 0)
            {
                writer.WriteRawValue(value.ToString("0.0", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }
    }

    /// <summary>
    /// DoubleConverter
    /// </summary>
    private class DoubleConverter : JsonConverter<double>
    {
        /// <summary>
        /// 讀
        /// </summary>
        /// <param name="reader">Utf8JsonReader</param>
        /// <param name="typeToConvert">Type</param>
        /// <param name="options">JsonSerializerOptions</param>
        /// <returns>double</returns>
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();

        /// <summary>
        /// 寫
        /// </summary>
        /// <param name="writer">Utf8JsonWriter</param>
        /// <param name="value">double</param>
        /// <param name="options">JsonSerializerOptions</param>
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (value % 1 == 0)
            {
                writer.WriteRawValue(value.ToString("0.0", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }
    }

    /// <summary>
    /// DecimalConverter
    /// </summary>
    private class DecimalConverter : JsonConverter<decimal>
    {
        /// <summary>
        /// 讀
        /// </summary>
        /// <param name="reader">Utf8JsonReader</param>
        /// <param name="typeToConvert">Type</param>
        /// <param name="options">JsonSerializerOptions</param>
        /// <returns>decimal</returns>
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

        /// <summary>
        /// 寫
        /// </summary>
        /// <param name="writer">Utf8JsonWriter</param>
        /// <param name="value">decimal</param>
        /// <param name="options">JsonSerializerOptions</param>
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            if (value % 1 == 0)
            {
                writer.WriteRawValue(value.ToString("0.0", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }
    }
}