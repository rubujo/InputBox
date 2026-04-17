namespace InputBox.Core.Feedback;

/// <summary>
/// 預定義的震動模式
/// </summary>
public static class VibrationPatterns
{
    /// <summary>
    /// 全域強度倍率（0.7 = 預設、0.5 = 弱、0.0 = 關閉）
    /// </summary>
    /// <remarks>
    /// 以 volatile 欄位支撐，確保 UI 執行緒寫入對震動輪詢執行緒的記憶體可見性。
    /// </remarks>
    private static volatile float _globalIntensityMultiplier = 0.7f;

    public static float GlobalIntensityMultiplier
    {
        get => _globalIntensityMultiplier;
        set => _globalIntensityMultiplier = value;
    }

    /// <summary>
    /// 游標移動（輕微短促的點擊感）
    /// 說明：強度足以克服馬達啟動閾值，時間短暫以模擬「滴答」聲，避免連按時糊在一起。
    /// </summary>
    public static readonly VibrationProfile CursorMove = new(18000, 50);

    /// <summary>
    /// 分頁切換（比游標移動更明確的翻頁感）
    /// 說明：略強於一般游標移動，讓使用者能清楚感知已切換到另一頁，但不至於干擾連續導覽。
    /// </summary>
    public static readonly VibrationProfile PageSwitch = new(26000, 80);

    /// <summary>
    /// 複製成功（強烈且明確的確認感）
    /// 說明：提供約 60% 的強度與 150ms 的持續時間，給予使用者明確「任務已完成」的安全感。
    /// </summary>
    public static readonly VibrationProfile CopySuccess = new(40000, 150);

    /// <summary>
    /// 清除文字方塊（中等強度的提示）
    /// 說明：比游標移動強烈，但不及複製成功，表示狀態重置。
    /// </summary>
    public static readonly VibrationProfile ClearInput = new(25000, 100);

    /// <summary>
    /// 邊界撞擊、錯誤操作（強烈且較長的警告）
    /// 說明：約 68% 強度與 200ms 長時間，打破原本的節奏確保操作錯誤不被忽視，
    /// 同時避免對感覺敏感或有關節疾患的使用者造成不適。
    /// </summary>
    public static readonly VibrationProfile ActionFail = new(45000, 200);

    /// <summary>
    /// 顯示輸入視窗（明確的喚醒感）
    /// 說明：中高強度，用以告知 UI 焦點已發生重大轉移並準備好接收輸入。
    /// </summary>
    public static readonly VibrationProfile ShowInput = new(35000, 120);

    /// <summary>
    /// 準備切換視窗（開始動作）
    /// 說明：起步階段給予中輕度提示，作為動作的開端。
    /// </summary>
    public static readonly VibrationProfile ReturnStart = new(20000, 80);

    /// <summary>
    /// 視窗切換完成（結束動作）
    /// 說明：稍微加強並延長，與 ReturnStart 組合形成「起->落」的完整切換體驗。
    /// </summary>
    public static readonly VibrationProfile ReturnSuccess = new(30000, 120);

    /// <summary>
    /// 控制器已連線（觸覺握手）
    /// 說明：中等強度的短脈衝，確認控制器與應用程式之間的通訊已建立。
    /// 對視聽雙障的使用者而言，這可能是唯一能感知「控制器已被識別」的通道。
    /// </summary>
    public static readonly VibrationProfile ControllerConnected = new(30000, 200);
}