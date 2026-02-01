namespace InputBox.Libraries.Input;

/// <summary>
/// 遊戲手把重複設定
/// </summary>
/// <remarks> 
/// 單位為「Polling frame」，而非毫秒。
/// 實際時間取決於 PollingIntervalMs（預設約 16ms）。
/// </remarks>
internal sealed class GamepadRepeatSettings
{
    /// <summary>
    /// 初始延遲
    /// <para>約 320ms（20 * 16ms）</para>
    /// </summary>
    public int InitialDelayFrames { get; init; } = 20;

    /// <summary>
    /// Repeat 速度
    /// <para>約 48ms（3 * 16ms）</para>
    /// </summary>
    public int IntervalFrames { get; init; } = 3;

    /// <summary>
    /// 驗證
    /// </summary>
    /// <exception cref="InvalidOperationException">發生例外時會拋出</exception>
    public void Validate()
    {
        if (IntervalFrames <= 0)
        {
            throw new InvalidOperationException("GamepadRepeatSettings.IntervalFrames 的值必須大於 0。");
        }
    }
}
