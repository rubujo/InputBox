namespace InputBox.Libraries.Feedback;

/// <summary>
/// 震動設定結構
/// </summary>
/// <param name="Strength">強度（0~65535）</param>
/// <param name="Duration">持續時間（毫秒）</param>
public readonly record struct VibrationProfile(ushort Strength, int Duration);