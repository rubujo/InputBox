namespace InputBox.Core.Input;

/// <summary>
/// 震動優先級，用於在硬體保護情境下保留關鍵 A11y 體感。
/// </summary>
internal enum VibrationPriority
{
    /// <summary>
    /// 高頻、可被抑制的環境提示（例如游標移動）。
    /// </summary>
    Ambient = 0,

    /// <summary>
    /// 一般操作回饋。
    /// </summary>
    Normal = 1,

    /// <summary>
    /// 關鍵提示，應盡量保留可感知體感。
    /// </summary>
    Critical = 2
}