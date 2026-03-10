namespace InputBox.Core.Input;

/// <summary>
/// IInputContext
/// </summary>
public interface IInputContext : IDisposable
{
    /// <summary>
    /// Input 是否啟用
    /// </summary>
    bool IsInputActive { get; }
}