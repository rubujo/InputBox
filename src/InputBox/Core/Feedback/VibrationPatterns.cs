namespace InputBox.Core.Feedback;

/// <summary>
/// 觸覺回饋的語意分類。
/// </summary>
internal enum VibrationSemantic
{
    /// <summary>
    /// 一般游標或焦點的細微移動。
    /// </summary>
    CursorMove,

    /// <summary>
    /// 以單字為粒度的快速跳轉。
    /// </summary>
    WordJump,

    /// <summary>
    /// 頁級或區段級的切換。
    /// </summary>
    PageSwitch,

    /// <summary>
    /// 撞牆、越界或失敗警示。
    /// </summary>
    Boundary,

    /// <summary>
    /// 模式狀態切換。
    /// </summary>
    ModeToggle
}

/// <summary>
/// 觸覺回饋所處的 UI 情境。
/// </summary>
internal enum VibrationContext
{
    /// <summary>
    /// 一般導覽情境。
    /// </summary>
    General,

    /// <summary>
    /// 主輸入區的歷程導覽。
    /// </summary>
    History,

    /// <summary>
    /// 片語子選單的翻頁與巡覽。
    /// </summary>
    PhraseMenu,

    /// <summary>
    /// 文字邊界或游標起訖點相關操作。
    /// </summary>
    TextBoundary,

    /// <summary>
    /// 隱私模式切換相關操作。
    /// </summary>
    PrivacyMode
}

/// <summary>
/// 控制器組合鍵進入保留或修飾狀態時的提示類型。
/// </summary>
internal enum GamepadComboCueKind
{
    /// <summary>
    /// 雙肩鍵組合的預備提示。
    /// </summary>
    ShoulderChord,

    /// <summary>
    /// 雙板機組合的預備提示。
    /// </summary>
    TriggerChord,

    /// <summary>
    /// Back 作為修飾鍵時的系統控制提示。
    /// </summary>
    SystemModifier
}

/// <summary>
/// 預定義的震動模式。
/// </summary>
public static class VibrationPatterns
{
    /// <summary>
    /// 全域強度倍率（0.7 = 預設、0.5 = 弱、0.0 = 關閉）。
    /// </summary>
    /// <remarks>
    /// 以 volatile 欄位支撐，確保 UI 執行緒寫入對震動輪詢執行緒的記憶體可見性。
    /// </remarks>
    private static volatile float _globalIntensityMultiplier = 0.7f;

    /// <summary>
    /// 取得或設定全域震動強度倍率。
    /// <para>0.7 = 預設；0.5 = 弱；0.0 = 關閉。更改後，後續所有震動呼叫均會套用新值。</para>
    /// </summary>
    public static float GlobalIntensityMultiplier
    {
        get => _globalIntensityMultiplier;
        set => _globalIntensityMultiplier = value;
    }

    /// <summary>
    /// 游標移動（輕微短促的點擊感）。
    /// </summary>
    public static readonly VibrationProfile CursorMove = new(18000, 50);

    /// <summary>
    /// 向左移動游標時的細緻微震動。
    /// </summary>
    public static readonly VibrationProfile CursorMoveLeft = new(14000, 24, 0.30f, 0.08f, 0.92f, 0.03f);

    /// <summary>
    /// 向右移動游標時的細緻微震動。
    /// </summary>
    public static readonly VibrationProfile CursorMoveRight = new(14000, 24, 0.08f, 0.30f, 0.03f, 0.92f);

    /// <summary>
    /// 左向游標微震後的細小收尾。
    /// </summary>
    public static readonly VibrationProfile CursorMoveLeftEcho = new(7000, 12, 0.40f, 0.10f, 0.22f, 0.03f);

    /// <summary>
    /// 右向游標微震後的細小收尾。
    /// </summary>
    public static readonly VibrationProfile CursorMoveRightEcho = new(7000, 12, 0.10f, 0.40f, 0.03f, 0.22f);

    /// <summary>
    /// 分頁切換（比游標移動更明確的翻頁感）。
    /// </summary>
    public static readonly VibrationProfile PageSwitch = new(26000, 80);

    /// <summary>
    /// 向左／向前的方向性翻頁震動。
    /// </summary>
    public static readonly VibrationProfile PageSwitchLeft = new(25000, 74, 1.0f, 0.32f, 0.68f, 0.12f);

    /// <summary>
    /// 向右／向後的方向性翻頁震動。
    /// </summary>
    public static readonly VibrationProfile PageSwitchRight = new(25000, 74, 0.32f, 1.0f, 0.12f, 0.68f);

    /// <summary>
    /// 主輸入區歷程向前翻頁時的觸覺樣式。
    /// </summary>
    public static readonly VibrationProfile HistoryPageBackward = new(22000, 64, 1.0f, 0.38f, 0.46f, 0.06f);

    /// <summary>
    /// 主輸入區歷程向後翻頁時的觸覺樣式。
    /// </summary>
    public static readonly VibrationProfile HistoryPageForward = new(22000, 64, 0.38f, 1.0f, 0.06f, 0.46f);

    /// <summary>
    /// 歷程向前翻頁時的第二段機械收尾。
    /// </summary>
    public static readonly VibrationProfile HistoryPageBackwardSettle = new(9500, 18, 0.68f, 0.14f, 0.16f, 0.02f);

    /// <summary>
    /// 歷程向後翻頁時的第二段機械收尾。
    /// </summary>
    public static readonly VibrationProfile HistoryPageForwardSettle = new(9500, 18, 0.14f, 0.68f, 0.02f, 0.16f);

    /// <summary>
    /// 單筆歷程導覽的精緻滾輪刻度感（向前／較舊）。
    /// </summary>
    public static readonly VibrationProfile HistoryWheelBackward = new(18500, 26, 1.0f, 0.34f, 0.30f, 0.04f);

    /// <summary>
    /// 單筆歷程導覽的精緻滾輪刻度感（向後／較新）。
    /// </summary>
    public static readonly VibrationProfile HistoryWheelForward = new(18500, 26, 0.34f, 1.0f, 0.04f, 0.30f);

    /// <summary>
    /// 快速翻閱歷程時較輕的阻尼刻度感（向前／較舊）。
    /// </summary>
    public static readonly VibrationProfile HistoryWheelBackwardFast = new(13500, 18, 0.92f, 0.26f, 0.18f, 0.03f);

    /// <summary>
    /// 快速翻閱歷程時較輕的阻尼刻度感（向後／較新）。
    /// </summary>
    public static readonly VibrationProfile HistoryWheelForwardFast = new(13500, 18, 0.26f, 0.92f, 0.03f, 0.18f);

    /// <summary>
    /// 極高速掃描歷程時的滑順餘韻（向前／較舊）。
    /// </summary>
    public static readonly VibrationProfile HistoryWheelBackwardGlide = new(9000, 12, 0.74f, 0.18f, 0.12f, 0.02f);

    /// <summary>
    /// 極高速掃描歷程時的滑順餘韻（向後／較新）。
    /// </summary>
    public static readonly VibrationProfile HistoryWheelForwardGlide = new(9000, 12, 0.18f, 0.74f, 0.02f, 0.12f);

    /// <summary>
    /// 片語子選單翻頁時的較強觸覺樣式。
    /// </summary>
    public static readonly VibrationProfile PhrasePageBackward = new(27000, 82, 1.0f, 0.20f, 0.72f, 0.06f);

    /// <summary>
    /// 片語子選單向後翻頁時的較強觸覺樣式。
    /// </summary>
    public static readonly VibrationProfile PhrasePageForward = new(27000, 82, 0.20f, 1.0f, 0.06f, 0.72f);

    /// <summary>
    /// 片語子選單向前翻頁時的較清脆收尾。
    /// </summary>
    public static readonly VibrationProfile PhrasePageBackwardSettle = new(12500, 20, 0.74f, 0.12f, 0.24f, 0.03f);

    /// <summary>
    /// 片語子選單向後翻頁時的較清脆收尾。
    /// </summary>
    public static readonly VibrationProfile PhrasePageForwardSettle = new(12500, 20, 0.12f, 0.74f, 0.03f, 0.24f);

    /// <summary>
    /// 單字跳轉的左向首脈衝。
    /// </summary>
    public static readonly VibrationProfile WordJumpLeftPrimary = new(21000, 24, 0.42f, 0.10f, 1.0f, 0.04f);

    /// <summary>
    /// 單字跳轉的左向第二脈衝。
    /// </summary>
    public static readonly VibrationProfile WordJumpLeftSecondary = new(11500, 14, 0.56f, 0.12f, 0.15f, 0.02f);

    /// <summary>
    /// 單字跳轉的右向首脈衝。
    /// </summary>
    public static readonly VibrationProfile WordJumpRightPrimary = new(21000, 24, 0.10f, 0.42f, 0.04f, 1.0f);

    /// <summary>
    /// 單字跳轉的右向第二脈衝。
    /// </summary>
    public static readonly VibrationProfile WordJumpRightSecondary = new(11500, 14, 0.12f, 0.56f, 0.02f, 0.15f);

    /// <summary>
    /// 複製成功（強烈且明確的確認感）。
    /// </summary>
    public static readonly VibrationProfile CopySuccess = new(40000, 150);

    /// <summary>
    /// 清除文字方塊（中等強度的提示）。
    /// </summary>
    public static readonly VibrationProfile ClearInput = new(25000, 100);

    /// <summary>
    /// 邊界撞擊、錯誤操作（強烈且較長的警告）。
    /// </summary>
    public static readonly VibrationProfile ActionFail = new(45000, 200);

    /// <summary>
    /// 向左撞到邊界時的方向性警示震動。
    /// </summary>
    public static readonly VibrationProfile BoundaryLeft = new(37000, 104, 1.0f, 0.28f, 0.80f, 0.08f);

    /// <summary>
    /// 向右撞到邊界時的方向性警示震動。
    /// </summary>
    public static readonly VibrationProfile BoundaryRight = new(37000, 104, 0.28f, 1.0f, 0.08f, 0.80f);

    /// <summary>
    /// 長按撞牆時的左向柔和重複回饋，用於降低疲勞感。
    /// </summary>
    public static readonly VibrationProfile BoundaryLeftRepeat = new(22000, 42, 0.74f, 0.22f, 0.24f, 0.04f);

    /// <summary>
    /// 長按撞牆時的右向柔和重複回饋，用於降低疲勞感。
    /// </summary>
    public static readonly VibrationProfile BoundaryRightRepeat = new(22000, 42, 0.22f, 0.74f, 0.04f, 0.24f);

    /// <summary>
    /// 左向邊界碰撞後的短餘震。
    /// </summary>
    public static readonly VibrationProfile BoundaryLeftAftershock = new(12000, 16, 0.62f, 0.14f, 0.10f, 0.02f);

    /// <summary>
    /// 右向邊界碰撞後的短餘震。
    /// </summary>
    public static readonly VibrationProfile BoundaryRightAftershock = new(12000, 16, 0.14f, 0.62f, 0.02f, 0.10f);

    /// <summary>
    /// 文字邊界跳轉成功時的起點方向提示。
    /// </summary>
    public static readonly VibrationProfile CursorJumpStart = new(20000, 55, 1.0f, 0.40f, 0.30f, 0.10f);

    /// <summary>
    /// 文字邊界跳轉成功時的終點方向提示。
    /// </summary>
    public static readonly VibrationProfile CursorJumpEnd = new(20000, 55, 0.40f, 1.0f, 0.10f, 0.30f);

    /// <summary>
    /// 隱私模式切換時的獨立語意震動。
    /// </summary>
    public static readonly VibrationProfile PrivacyModeToggle = new(28000, 140, 0.65f, 0.65f, 1.0f, 1.0f);

    /// <summary>
    /// 隱私模式切換時的鎖定脈衝。
    /// </summary>
    public static readonly VibrationProfile PrivacyModeToggleLatch = new(25000, 42, 0.44f, 0.44f, 1.0f, 1.0f);

    /// <summary>
    /// 隱私模式切換時的回穩收尾。
    /// </summary>
    public static readonly VibrationProfile PrivacyModeToggleSettle = new(12500, 48, 0.20f, 0.20f, 0.46f, 0.46f);

    /// <summary>
    /// 隱私模式關閉時的解鎖脈衝。
    /// </summary>
    public static readonly VibrationProfile PrivacyModeReleaseLatch = new(16500, 30, 0.62f, 0.62f, 0.28f, 0.28f);

    /// <summary>
    /// 隱私模式關閉時的鬆開收尾。
    /// </summary>
    public static readonly VibrationProfile PrivacyModeReleaseSettle = new(8500, 32, 0.16f, 0.16f, 0.18f, 0.18f);

    /// <summary>
    /// 雙肩鍵保留成功時的短促預備脈衝。
    /// </summary>
    public static readonly VibrationProfile ShoulderComboArm = new(16000, 26, 0.74f, 0.74f, 0.30f, 0.30f);

    /// <summary>
    /// 雙肩鍵保留後的穩定收尾，提示後續可接 B／X 動作。
    /// </summary>
    public static readonly VibrationProfile ShoulderComboSettle = new(9000, 16, 0.28f, 0.28f, 0.10f, 0.10f);

    /// <summary>
    /// 雙板機組合進入模式切換前的扳機導向提示。
    /// </summary>
    public static readonly VibrationProfile TriggerComboArm = new(17500, 24, 0.16f, 0.16f, 1.0f, 1.0f);

    /// <summary>
    /// 雙板機組合的短收尾，降低與正式模式切換主震動之間的黏連感。
    /// </summary>
    public static readonly VibrationProfile TriggerComboSettle = new(8500, 14, 0.12f, 0.12f, 0.34f, 0.34f);

    /// <summary>
    /// Back 修飾鍵進入系統控制語意時的輕量提示。
    /// </summary>
    public static readonly VibrationProfile SystemModifierArm = new(12000, 18, 0.44f, 0.44f, 0.12f, 0.12f);

    /// <summary>
    /// 顯示輸入視窗（明確的喚醒感）。
    /// </summary>
    public static readonly VibrationProfile ShowInput = new(35000, 120);

    /// <summary>
    /// 準備切換視窗（開始動作）。
    /// </summary>
    public static readonly VibrationProfile ReturnStart = new(20000, 80);

    /// <summary>
    /// 視窗切換完成（結束動作）。
    /// </summary>
    public static readonly VibrationProfile ReturnSuccess = new(30000, 120);

    /// <summary>
    /// 控制器已連線（觸覺握手）。
    /// </summary>
    public static readonly VibrationProfile ControllerConnected = new(30000, 200);

    /// <summary>
    /// 調整震動強度後的即時預覽脈衝，維持中等強度與短時間以避免突兀。
    /// </summary>
    public static readonly VibrationProfile IntensityPreview = new(26000, 75, 0.72f, 0.72f, 0.30f, 0.30f);

    /// <summary>
    /// 一般設定切換為「啟用」時的確認脈衝（對稱馬達，不含方向語意）。
    /// <para>強度介於游標移動與清除輸入之間，代表一個明確但非破壞性的狀態確認。</para>
    /// </summary>
    public static readonly VibrationProfile SettingToggleOn = new(22000, 55, 0.62f, 0.62f, 0.50f, 0.50f);

    /// <summary>
    /// 一般設定切換為「停用」時的確認脈衝（對稱馬達，強度比 On 輕以反映移除語意）。
    /// </summary>
    public static readonly VibrationProfile SettingToggleOff = new(15000, 38, 0.48f, 0.48f, 0.22f, 0.22f);

    /// <summary>
    /// 識別目前控制器時的第一拍定位脈衝。
    /// </summary>
    public static readonly VibrationProfile ControllerIdentifyLead = new(24000, 44, 1.0f, 0.34f, 0.26f, 0.08f);

    /// <summary>
    /// 識別目前控制器時的第二拍回應脈衝。
    /// </summary>
    public static readonly VibrationProfile ControllerIdentifyTail = new(32000, 78, 0.34f, 1.0f, 0.08f, 0.26f);

    /// <summary>
    /// 長按結束程式時的預備提示。
    /// </summary>
    public static readonly VibrationProfile ExitHoldArm = new(18000, 34, 0.56f, 0.56f, 0.18f, 0.18f);

    /// <summary>
    /// 長按結束程式仍在持續時的進度提示。
    /// </summary>
    public static readonly VibrationProfile ExitHoldProgress = new(23000, 26, 0.42f, 0.42f, 0.10f, 0.10f);

    /// <summary>
    /// 長按結束程式完成時的明確確認震動。
    /// </summary>
    public static readonly VibrationProfile ExitHoldConfirm = new(36000, 92, 0.82f, 0.82f, 0.36f, 0.36f);

    /// <summary>
    /// 接近文字上限時的第一段預警。
    /// </summary>
    public static readonly VibrationProfile TextLimitApproach = new(17000, 22, 0.55f, 0.55f, 0.18f, 0.18f);

    /// <summary>
    /// 接近文字上限時的急促預警。
    /// </summary>
    public static readonly VibrationProfile TextLimitApproachUrgent = new(22000, 18, 0.66f, 0.66f, 0.36f, 0.36f);

    /// <summary>
    /// 幾乎撞到文字上限時的高密度臨界預警。
    /// </summary>
    public static readonly VibrationProfile TextLimitApproachCritical = new(26000, 16, 0.78f, 0.78f, 0.58f, 0.58f);

    /// <summary>
    /// 已達文字上限時的沉重硬牆震動。
    /// </summary>
    public static readonly VibrationProfile TextLimitWall = new(41000, 110, 0.92f, 0.92f, 0.30f, 0.30f);

    /// <summary>
    /// 文字硬牆後的短暫死板回震。
    /// </summary>
    public static readonly VibrationProfile TextLimitWallSettle = new(16000, 28, 0.34f, 0.34f, 0.08f, 0.08f);

    /// <summary>
    /// 逐字選取時的細緻拉鏈感（向左）。
    /// </summary>
    public static readonly VibrationProfile SelectionZipLeft = new(10000, 10, 0.20f, 0.08f, 1.0f, 0.02f);

    /// <summary>
    /// 逐字選取時的細緻拉鏈感（向右）。
    /// </summary>
    public static readonly VibrationProfile SelectionZipRight = new(10000, 10, 0.08f, 0.20f, 0.02f, 1.0f);

    /// <summary>
    /// 快速逐字選取時更緊實的拉鏈感（向左）。
    /// </summary>
    public static readonly VibrationProfile SelectionZipLeftFast = new(8000, 8, 0.18f, 0.06f, 0.80f, 0.02f);

    /// <summary>
    /// 快速逐字選取時更緊實的拉鏈感（向右）。
    /// </summary>
    public static readonly VibrationProfile SelectionZipRightFast = new(8000, 8, 0.06f, 0.18f, 0.02f, 0.80f);

    /// <summary>
    /// 單字粒度選取時的方向性確認脈衝（向左）。
    /// </summary>
    public static readonly VibrationProfile SelectionWordLeft = new(19000, 22, 0.48f, 0.12f, 0.80f, 0.08f);

    /// <summary>
    /// 單字粒度選取時的方向性確認脈衝（向右）。
    /// </summary>
    public static readonly VibrationProfile SelectionWordRight = new(19000, 22, 0.12f, 0.48f, 0.08f, 0.80f);

    /// <summary>
    /// 喚起主輸入框時的科技感握手第一拍。
    /// </summary>
    public static readonly VibrationProfile FocusHandshakeLead = new(18000, 20, 0.60f, 0.60f, 0.20f, 0.20f);

    /// <summary>
    /// 喚起主輸入框時的科技感握手第二拍。
    /// </summary>
    public static readonly VibrationProfile FocusHandshakeMid = new(22000, 24, 0.30f, 0.30f, 0.72f, 0.72f);

    /// <summary>
    /// 喚起主輸入框時的科技感握手第三拍。
    /// </summary>
    public static readonly VibrationProfile FocusHandshakeTail = new(14000, 18, 0.70f, 0.70f, 0.12f, 0.12f);

    /// <summary>
    /// 取得柔和版的文字邊界重複回饋。
    /// </summary>
    internal static VibrationProfile GetRepeatedBoundaryProfile(int direction)
        => direction < 0 ? BoundaryLeftRepeat : BoundaryRightRepeat;

    /// <summary>
    /// 取得用於識別目前作用中控制器的震動序列。
    /// </summary>
    internal static IReadOnlyList<VibrationSequenceStep> GetControllerIdentifySequence(
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
            [new VibrationSequenceStep(ControllerConnected, 24), new VibrationSequenceStep(PageSwitch)] :
            [new VibrationSequenceStep(ControllerIdentifyLead, 36), new VibrationSequenceStep(ControllerIdentifyTail)];
    }

    /// <summary>
    /// 取得結束程式長按保護的觸覺序列。
    /// </summary>
    internal static IReadOnlyList<VibrationSequenceStep> GetExitHoldSequence(
        bool confirmed,
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        if (confirmed)
        {
            return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
                [new VibrationSequenceStep(ReturnSuccess)] :
                [new VibrationSequenceStep(ExitHoldConfirm, 10), new VibrationSequenceStep(ReturnSuccess)];
        }

        return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
            [new VibrationSequenceStep(ExitHoldArm)] :
            [new VibrationSequenceStep(ExitHoldArm, 260), new VibrationSequenceStep(ExitHoldProgress)];
    }

    /// <summary>
    /// 取得歷程快速翻閱時的阻尼滾輪震動序列。
    /// </summary>
    internal static IReadOnlyList<VibrationSequenceStep> GetHistoryScrollSequence(
        int direction,
        int burstLevel,
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        int normalizedDirection = direction < 0 ? -1 : 1;
        int clampedBurstLevel = Math.Clamp(burstLevel, 0, 3);

        VibrationProfile primary = clampedBurstLevel switch
        {
            0 => normalizedDirection < 0 ? HistoryWheelBackward : HistoryWheelForward,
            1 or 2 => normalizedDirection < 0 ? HistoryWheelBackwardFast : HistoryWheelForwardFast,
            _ => normalizedDirection < 0 ? HistoryWheelBackwardGlide : HistoryWheelForwardGlide
        };

        VibrationProfile settle = clampedBurstLevel >= 2 ?
            (normalizedDirection < 0 ? CursorMoveLeftEcho : CursorMoveRightEcho) :
            (normalizedDirection < 0 ? HistoryPageBackwardSettle : HistoryPageForwardSettle);

        return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 || clampedBurstLevel >= 2 ?
            [new VibrationSequenceStep(primary)] :
            [new VibrationSequenceStep(primary, 4), new VibrationSequenceStep(settle)];
    }

    /// <summary>
    /// 依剩餘字元數回傳接近上限或撞到上限時的觸覺序列。
    /// </summary>
    internal static IReadOnlyList<VibrationSequenceStep> GetTextLimitSequence(
        int remainingCharacters,
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        if (remainingCharacters > 10)
        {
            return [];
        }

        if (remainingCharacters <= 0)
        {
            return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
                [new VibrationSequenceStep(TextLimitWall)] :
                [new VibrationSequenceStep(TextLimitWall, 14), new VibrationSequenceStep(TextLimitWallSettle)];
        }

        if (remainingCharacters <= 2)
        {
            return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
                [new VibrationSequenceStep(TextLimitApproachCritical)] :
                [new VibrationSequenceStep(TextLimitApproachCritical, 10), new VibrationSequenceStep(TextLimitApproachCritical)];
        }

        if (remainingCharacters <= 5)
        {
            return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
                [new VibrationSequenceStep(TextLimitApproachUrgent)] :
                [new VibrationSequenceStep(TextLimitApproachUrgent, 8), new VibrationSequenceStep(TextLimitApproach)];
        }

        return [new VibrationSequenceStep(TextLimitApproach)];
    }

    /// <summary>
    /// 依選取粒度與速度回傳右搖桿文字選取的觸覺序列。
    /// </summary>
    internal static IReadOnlyList<VibrationSequenceStep> GetSelectionSequence(
        int direction,
        bool wordGranularity,
        int burstLevel = 0,
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        int normalizedDirection = direction < 0 ? -1 : 1;
        int clampedBurstLevel = Math.Clamp(burstLevel, 0, 3);

        if (wordGranularity)
        {
            VibrationProfile wordPrimary = normalizedDirection < 0 ? SelectionWordLeft : SelectionWordRight;
            VibrationProfile wordSettle = normalizedDirection < 0 ? WordJumpLeftSecondary : WordJumpRightSecondary;

            return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
                [new VibrationSequenceStep(wordPrimary)] :
                [new VibrationSequenceStep(wordPrimary, 8), new VibrationSequenceStep(wordSettle)];
        }

        VibrationProfile charPrimary = clampedBurstLevel >= 2 ?
            (normalizedDirection < 0 ? SelectionZipLeftFast : SelectionZipRightFast) :
            (normalizedDirection < 0 ? SelectionZipLeft : SelectionZipRight);
        VibrationProfile charSettle = normalizedDirection < 0 ? CursorMoveLeftEcho : CursorMoveRightEcho;

        return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 || clampedBurstLevel >= 2 ?
            [new VibrationSequenceStep(charPrimary)] :
            [new VibrationSequenceStep(charPrimary, 2), new VibrationSequenceStep(charSettle)];
    }

    /// <summary>
    /// 取得由全域快速鍵喚起主視窗時的輕巧握手震動。
    /// </summary>
    internal static IReadOnlyList<VibrationSequenceStep> GetFocusHandshakeSequence(
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        return (motorSupport & VibrationMotorSupport.TriggerMotors) == 0 ?
            [new VibrationSequenceStep(CursorMove), new VibrationSequenceStep(CursorMove), new VibrationSequenceStep(CursorMove)] :
            [new VibrationSequenceStep(FocusHandshakeLead, 10), new VibrationSequenceStep(FocusHandshakeMid, 8), new VibrationSequenceStep(FocusHandshakeTail)];
    }

    /// <summary>
    /// 依語意、方向與情境取得集中管理的方向性震動樣式。
    /// </summary>
    internal static VibrationProfile GetNavigationProfile(
        VibrationSemantic semantic,
        int direction,
        VibrationContext context = VibrationContext.General)
    {
        int normalizedDirection = direction < 0 ? -1 : 1;

        return semantic switch
        {
            VibrationSemantic.CursorMove => normalizedDirection < 0 ? CursorMoveLeft : CursorMoveRight,
            VibrationSemantic.WordJump => normalizedDirection < 0 ? WordJumpLeftPrimary : WordJumpRightPrimary,
            VibrationSemantic.PageSwitch => context switch
            {
                VibrationContext.History => normalizedDirection < 0 ? HistoryPageBackward : HistoryPageForward,
                VibrationContext.PhraseMenu => normalizedDirection < 0 ? PhrasePageBackward : PhrasePageForward,
                VibrationContext.TextBoundary => normalizedDirection < 0 ? CursorJumpStart : CursorJumpEnd,
                _ => normalizedDirection < 0 ? PageSwitchLeft : PageSwitchRight
            },
            VibrationSemantic.Boundary => normalizedDirection < 0 ? BoundaryLeft : BoundaryRight,
            VibrationSemantic.ModeToggle => normalizedDirection < 0 ? PrivacyModeReleaseLatch : PrivacyModeToggle,
            _ => CursorMove
        };
    }

    /// <summary>
    /// 依組合鍵家族回傳進入保留或修飾狀態時的短提示序列，讓肩鍵、板機與 Back 修飾鍵有明確區隔。
    /// </summary>
    /// <param name="kind">要產生提示序列的組合鍵類型。</param>
    /// <param name="motorSupport">目前控制器可支援的震動馬達能力。</param>
    /// <returns>對應情境的短提示震動序列。</returns>
    internal static IReadOnlyList<VibrationSequenceStep> GetComboCueSequence(
        GamepadComboCueKind kind,
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        // 有扳機馬達時可使用更鮮明的前後拍層次；否則退化為雙主馬達可辨識版本。
        bool supportsTriggers = (motorSupport & VibrationMotorSupport.TriggerMotors) != 0;

        return kind switch
        {
            GamepadComboCueKind.ShoulderChord when supportsTriggers =>
                [new VibrationSequenceStep(ShoulderComboArm, 6), new VibrationSequenceStep(ShoulderComboSettle)],

            GamepadComboCueKind.TriggerChord when supportsTriggers =>
                [new VibrationSequenceStep(TriggerComboArm, 5), new VibrationSequenceStep(TriggerComboSettle)],

            GamepadComboCueKind.SystemModifier when supportsTriggers =>
                [new VibrationSequenceStep(SystemModifierArm)],

            GamepadComboCueKind.ShoulderChord =>
                [new VibrationSequenceStep(new VibrationProfile(ShoulderComboArm.Strength, ShoulderComboArm.Duration, 0.65f, 0.65f, 0f, 0f))],

            GamepadComboCueKind.TriggerChord =>
                [new VibrationSequenceStep(new VibrationProfile(TriggerComboArm.Strength, TriggerComboArm.Duration, 0.30f, 0.30f, 0f, 0f))],

            _ => [new VibrationSequenceStep(new VibrationProfile(SystemModifierArm.Strength, SystemModifierArm.Duration, 0.40f, 0.40f, 0f, 0f))]
        };
    }

    /// <summary>
    /// 依控制器能力回傳最佳化的多段式觸覺序列；不支援扳機馬達時會安全退化為單段樣式。
    /// </summary>
    internal static IReadOnlyList<VibrationSequenceStep> GetNavigationSequence(
        VibrationSemantic semantic,
        int direction,
        VibrationContext context = VibrationContext.General,
        VibrationMotorSupport motorSupport = VibrationMotorSupport.DualMain)
    {
        int normalizedDirection = direction < 0 ? -1 : 1;

        if ((motorSupport & VibrationMotorSupport.TriggerMotors) == 0)
        {
            return [new VibrationSequenceStep(GetNavigationProfile(semantic, normalizedDirection, context))];
        }

        return semantic switch
        {
            VibrationSemantic.CursorMove => normalizedDirection < 0 ?
                [new VibrationSequenceStep(CursorMoveLeft, 6), new VibrationSequenceStep(CursorMoveLeftEcho)] :
                [new VibrationSequenceStep(CursorMoveRight, 6), new VibrationSequenceStep(CursorMoveRightEcho)],

            VibrationSemantic.WordJump => normalizedDirection < 0 ?
                [new VibrationSequenceStep(WordJumpLeftPrimary, 10), new VibrationSequenceStep(WordJumpLeftSecondary)] :
                [new VibrationSequenceStep(WordJumpRightPrimary, 10), new VibrationSequenceStep(WordJumpRightSecondary)],

            VibrationSemantic.PageSwitch => context switch
            {
                VibrationContext.History => normalizedDirection < 0 ?
                    [new VibrationSequenceStep(HistoryPageBackward, 8), new VibrationSequenceStep(HistoryPageBackwardSettle)] :
                    [new VibrationSequenceStep(HistoryPageForward, 8), new VibrationSequenceStep(HistoryPageForwardSettle)],

                VibrationContext.PhraseMenu => normalizedDirection < 0 ?
                    [new VibrationSequenceStep(PhrasePageBackward, 8), new VibrationSequenceStep(PhrasePageBackwardSettle)] :
                    [new VibrationSequenceStep(PhrasePageForward, 8), new VibrationSequenceStep(PhrasePageForwardSettle)],

                VibrationContext.TextBoundary => normalizedDirection < 0 ?
                    [new VibrationSequenceStep(CursorJumpStart, 8), new VibrationSequenceStep(CursorMoveLeftEcho)] :
                    [new VibrationSequenceStep(CursorJumpEnd, 8), new VibrationSequenceStep(CursorMoveRightEcho)],

                _ => normalizedDirection < 0 ?
                    [new VibrationSequenceStep(PageSwitchLeft, 8), new VibrationSequenceStep(CursorMoveLeftEcho)] :
                    [new VibrationSequenceStep(PageSwitchRight, 8), new VibrationSequenceStep(CursorMoveRightEcho)]
            },

            VibrationSemantic.Boundary => normalizedDirection < 0 ?
                [new VibrationSequenceStep(BoundaryLeft, 10), new VibrationSequenceStep(BoundaryLeftAftershock)] :
                [new VibrationSequenceStep(BoundaryRight, 10), new VibrationSequenceStep(BoundaryRightAftershock)],

            VibrationSemantic.ModeToggle => normalizedDirection < 0 ?
                [new VibrationSequenceStep(PrivacyModeReleaseLatch, 12), new VibrationSequenceStep(PrivacyModeReleaseSettle)] :
                [new VibrationSequenceStep(PrivacyModeToggleLatch, 14), new VibrationSequenceStep(PrivacyModeToggleSettle)],

            _ => [new VibrationSequenceStep(CursorMove)]
        };
    }
}