using InputBox.Core.Interop;
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
    private volatile int _windowRestoreDelay = 50;

    /// <summary>
    /// 視窗還原等待（毫秒）
    /// </summary>
    public int WindowRestoreDelay
    {
        get => _windowRestoreDelay;
        set => _windowRestoreDelay = Math.Clamp(value, 0, 5000);
    }

    /// <summary>
    /// 剪貼簿重試間隔基礎值（毫秒）
    /// </summary>
    private volatile int _clipboardRetryDelay = 20;

    /// <summary>
    /// 剪貼簿重試間隔基礎值（毫秒）
    /// </summary>
    public int ClipboardRetryDelay
    {
        get => _clipboardRetryDelay;
        set => _clipboardRetryDelay = Math.Clamp(value, 0, 1000);
    }

    /// <summary>
    /// 觸控式鍵盤關閉緩衝（毫秒）
    /// </summary>
    private volatile int _touchKeyboardDismissDelay = 300;

    /// <summary>
    /// 觸控式鍵盤關閉緩衝（毫秒）
    /// </summary>
    public int TouchKeyboardDismissDelay
    {
        get => _touchKeyboardDismissDelay;
        set => _touchKeyboardDismissDelay = Math.Clamp(value, 0, 5000);
    }

    /// <summary>
    /// 切換視窗前的基礎緩衝（毫秒）
    /// </summary>
    private volatile int _windowSwitchBufferBase = 150;

    /// <summary>
    /// 切換視窗前的基礎緩衝（毫秒）
    /// </summary>
    public int WindowSwitchBufferBase
    {
        get => _windowSwitchBufferBase;
        set => _windowSwitchBufferBase = Math.Clamp(value, 0, 5000);
    }

    /// <summary>
    /// 輸入隨機抖動範圍（毫秒）
    /// </summary>
    private volatile int _inputJitterRange = 50;

    /// <summary>
    /// 輸入隨機抖動範圍（毫秒）
    /// </summary>
    public int InputJitterRange
    {
        get => _inputJitterRange;
        set => _inputJitterRange = Math.Clamp(value, 0, 1000);
    }

    /// <summary>
    /// 輸入歷程記錄的最大容量
    /// </summary>
    private volatile int _historyCapacity = 100;

    /// <summary>
    /// 輸入歷程記錄的最大容量
    /// </summary>
    public int HistoryCapacity
    {
        get => _historyCapacity;
        set => _historyCapacity = Math.Clamp(value, 1, 1000);
    }

    /// <summary>
    /// 是否啟用隱私模式（不紀錄新的輸入）
    /// </summary>
    private volatile bool _isPrivacyMode = false;

    /// <summary>
    /// 是否啟用隱私模式（不紀錄新的輸入）
    /// </summary>
    public bool IsPrivacyMode
    {
        get => _isPrivacyMode;
        set => _isPrivacyMode = value;
    }

    /// <summary>
    /// 視窗不透明度（0.5 ~ 1.0）
    /// </summary>
    private volatile float _windowOpacity = 1.0f;

    /// <summary>
    /// 視窗不透明度（0.5 ~ 1.0）
    /// </summary>
    public float WindowOpacity
    {
        get => _windowOpacity;
        set => _windowOpacity = Math.Clamp(value, 0.5f, 1.0f);
    }

    #endregion

    #region 全域快速鍵設定

    /// <summary>
    /// 喚醒輸入框的修飾鍵組合值。
    /// <para>預設值 7 代表：Alt（1） + Ctrl（2） + Shift（4）</para>
    /// </summary>
    private volatile User32.KeyModifiers _hotKeyModifiers =
        User32.KeyModifiers.Alt |
        User32.KeyModifiers.Control |
        User32.KeyModifiers.Shift;

    /// <summary>
    /// 喚醒輸入框的修飾鍵組合值。
    /// <para>預設值 7 代表：Alt（1） + Ctrl（2） + Shift（4）</para>
    /// </summary>
    public User32.KeyModifiers HotKeyModifiers
    {
        get => _hotKeyModifiers;
        set => _hotKeyModifiers = value;
    }

    /// <summary>
    /// 喚醒輸入框的主要按鍵（對應 Keys 列舉的字串表示，預設為 "I"）
    /// </summary>
    private volatile string _hotKeyKey = "I";

    /// <summary>
    /// 喚醒輸入框的主要按鍵（對應 Keys 列舉的字串表示，預設為 "I"）
    /// </summary>
    public string HotKeyKey
    {
        get => _hotKeyKey;
        set => _hotKeyKey = value ?? "I";
    }

    #endregion

    #region 震動與回饋設定

    /// <summary>
    /// 是否啟用震動回饋
    /// </summary>
    private volatile bool _enableVibration = true;

    /// <summary>
    /// 是否啟用震動回饋
    /// </summary>
    public bool EnableVibration
    {
        get => _enableVibration;
        set => _enableVibration = value;
    }

    /// <summary>
    /// 全域震動強度倍率
    /// </summary>
    private volatile float _vibrationIntensity = 1.0f;

    /// <summary>
    /// 全域震動強度倍率（0.0 ~ 1.0）
    /// </summary>
    public float VibrationIntensity
    {
        get => _vibrationIntensity;
        set => _vibrationIntensity = Math.Clamp(value, 0.0f, 1.0f);
    }

    #endregion

    #region GamepadController 控制器設定

    /// <summary>
    /// 遊戲手把控制器 API 類型
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
    /// 遊戲手把控制器 API（預設為 XInput）
    /// </summary>
    private volatile GamepadProvider _gamepadProviderType = GamepadProvider.XInput;

    /// <summary>
    /// 遊戲手把控制器 API（預設為 XInput）
    /// </summary>
    public GamepadProvider GamepadProviderType
    {
        get => _gamepadProviderType;
        set => _gamepadProviderType = value;
    }

    /// <summary>
    /// 搖桿死區觸發閾值（Enter）- 預設 7849
    /// </summary>
    private volatile int _thumbDeadzoneEnter = 7849;

    /// <summary>
    /// 取得或設定搖桿死區觸發閾值（Enter）
    /// </summary>
    public int ThumbDeadzoneEnter
    {
        get => _thumbDeadzoneEnter;
        set
        {
            _thumbDeadzoneEnter = Math.Clamp(value, 0, 30000);

            ValidateDeadzone();
        }
    }

    /// <summary>
    /// 搖桿死區重置閾值（Exit）- 預設 2500
    /// </summary>
    private volatile int _thumbDeadzoneExit = 2500;

    /// <summary>
    /// 取得或設定搖桿死區重置閾值（Exit）
    /// </summary>
    public int ThumbDeadzoneExit
    {
        get => _thumbDeadzoneExit;
        set
        {
            _thumbDeadzoneExit = Math.Clamp(value, 0, 30000);

            ValidateDeadzone();
        }
    }

    /// <summary>
    /// 驗證死區設定，確保 Exit 與 Enter 之間有足夠的緩衝區防止抖動。
    /// </summary>
    private void ValidateDeadzone()
    {
        // 防抖機制強制介入：
        // 如果 Exit 設定得太靠近 Enter（兩者差距小於 4000），極易造成搖桿微小抖動時的連點誤判。
        // 此時強制將 Exit 壓低，確保有足夠的遲滯緩衝區（Hysteresis）。
        if (_thumbDeadzoneExit >= _thumbDeadzoneEnter - 4000)
        {
            _thumbDeadzoneExit = Math.Min(2500, _thumbDeadzoneEnter / 2);
        }
    }

    /// <summary>
    /// 長按重複的初始延遲（幀）
    /// </summary>
    private volatile int _repeatInitialDelayFrames = 30;

    /// <summary>
    /// 長按重複的初始延遲（幀）- 預設 30（約 500ms）
    /// </summary>
    public int RepeatInitialDelayFrames
    {
        get => _repeatInitialDelayFrames;
        set => _repeatInitialDelayFrames = Math.Clamp(value, 1, 300);
    }

    /// <summary>
    /// 長按重複的觸發間隔（幀）
    /// </summary>
    private volatile int _repeatIntervalFrames = 5;

    /// <summary>
    /// 長按重複的觸發間隔（幀）- 預設 5（約 80ms）
    /// </summary>
    public int RepeatIntervalFrames
    {
        get => _repeatIntervalFrames;
        set => _repeatIntervalFrames = Math.Clamp(value, 1, 100);
    }

    #endregion

    /// <summary>
    /// 設定檔存取鎖定物件
    /// </summary>
    private static readonly Lock ConfigLock = new();

    /// <summary>
    /// 載入設定
    /// </summary>
    public static void Load()
    {
        bool isInvalid = false;

        lock (ConfigLock)
        {
            // 確保資料夾存在。
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            if (File.Exists(ConfigPath))
            {
                try
                {
                    string strJsonContent;

                    using (FileStream fileStream = new(
                        ConfigPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                    using (StreamReader reader = new(fileStream))
                    {
                        strJsonContent = reader.ReadToEnd();
                    }

                    Current = JsonSerializer.Deserialize<AppSettings>(strJsonContent, Options) ?? new();

                    // 檢查重新序列化後的字串是否與讀取到的不同，不同才存檔。
                    string updatedJsonContent = JsonSerializer.Serialize(Current, Options);

                    if (strJsonContent != updatedJsonContent)
                    {
                        SaveInternal();
                    }
                }
                catch
                {
                    // 讀取失敗時保持預設值，並可考慮是否覆蓋壞掉的檔案。
                    Current = new();

                    isInvalid = true;
                }
            }
            else
            {
                // 檔案不存在時，建立預設設定檔。
                SaveInternal();
            }
        }

        // 警告視窗必須在 Lock 之外彈出，以免阻塞其他執行緒對設定檔的存取。
        if (isInvalid)
        {
            MessageBox.Show(
                Strings.Err_ConfigInvalid,
                caption: Strings.Wrn_Title,
                buttons: MessageBoxButtons.OK,
                icon: MessageBoxIcon.Exclamation);
        }
    }

    /// <summary>
    /// 儲存設定
    /// </summary>
    public static void Save()
    {
        lock (ConfigLock)
        {
            SaveInternal();
        }
    }

    /// <summary>
    /// 內部的儲存實作（不含鎖，由呼叫端控制）
    /// </summary>
    private static void SaveInternal()
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

            // 寫入臨時檔案。
            File.WriteAllText(strTempPath, strJsonContent);

            // 寫入成功後，再原子性地替換原有檔案。
            // 加入退避重試機制，防止被防毒軟體或備份工具短暫鎖定。
            int retries = 3;

            while (retries > 0)
            {
                try
                {
                    File.Move(strTempPath, ConfigPath, true);

                    break;
                }
                catch (IOException) when (retries > 1)
                {
                    retries--;

                    Thread.Sleep(50);
                }
            }
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