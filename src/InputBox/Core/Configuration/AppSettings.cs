using InputBox.Core.Controls;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Diagnostics;
using System.Text.Encodings.Web;
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
        MaxDepth = 32,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(),
            new FloatingPointFormatConverter()
        }
    };

    /// <summary>
    /// 定義儲存路徑：%AppData%\InputBox
    /// </summary>
    public static readonly string ConfigDirectory = Path.Combine(
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

    /// <summary>
    /// 工作列閃爍的安全上限次數
    /// <para>避免使用無限閃爍造成持續性視覺刺激與操作疲勞。</para>
    /// </summary>
    public const uint TaskbarFlashSafeCount = 3;

    #endregion

    #region 進階系統與效能參數

    /// <summary>
    /// 系統目標物理更新率（60 FPS 對應的毫秒數：16.6）
    /// <para>用於非同步計時器（PeriodicTimer）與連發計算，確保各處頻率一致。</para>
    /// </summary>
    public const double TargetFrameTimeMs = 16.6;

    /// <summary>
    /// 基礎 DPI 縮放值
    /// <para>用於計算高 DPI 環境下的 UI 控制項尺寸與字體縮放。</para>
    /// </summary>
    public const float BaseDpi = 96.0f;

    /// <summary>
    /// 按鈕文字與視覺復原的延遲時間（毫秒）
    /// </summary>
    public const int ButtonResetDelayMs = 1000;

    /// <summary>
    /// Audio Ducking 避讓延遲（毫秒）
    /// <para>廣播前保留此延遲以避開系統音效干擾。</para>
    /// </summary>
    public const int AudioDuckingDelayMs = 200;

    /// <summary>
    /// 單筆歷程記錄與輸入框的最大字數限制
    /// </summary>
    public const int MaxHistoryEntryLength = 10000;

    /// <summary>
    /// 設定檔允許讀取的最大位元組數（1 MB）
    /// </summary>
    public const long MaxConfigFileSizeBytes = 1 * 1024 * 1024;

    #endregion

    #region 視窗與剪貼簿控制參數

    /// <summary>
    /// 切換視窗重試間隔（毫秒）
    /// </summary>
    public const int WindowSwitchRetryDelayMs = 50;

    /// <summary>
    /// 切換視窗最大重試次數
    /// </summary>
    public const int WindowSwitchMaxRetries = 3;

    /// <summary>
    /// 視窗導航時，按鍵放開的檢查頻率（毫秒）
    /// </summary>
    public const int KeyReleaseCheckIntervalMs = 15;

    /// <summary>
    /// 視窗導航時，等待按鍵放開的超時上限（毫秒）
    /// </summary>
    public const int KeyReleaseTimeoutMs = 2000;

    /// <summary>
    /// 剪貼簿操作最大重試次數
    /// </summary>
    public const int ClipboardMaxRetries = 10;

    /// <summary>
    /// 剪貼簿退避重試的延遲上限（毫秒）
    /// </summary>
    public const int ClipboardMaxRetryDelayMs = 200;

    /// <summary>
    /// 寫入剪貼簿後的系統緩衝等待時間（毫秒）
    /// </summary>
    public const int ClipboardBufferDelayMs = 50;

    #endregion

    #region 控制器底層感測參數

    /// <summary>
    /// 裝置重整的冷卻時間（毫秒），過濾掌機虛擬裝置切換造成的連發事件
    /// </summary>
    public const int GamepadRefreshCooldownMs = 500;

    /// <summary>
    /// 斷線重連的降頻計數閾值（幀）
    /// </summary>
    public const int GamepadReconnectThresholdFrames = 30;

    /// <summary>
    /// GameInput 閒置判定閾值
    /// </summary>
    public const float GameInputIdleThreshold = 0.01f;

    /// <summary>
    /// GameInput 活動判定閾值
    /// </summary>
    public const float GameInputActiveThreshold = 0.1f;

    /// <summary>
    /// GameInput 扳機鍵觸發閾值
    /// </summary>
    public const float GameInputTriggerThreshold = 0.12f;

    /// <summary>
    /// XInput 最大支援控制器數量
    /// </summary>
    public const int XInputMaxControllers = 4;

    /// <summary>
    /// XInput 多控制器切換時的搖桿活動閾值
    /// </summary>
    public const short XInputActiveThumbstickThreshold = 8000;

    /// <summary>
    /// XInput 扳機鍵觸發閾值
    /// </summary>
    public const byte XInputTriggerThreshold = 30;

    /// <summary>
    /// 方向輸入防卡住保護幀數（約 400ms）
    /// </summary>
    public const int GamepadDirectionalStuckGuardFrames = 24;

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
    /// 是否允許無障礙廣播中斷前一則訊息（WCAG 2.2.4）
    /// <para>停用後，廣播佇列將以完整排隊模式播報，適合需要完整聆聽所有訊息的使用者。</para>
    /// </summary>
    private volatile bool _a11yInterruptEnabled = true;

    /// <summary>
    /// 是否允許無障礙廣播中斷前一則訊息（WCAG 2.2.4）
    /// </summary>
    public bool A11yInterruptEnabled
    {
        get => _a11yInterruptEnabled;
        set => _a11yInterruptEnabled = value;
    }

    /// <summary>
    /// 是否啟用動畫式視覺警示
    /// <para>預設關閉以優先保護光敏感使用者；關閉時改為一次性靜態脈衝。</para>
    /// </summary>
    private volatile bool _enableAnimatedVisualAlerts = false;

    /// <summary>
    /// 是否啟用動畫式視覺警示
    /// </summary>
    public bool EnableAnimatedVisualAlerts
    {
        get => _enableAnimatedVisualAlerts;
        set => _enableAnimatedVisualAlerts = value;
    }

    /// <summary>
    /// 返回前一個視窗時是否最小化 InputBox
    /// </summary>
    private volatile bool _minimizeOnReturn = false;

    /// <summary>
    /// 返回前一個視窗時是否最小化 InputBox
    /// </summary>
    public bool MinimizeOnReturn
    {
        get => _minimizeOnReturn;
        set => _minimizeOnReturn = value;
    }

    /// <summary>
    /// 視窗不透明度（0.1 ~ 1.0）
    /// </summary>
    private volatile float _windowOpacity = 1.0f;

    /// <summary>
    /// 視窗不透明度（0.1 ~ 1.0）。
    /// 下限設為 10% 以防止視窗完全消失。
    /// 低於 50% 時，UI 層會在套用前向使用者顯示知情警告。
    /// 高對比模式下由 UpdateOpacity() 強制覆寫為 1.0。
    /// </summary>
    public float WindowOpacity
    {
        get => _windowOpacity;
        set => _windowOpacity = Math.Clamp(value, 0.1f, 1.0f);
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
    private volatile float _vibrationIntensity = 0.7f;

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
    /// 封裝相互關聯的控制器設定，用於原子化更新快照
    /// </summary>
    public record GamepadConfigSnapshot(
        int ThumbDeadzoneEnter,
        int ThumbDeadzoneExit,
        int RepeatInitialDelayFrames,
        int RepeatIntervalFrames);

    /// <summary>
    /// 控制器設定快照
    /// </summary>
    private volatile GamepadConfigSnapshot _gamepadSettings = new(7849, 2500, 30, 5);

    /// <summary>
    /// 取得控制器設定快照
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
            lock (ConfigLock)
            {
                _thumbDeadzoneEnter = Math.Clamp(value, 0, 30000);

                UpdateGamepadSnapshot();
            }
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
            lock (ConfigLock)
            {
                _thumbDeadzoneExit = Math.Clamp(value, 0, 30000);

                UpdateGamepadSnapshot();
            }
        }
    }

    /// <summary>
    /// 更新控制器設定快照，確保背景執行緒讀取到一致的數值組合
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

        // 確保 Load() 校驗後快照與欄位值保持一致。
        UpdateGamepadSnapshot();
    }

    /// <summary>
    /// 長按重複的初始延遲（幀）
    /// </summary>
    private volatile int _repeatInitialDelayFrames = 30;

    /// <summary>
    /// 長按重複的初始延遲（幁）- 預設 30（約 500ms）
    /// </summary>
    public int RepeatInitialDelayFrames
    {
        get => _repeatInitialDelayFrames;
        set
        {
            lock (ConfigLock)
            {
                _repeatInitialDelayFrames = Math.Clamp(value, 1, 300);

                UpdateGamepadSnapshot();
            }
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
            lock (ConfigLock)
            {
                _repeatIntervalFrames = Math.Clamp(value, 1, 100);

                UpdateGamepadSnapshot();
            }
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
        string? jsonToSave = null;
        Exception? loadException = null;

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
                    // 拒絕讀取超過大小限制的設定檔，防止畸形 JSON 佔用過多記憶體。
                    if (new FileInfo(ConfigPath).Length > MaxConfigFileSizeBytes)
                    {
                        throw new InvalidDataException($"設定檔超過允許的最大大小（{MaxConfigFileSizeBytes / 1024} KB），拒絕讀取。");
                    }

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

                    // 強制校驗死區設定，確保 Exit 與 Enter 之間有足夠的遲滯緩衝區（Hysteresis）。
                    Current.ValidateDeadzone();

                    // 檢查重新序列化後的字串是否與讀取到的不同，不同才存檔。
                    string updatedJsonContent = JsonSerializer.Serialize(Current, Options);

                    if (strJsonContent != updatedJsonContent)
                    {
                        // 標記需要在鎖外存檔，避免在 ConfigLock 內進行 I/O。
                        jsonToSave = updatedJsonContent;
                    }
                }
                catch (Exception ex)
                {
                    // 捕捉例外，延後至鎖外執行備份與記錄，以免 Thread.Sleep 阻塞 ConfigLock。
                    loadException = ex;

                    Current = new();

                    Current.ValidateDeadzone();

                    isInvalid = true;
                }
            }
            else
            {
                // 檔案不存在時，序列化預設值以供鎖外建立。
                Current.ValidateDeadzone();

                jsonToSave = JsonSerializer.Serialize(Current, Options);
            }
        }

        // 以下操作皆在 ConfigLock 之外進行，Thread.Sleep 不再阻塞鎖。

        // 讀取失敗時備份損壞檔案，加入退避重試機制以應對檔案鎖定。
        if (loadException != null)
        {
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

                Debug.WriteLine($"設定檔損壞，已備份至：{strBackupPath}。錯誤：{loadException.Message}");
            }
            catch (Exception backupEx)
            {
                // 忽略備份失敗，但記錄原因。
                LoggerService.LogException(backupEx, "設定檔備份失敗");
            }

            // 記錄原始損壞原因。
            LoggerService.LogException(loadException, "設定檔讀取或反序列化失敗，將重設為預設值並嘗試修復。");
        }

        // 儲存設定（新建或更新均在鎖外執行）。
        if (jsonToSave != null)
        {
            WriteConfigToFile(jsonToSave);
        }

        // 警告視窗必須在 Lock 之外彈出，以免阻塞其他執行緒對設定檔的存取。
        if (isInvalid)
        {
            GamepadMessageBox.Show(
                null,
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
        // 只在鎖內進行序列化（讀取 Current），I/O 操作在鎖外執行。
        string strJsonContent;

        lock (ConfigLock)
        {
            strJsonContent = JsonSerializer.Serialize(Current, Options);
        }

        WriteConfigToFile(strJsonContent);
    }

    /// <summary>
    /// 將 JSON 字串安全地寫入設定檔（不含鎖，由呼叫端控制）
    /// </summary>
    /// <param name="strJsonContent">已序列化的 JSON 字串</param>
    private static void WriteConfigToFile(string strJsonContent)
    {
        string strTempPath = ConfigPath + ".tmp";

        try
        {
            // 確保資料夾存在。
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

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

                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception ex)
        {
            // 記錄儲存失敗原因。
            LoggerService.LogException(ex, "設定檔儲存失敗（WriteConfigToFile）");

            Debug.WriteLine($"無法儲存設定檔：{ex.Message}");

            // 嘗試清理殘留的臨時檔。
            try
            {
                if (File.Exists(strTempPath))
                {
                    File.Delete(strTempPath);
                }
            }
            catch (Exception cleanupEx)
            {
                Debug.WriteLine($"暫存檔清理失敗，已忽略：{cleanupEx.Message}");
            }
        }
    }
}