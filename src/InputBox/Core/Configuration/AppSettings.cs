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

    #region A11y 無障礙與視覺安全閾值

    /// <summary>
    /// 光敏性癲癇安全頻率（毫秒）
    /// <para>根據規範，律動頻率必須鎖定在 1Hz（1000ms），遠低於 3Hz 的風險閾值。</para>
    /// </summary>
    public const int PhotoSafeFrequencyMs = 1000;

    #endregion

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
    /// 遊戲控制器輸入 API 類型
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
    /// 封裝相互關聯的手把設定，用於原子化更新快照
    /// </summary>
    public record GamepadConfigSnapshot(
        int ThumbDeadzoneEnter,
        int ThumbDeadzoneExit,
        int RepeatInitialDelayFrames,
        int RepeatIntervalFrames);

    /// <summary>
    /// 手把設定快照
    /// </summary>
    private volatile GamepadConfigSnapshot _gamepadSettings = new(7849, 2500, 30, 5);

    /// <summary>
    /// 取得手把設定快照
    /// </summary>
    [JsonIgnore]
    public GamepadConfigSnapshot GamepadSettings => _gamepadSettings;

    /// <summary>
    /// 遊戲控制器輸入 API（預設為 XInput）
    /// </summary>
    private volatile GamepadProvider _gamepadProviderType = GamepadProvider.XInput;

    /// <summary>
    /// 遊戲控制器輸入 API
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

            UpdateGamepadSnapshot();
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

            UpdateGamepadSnapshot();
        }
    }

    /// <summary>
    /// 更新手把設定快照，確保背景執行緒讀取到一致的數值組合
    /// </summary>
    private void UpdateGamepadSnapshot()
    {
        lock (ConfigLock)
        {
            // 先進行數值校驗，再同步更新快照。
            // 透過將 Exit 閾值的修正邏輯封裝，確保 Enter 與 Exit 始終符合遲滯（Hysteresis）規範。
            int validatedExit = CalculateValidDeadzoneExit(
                _thumbDeadzoneEnter,
                _thumbDeadzoneExit);

            _thumbDeadzoneExit = validatedExit;

            _gamepadSettings = new GamepadConfigSnapshot(
                _thumbDeadzoneEnter,
                validatedExit,
                _repeatInitialDelayFrames,
                _repeatIntervalFrames);
        }
    }

    /// <summary>
    /// 計算符合遲滯規範的死區重置閾值。
    /// </summary>
    /// <param name="enter">觸發閾值</param>
    /// <param name="exit">原始重置閾值</param>
    /// <returns>修正後的重置閾值</returns>
    private static int CalculateValidDeadzoneExit(int enter, int exit)
    {
        // 防抖機制強化：
        // 使用動態比例計算遲滯（Hysteresis）緩衝空間。
        // 預設取 Enter 值的 30% 作為緩衝，且至少保留 2000 單位（約 XInput 滿程的 6%）。
        int margin = Math.Max(2000, (int)(enter * 0.3f));

        if (exit >= enter - margin)
        {
            return Math.Max(0, enter - margin);
        }

        return exit;
    }

    /// <summary>
    /// 驗證死區設定（相容性方法，內部調用 CalculateValidDeadzoneExit）
    /// </summary>
    private void ValidateDeadzone()
    {
        _thumbDeadzoneExit = CalculateValidDeadzoneExit(
            _thumbDeadzoneEnter,
            _thumbDeadzoneExit);
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
        set
        {
            _repeatInitialDelayFrames = Math.Clamp(value, 1, 300);

            UpdateGamepadSnapshot();
        }
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
        set
        {
            _repeatIntervalFrames = Math.Clamp(value, 1, 100);

            UpdateGamepadSnapshot();
        }
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
                catch (Exception ex)
                {
                    // 讀取失敗時備份損壞檔案，加入退避重試機制以應對檔案鎖定。
                    try
                    {
                        string strBackupPath = ConfigPath + ".bak";

                        int backupRetries = 3;

                        while (backupRetries > 0)
                        {
                            try
                            {
                                File.Move(ConfigPath, strBackupPath, true);

                                break;
                            }
                            catch (IOException) when (backupRetries > 1)
                            {
                                backupRetries--;

                                Thread.Sleep(100);
                            }
                        }

                        Debug.WriteLine($"設定檔損壞，已備份至：{strBackupPath}。錯誤：{ex.Message}");
                    }
                    catch
                    {
                        // 忽略備份失敗。
                    }

                    Current = new();

                    isInvalid = true;
                }
            }
            else
            {
                // 檔案不存在時，建立預設設定檔。
                SaveInternal();
            }

            // 強制校驗死區設定，確保 Exit 與 Enter 之間有足夠的遲滯緩衝區（Hysteresis）。
            Current.ValidateDeadzone();
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