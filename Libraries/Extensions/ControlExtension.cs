namespace InputBox.Libraries.Extensions;

/// <summary>
/// Control 類別的擴充方法
/// </summary>
public static class ControlExtension
{
    /// <summary>
    /// 安全的同步 Invoke
    /// </summary>
    /// <remarks>
    /// 會阻塞開啟端執行緒，直到 UI 執行緒完成操作。
    /// 適用於「連續輸入」或「游標移動」等需要視覺平滑的情境。
    /// </remarks>
    /// <param name="control">要執行的控制項</param>
    /// <param name="action">要執行的動作</param>
    public static void SafeInvoke(this Control control, Action action)
    {
        if (control == null ||
            control.IsDisposed ||
            !control.IsHandleCreated)
        {
            return;
        }

        try
        {
            if (control.InvokeRequired)
            {
                // 使用 Invoke，會等待 UI 處理完畢。
                control.Invoke(new MethodInvoker(() =>
                {
                    if (!control.IsDisposed &&
                        control.IsHandleCreated)
                    {
                        try
                        {
                            action();
                        }
                        catch (ObjectDisposedException)
                        {
                            // 忽略執行過程中的釋放錯誤。
                        }
                    }
                }));
            }
            else
            {
                // 如果已經在 UI 執行緒，直接執行。
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // 捕捉：在開啟 Invoke 的瞬間視窗被釋放。
        }
        catch (InvalidOperationException)
        {
            // 捕捉：Handle 尚未建立或已失效。
        }
    }

    /// <summary>
    /// 安全的非同步 Invoke
    /// </summary>
    /// <remarks>
    /// 自動檢查 IsHandleCreated 與 IsDisposed，並捕捉 ObjectDisposedException。
    /// 適用於從背景執行緒更新 UI，且不希望阻塞背景執行緒的情境。
    /// </remarks>
    /// <param name="control">要執行的控制項</param>
    /// <param name="action">要執行的動作</param>
    public static void SafeBeginInvoke(this Control control, Action action)
    {
        // 第一層檢查：如果控制項已經無效，直接放棄，不要排程。
        if (control == null ||
            control.IsDisposed ||
            !control.IsHandleCreated)
        {
            return;
        }

        try
        {
            // 嘗試排程到 UI 執行緒。
            control.BeginInvoke(new MethodInvoker(() =>
            {
                // 第二層檢查：
                // 雖然排程當下控制項還在，但等到 UI 執行緒真正要跑這段程式碼時，
                // 視窗可能剛好被關掉了。所以這裡要再檢查一次。
                if (control.IsDisposed ||
                    !control.IsHandleCreated)
                {
                    return;
                }

                try
                {
                    action();
                }
                catch (ObjectDisposedException)
                {
                    // 忽略執行過程中的釋放錯誤。
                }
            }));
        }
        catch (ObjectDisposedException)
        {
            // 捕捉：在開啟 BeginInvoke 的瞬間視窗被釋放。
        }
        catch (InvalidOperationException)
        {
            // 捕捉：Handle 尚未建立或已失效。
        }
    }
}