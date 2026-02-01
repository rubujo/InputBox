using InputBox.Resources;
using System.Diagnostics;
using System.Text.Json;

namespace InputBox.Libraries.Configuration;

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
        WriteIndented = true
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
        try
        {
            // 確保資料夾存在。
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            string strJsonContent = JsonSerializer.Serialize(Current, Options);

            File.WriteAllText(ConfigPath, strJsonContent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"無法儲存設定檔：{ex.Message}");
        }
    }
}