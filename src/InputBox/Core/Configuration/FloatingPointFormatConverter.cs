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
    /// 判斷是否可以轉換指定型別；僅攔截 float、double、decimal 三種浮點數型別。
    /// </summary>
    /// <param name="typeToConvert">JSON 序列化器查詢的目標型別。</param>
    /// <returns>目標型別為 float、double 或 decimal 時回傳 <see langword="true"/>，否則回傳 <see langword="false"/>。</returns>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(float) ||
            typeToConvert == typeof(double) ||
            typeToConvert == typeof(decimal);
    }

    /// <summary>
    /// 依目標型別動態產生對應的浮點數 JSON 轉換器執行個體。
    /// </summary>
    /// <param name="typeToConvert">要建立轉換器的目標浮點數型別。</param>
    /// <param name="options">目前的 JSON 序列化設定。</param>
    /// <returns>對應目標型別的 <see cref="JsonConverter"/> 執行個體。</returns>
    /// <exception cref="NotSupportedException">目標型別不在支援清單內時擲出。</exception>
    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
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
    /// 針對 float 型別強制保留小數點格式的 JSON 轉換器。
    /// </summary>
    private class FloatConverter : JsonConverter<float>
    {
        /// <summary>
        /// 從 JSON 讀取並解析 float 值；支援數字 token 與字串形式，使用 InvariantCulture。
        /// </summary>
        /// <param name="reader">提供 JSON 資料的讀取器（以傳址方式傳入）。</param>
        /// <param name="typeToConvert">目標型別，此轉換器固定為 float。</param>
        /// <param name="options">目前的 JSON 序列化設定。</param>
        /// <returns>解析結果的 float 值；解析失敗時回傳 <c>0f</c>。</returns>
        public override float Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetSingle();
            }

            // 支援從字串解析，並強制使用 InvariantCulture。
            string? str = reader.GetString();

            return float.TryParse(str, CultureInfo.InvariantCulture, out float result) ?
                result :
                0f;
        }

        /// <summary>
        /// 將 float 值寫入 JSON；整數值強制以 "0.0" 格式輸出以保留小數點。
        /// </summary>
        /// <param name="writer">接收 JSON 輸出的寫入器。</param>
        /// <param name="value">要寫入的 float 值。</param>
        /// <param name="options">目前的 JSON 序列化設定。</param>
        public override void Write(
            Utf8JsonWriter writer,
            float value,
            JsonSerializerOptions options)
        {
            if (MathF.Abs(value % 1) < float.Epsilon)
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
    /// 針對 double 型別強制保留小數點格式的 JSON 轉換器。
    /// </summary>
    private class DoubleConverter : JsonConverter<double>
    {
        /// <summary>
        /// 從 JSON 讀取並解析 double 值；支援數字 token 與字串形式，使用 InvariantCulture。
        /// </summary>
        /// <param name="reader">提供 JSON 資料的讀取器（以傳址方式傳入）。</param>
        /// <param name="typeToConvert">目標型別，此轉換器固定為 double。</param>
        /// <param name="options">目前的 JSON 序列化設定。</param>
        /// <returns>解析結果的 double 值；解析失敗時回傳 <c>0.0</c>。</returns>
        public override double Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDouble();
            }

            string? str = reader.GetString();

            return double.TryParse(str, CultureInfo.InvariantCulture, out double result) ?
                result :
                0.0;
        }

        /// <summary>
        /// 將 double 值寫入 JSON；整數值強制以 "0.0" 格式輸出以保留小數點。
        /// </summary>
        /// <param name="writer">接收 JSON 輸出的寫入器。</param>
        /// <param name="value">要寫入的 double 值。</param>
        /// <param name="options">目前的 JSON 序列化設定。</param>
        public override void Write(
            Utf8JsonWriter writer,
            double value,
            JsonSerializerOptions options)
        {
            if (Math.Abs(value % 1) < double.Epsilon)
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
    /// 針對 decimal 型別強制保留小數點格式的 JSON 轉換器。
    /// </summary>
    private class DecimalConverter : JsonConverter<decimal>
    {
        /// <summary>
        /// 從 JSON 讀取並解析 decimal 值；支援數字 token 與字串形式，使用 InvariantCulture。
        /// </summary>
        /// <param name="reader">提供 JSON 資料的讀取器（以傳址方式傳入）。</param>
        /// <param name="typeToConvert">目標型別，此轉換器固定為 decimal。</param>
        /// <param name="options">目前的 JSON 序列化設定。</param>
        /// <returns>解析結果的 decimal 值；解析失敗時回傳 <c>0m</c>。</returns>
        public override decimal Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDecimal();
            }

            string? str = reader.GetString();

            return decimal.TryParse(str, CultureInfo.InvariantCulture, out decimal result) ?
                result :
                0m;
        }

        /// <summary>
        /// 將 decimal 值寫入 JSON；整數值強制以 "0.0" 格式輸出以保留小數點。
        /// </summary>
        /// <param name="writer">接收 JSON 輸出的寫入器。</param>
        /// <param name="value">要寫入的 decimal 值。</param>
        /// <param name="options">目前的 JSON 序列化設定。</param>
        public override void Write(
            Utf8JsonWriter writer,
            decimal value,
            JsonSerializerOptions options)
        {
            if (value % 1 == 0m)
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
