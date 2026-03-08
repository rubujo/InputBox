using InputBox.Resources;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputBox.Core.Configuration;

/// <summary>
/// 應用程式設定檔
/// </summary>
public class AppSettings
{
    /// <summary>
    /// AppSettings
    /// <para>單例模式，方便全域存取。</para>
    /// </summary>
    public static AppSettings Current { get; private set; } = new();

    /// <summary>
    /// JsonSerializerOptions
    /// </summary>
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new FloatingPointFormatConverter()
        }
    };

    /// <summary>
    /// 定義儲存路徑：%AppData%\InputBox\appsettings.json
    /// </summary>
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InputBox");

    /// <summary>
    /// 設定檔檔案路徑
    /// </summary>
    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "appsettings.json");

    #region MainForm 視窗與操作設定

    /// <summary>
    /// 視窗還原等待（毫秒）
    /// </summary>
    public int WindowRestoreDelay { get; set; } = 50;

    /// <summary>
    /// 剪貼簿重試間隔基礎值（毫秒）
    /// </summary>
    public int ClipboardRetryDelay { get; set; } = 20;

    /// <summary>
    /// 觸控鍵盤關閉緩衝（毫秒）
    /// </summary>
    public int TouchKeyboardDismissDelay { get; set; } = 300;

    /// <summary>
    /// 切換視窗前的基礎緩衝（毫秒）
    /// </summary>
    public int WindowSwitchBufferBase { get; set; } = 150;

    /// <summary>
    /// 輸入隨機抖動範圍（毫秒）
    /// </summary>
    public int InputJitterRange { get; set; } = 50;

    /// <summary>
    /// 輸入歷史記錄的最大容量
    /// </summary>
    public int HistoryCapacity { get; set; } = 100;

    #endregion

    #region 全域快速鍵設定

    /// <summary>
    /// 喚醒輸入框的修飾鍵組合值。
    /// <para>預設值 7 代表：Alt（1） + Ctrl（2） + Shift（4）</para>
    /// </summary>
    public int HotKeyModifiers { get; set; } = 7;

    /// <summary>
    /// 喚醒輸入框的主要按鍵（對應 Keys 列舉的字串表示，預設為 "I"）
    /// </summary>
    public string HotKeyKey { get; set; } = "I";

    #endregion

    #region 震動與回饋設定

    /// <summary>
    /// 是否啟用震動回饋
    /// </summary>
    public bool EnableVibration { get; set; } = true;

    /// <summary>
    /// 全域震動強度倍率（0.0 ~ 1.0）
    /// </summary>
    public float VibrationIntensity { get; set; } = 1.0f;

    #endregion

    #region GamepadController 控制器設定

    /// <summary>
    /// 遊戲手把控制器提供者類型
    /// </summary>
    public enum GamepadProvider
    {
        /// <summary>
        /// XInput
        /// </summary>
        XInput = 0,
        /// <summary>
        /// GameInput
        /// </summary>
        GameInput = 1
    }

    /// <summary>
    /// 遊戲手把控制器提供者（預設為 XInput）
    /// </summary>
    public GamepadProvider GamepadProviderType { get; set; } = GamepadProvider.XInput;

    /// <summary>
    /// 搖桿死區觸發閾值（Enter）- 預設 7849
    /// </summary>
    public int ThumbDeadzoneEnter { get; set; } = 7849;

    /// <summary>
    /// 搖桿死區重置閾值（Exit）- 預設 7000
    /// </summary>
    public int ThumbDeadzoneExit { get; set; } = 7000;

    /// <summary>
    /// 長按重複的初始延遲（幀）- 預設 30（約 500ms）
    /// </summary>
    public int RepeatInitialDelayFrames { get; set; } = 30;

    /// <summary>
    /// 長按重複的觸發間隔（幀）- 預設 5（約 80ms）
    /// </summary>
    public int RepeatIntervalFrames { get; set; } = 5;

    #endregion

    /// <summary>
    /// 載入設定
    /// </summary>
    public static void Load()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                string strJsonContent = File.ReadAllText(ConfigPath);

                Current = JsonSerializer.Deserialize<AppSettings>(strJsonContent) ?? new();

                // 讀取成功後立即存檔一次。
                // 這樣如果 C# 類別有新增欄位（而 JSON 裡沒有），
                // 這些新欄位的預設值會立即被寫入 JSON 檔，達成 Schema 同步。
                Save();
            }
            catch
            {
                // 讀取失敗時保持預設值，並可考慮是否覆蓋壞掉的檔案。
                Current = new();

                MessageBox.Show(
                    Strings.Err_ConfigInvalid,
                    caption: Strings.Wrn_Title,
                    buttons: MessageBoxButtons.OK,
                    icon: MessageBoxIcon.Exclamation);
            }
        }
        else
        {
            // 檔案不存在時，建立預設設定檔。
            Save();
        }
    }

    /// <summary>
    /// 儲存設定
    /// </summary>
    public static void Save()
    {
        string strTempPath = ConfigPath + ".tmp";

        try
        {
            // 確保資料夾存在。
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            string strJsonContent = JsonSerializer.Serialize(Current, Options);

            // 先寫入臨時檔案。
            File.WriteAllText(strTempPath, strJsonContent);

            // 寫入成功後，再原子性地替換原有檔案。
            // 使用 File.Move 的覆蓋模式（true）是最快速且支援跨磁區的安全替換方式。
            File.Move(strTempPath, ConfigPath, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"無法儲存設定檔：{ex.Message}");

            // 嘗試清理殘留的臨時檔。
            try
            {
                if (File.Exists(strTempPath))
                {
                    File.Delete(strTempPath);
                }
            }
            catch
            {
                // 忽略清理錯誤。
            }
        }
    }
}