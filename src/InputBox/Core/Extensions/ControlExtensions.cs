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
    /// 依據語言習慣與內容產生包含助記鍵（Mnemonic）的文字
    /// </summary>
    /// <param name="text">原始文字</param>
    /// <param name="mnemonic">助記鍵字母</param>
    /// <returns>格式化後的文字</returns>
    public static string GetMnemonicText(string text, char mnemonic)
    {
        if (string.IsNullOrEmpty(text))
        {
            return $"&{char.ToUpperInvariant(mnemonic)}";
        }

        char upperMnemonic = char.ToUpperInvariant(mnemonic);

        // 冪等性檢查：如果字串中已經包含 '&' 標記或是相同的後綴提示，則直接回傳原文字。
        // 這能防止重複調用或資源檔本身自帶標記時產生的「確定 (&A) (&A)」問題。
        if (text.Contains('&') ||
            text.Contains($"(&{upperMnemonic})", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        // 核心設計：全語系一律採用後綴式提示。
        return $"{text} (&{upperMnemonic})";
    }

    /// <summary>
    /// 判斷目前控制項是否處於深色模式（Dark Mode）
    /// </summary>
    /// <param name="control">要檢查的控制項</param>
    /// <returns>若為深色模式則傳回 true</returns>
    public static bool IsDarkModeActive(this Control control)
    {
        if (control == null ||
            control.IsDisposed)
        {
            return false;
        }

        // 高對比模式優先權高於深色模式，但在配色邏輯中通常分開處理。
        if (SystemInformation.HighContrast)
        {
            return false;
        }

        // .NET 10 官方 API：返回目前應用程式實際解析後的深色模式狀態。
        return Application.IsDarkModeEnabled;
    }

    /// <summary>
    /// 執行通用的眼動儀注視填滿動畫
    /// </summary>
    /// <param name="control">要執行動畫的控制項</param>
    /// <param name="animationIdField">用於追蹤目前動畫序號的欄位引用（需使用 Interlocked 操作）</param>
    /// <param name="id">本次動畫的目標序號</param>
    /// <param name="progressSetter">設定進度值（0.0 ~ 1.0）的回呼委派</param>
    /// <param name="durationMs">動畫總時長（毫秒），預設 1000ms</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task</returns>
    public static async Task RunDwellAnimationAsync(
        this Control control,
        long id,
        Func<long> animationIdGetter,
        Action<float> progressSetter,
        int durationMs = 1000,
        CancellationToken ct = default)
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

        while (!ct.IsCancellationRequested &&
            animationIdGetter() == id &&
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
                await Task.Delay(16, ct);
            }
            catch
            {
                break;
            }
        }
    }

    /// <summary>
    /// 遞歸更新控制項及其所有子控制項的背景色與前景色。
    /// </summary>
    /// <param name="parent">要開始更新的父控制項。</param>
    /// <param name="bg">新的背景顏色。</param>
    /// <param name="fg">新的前景顏色。</param>
    public static void UpdateRecursive(
        this Control parent,
        Color bg,
        Color fg)
    {
        if (parent == null)
        {
            return;
        }

        parent.BackColor = bg;
        parent.ForeColor = fg;

        foreach (Control child in parent.Controls)
        {
            UpdateRecursive(child, bg, fg);
        }
    }

    /// <summary>
    /// 將控制項及其所有子控制項的顏色屬性重設為 Color.Empty，
    /// 這將觸發 .NET 10 的原生主題引擎自動套用正確的系統配色。
    /// </summary>
    /// <param name="parent">父控制項。</param>
    public static void ResetThemeRecursive(this Control parent)
    {
        UpdateRecursive(parent, Color.Empty, Color.Empty);
    }
}