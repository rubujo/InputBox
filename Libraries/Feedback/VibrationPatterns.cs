namespace InputBox.Libraries.Feedback;

/// <summary>
/// 預定義的震動模式
/// </summary>
public static class VibrationPatterns
{
    /// <summary>
    /// 全域強度倍率（1.0 = 預設、0.5 = 弱、0.0 = 關閉）
    /// </summary>
    public static float GlobalIntensityMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// 游標移動（輕微短促）
    /// </summary>
    public static readonly VibrationProfile CursorMove = new(8000, 30);

    /// <summary>
    /// 複製成功（強烈且稍長，確認感）
    /// </summary>
    public static readonly VibrationProfile CopySuccess = new(20000, 50);

    /// <summary>
    /// 清除文字方塊（中等強度）
    /// </summary>
    public static readonly VibrationProfile ClearInput = new(10000, 40);

    /// <summary>
    /// 邊界撞擊、錯誤操作（低沈提示）
    /// </summary>
    public static readonly VibrationProfile ActionFail = new(6000, 25);

    /// <summary>
    /// 顯示輸入視窗（喚醒感）
    /// </summary>
    public static readonly VibrationProfile ShowInput = new(12000, 40);

    /// <summary>
    /// 準備切換視窗（開始動作）
    /// </summary>
    public static readonly VibrationProfile ReturnStart = new(5000, 20);

    /// <summary>
    /// 視窗切換完成（結束動作）
    /// </summary>
    public static readonly VibrationProfile ReturnSuccess = new(8000, 30);
}