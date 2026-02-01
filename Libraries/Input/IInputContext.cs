namespace InputBox.Libraries.Input;

/// <summary>
/// IInputContext
/// </summary>
public interface IInputContext 
{
    /// <summary>
    /// Input 是否啟用
    /// </summary>
    bool IsInputActive { get; }
}