namespace InputBox.Core.Input;

/// <summary>
/// 遊戲控制器校準視覺化所需的唯讀診斷快照。
/// </summary>
internal sealed record GamepadCalibrationSnapshot
{
    /// <summary>
    /// 空白快照，用於未連線或尚未初始化時。
    /// </summary>
    public static GamepadCalibrationSnapshot Empty { get; } = new();

    /// <summary>
    /// 目前是否已連線。
    /// </summary>
    public bool IsConnected { get; init; }

    /// <summary>
    /// 左搖桿原始 X 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float RawLeftX { get; init; }

    /// <summary>
    /// 左搖桿原始 Y 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float RawLeftY { get; init; }

    /// <summary>
    /// 右搖桿原始 X 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float RawRightX { get; init; }

    /// <summary>
    /// 右搖桿原始 Y 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float RawRightY { get; init; }

    /// <summary>
    /// 左搖桿校正後的 X 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float CorrectedLeftX { get; init; }

    /// <summary>
    /// 左搖桿校正後的 Y 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float CorrectedLeftY { get; init; }

    /// <summary>
    /// 右搖桿校正後的 X 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float CorrectedRightX { get; init; }

    /// <summary>
    /// 右搖桿校正後的 Y 軸值（正規化到 -1.0 ~ 1.0）。
    /// </summary>
    public float CorrectedRightY { get; init; }

    /// <summary>
    /// 左搖桿目前學習到的 X 軸偏移量。
    /// </summary>
    public float BiasLeftX { get; init; }

    /// <summary>
    /// 左搖桿目前學習到的 Y 軸偏移量。
    /// </summary>
    public float BiasLeftY { get; init; }

    /// <summary>
    /// 右搖桿目前學習到的 X 軸偏移量。
    /// </summary>
    public float BiasRightX { get; init; }

    /// <summary>
    /// 右搖桿目前學習到的 Y 軸偏移量。
    /// </summary>
    public float BiasRightY { get; init; }

    /// <summary>
    /// 目前搖桿進入死區閾值。
    /// </summary>
    public int ThumbDeadzoneEnter { get; init; }

    /// <summary>
    /// 目前搖桿離開死區閾值。
    /// </summary>
    public int ThumbDeadzoneExit { get; init; }

    /// <summary>
    /// 快照建立時間（UTC）。
    /// </summary>
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}