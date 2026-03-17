using System.Diagnostics;

namespace InputBox.Core.Extensions;

/// <summary>
/// Control 類別的擴充方法
/// </summary>
public static class ControlExtensions
{
    /// <summary>
    /// 安全的同步 Invoke
    /// </summary>
    /// <param name="control">要執行的控制項</param>
    /// <param name="action">要執行的動作</param>
    public static void SafeInvoke(
        this Control control,
        Action action)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return;
        }

        try
        {
            // 如果控制代碼尚未建立，無法使用 Invoke，直接在當前執行緒執行。
            // 這種情境通常發生在建構子或 Load 事件前，本來就還在同一執行緒。
            if (!control.IsHandleCreated)
            {
                action();

                return;
            }

            if (control.InvokeRequired)
            {
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

                        }
                    }
                }));
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {

        }
        catch (InvalidOperationException)
        {

        }
    }

    /// <summary>
    /// 安全的非同步 Invoke（支援 await）
    /// </summary>
    /// <param name="control">要執行的控制項</param>
    /// <param name="action">要執行的非同步動作</param>
    /// <returns>Task</returns>
    public static async Task SafeInvokeAsync(
        this Control control,
        Func<Task> action)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return;
        }

        // 如果控制代碼尚未建立，直接執行（因為此時通常還沒跨執行緒）。
        if (!control.IsHandleCreated)
        {
            try
            {
                await action();
            }
            catch (ObjectDisposedException)
            {
                // 忽略執行過程中的釋放錯誤。
            }
            catch (Exception)
            {
                // 重新拋出業務邏輯例外。
                throw;
            }

            return;
        }

        if (control.InvokeRequired)
        {
            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                control.BeginInvoke(new MethodInvoker(async () =>
                {
                    try
                    {
                        // 再次檢查狀態，因為排程到執行可能有延遲。
                        if (control.IsDisposed ||
                            !control.IsHandleCreated)
                        {
                            tcs.TrySetResult();

                            return;
                        }

                        await action();

                        tcs.TrySetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        tcs.TrySetCanceled();
                    }
                    catch (Exception ex)
                    {
                        // 確保例外能傳回呼叫端。
                        tcs.TrySetException(ex);
                    }
                }));
            }
            catch (ObjectDisposedException)
            {
                tcs.TrySetResult();
            }
            catch (InvalidOperationException)
            {
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            await tcs.Task;
        }
        else
        {
            try
            {
                await action();
            }
            catch (ObjectDisposedException)
            {

            }
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
    public static void SafeBeginInvoke(
        this Control control,
        Action action)
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

    /// <summary>
    /// 依據語言習慣產生包含助記鍵的文字
    /// </summary>
    /// <param name="text">原始文字</param>
    /// <param name="mnemonic">助記鍵字母</param>
    /// <returns>格式化後的文字</returns>
    public static string GetMnemonicText(string text, char mnemonic)
    {
        if (string.IsNullOrEmpty(text))
        {
            return $"&{mnemonic}";
        }

        // 簡單判斷：如果第一個字元是 ASCII（通常是英文），則使用前綴式 &。
        // 如果是非 ASCII（如中日文），則使用後綴括號式 (&X)。
        if (text[0] < 128)
        {
            return $"&{text}";
        }

        return $"{text} (&{mnemonic})";
    }

    /// <summary>
    /// 執行通用的眼動儀注視填滿動畫
    /// </summary>
    /// <param name="control">要執行動畫的控制項</param>
    /// <param name="animationIdField">用於追蹤目前動畫序號的欄位引用（需使用 Interlocked 操作）</param>
    /// <param name="id">本次動畫的目標序號</param>
    /// <param name="progressSetter">設定進度值（0.0 ~ 1.0）的回呼委派</param>
    /// <param name="durationMs">動畫總時長（毫秒），預設 1000ms</param>
    /// <returns>Task</returns>
    public static async Task RunDwellAnimationAsync(
        this Control control,
        long id,
        Func<long> animationIdGetter,
        Action<float> progressSetter,
        int durationMs = 1000)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return;
        }

        // 系統動畫服從性：若使用者關閉了 UI 特效，直接跳至完成狀態。
        if (!SystemInformation.UIEffectsEnabled)
        {
            progressSetter(1.0f);

            control.Invalidate();

            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        while (animationIdGetter() == id &&
            !control.IsDisposed &&
            control.IsHandleCreated)
        {
            double elapsed = stopwatch.Elapsed.TotalMilliseconds;

            float progress = (float)Math.Min(1.0, elapsed / durationMs);

            progressSetter(progress);

            control.Invalidate();

            if (progress >= 1.0f)
            {
                break;
            }

            try
            {
                // 固定幀率（約 60 FPS）。
                await Task.Delay(16);
            }
            catch
            {
                break;
            }
        }
    }
}