using System.Diagnostics;

namespace InputBox.Core.Extensions;

/// <summary>
/// Task 類別的擴充方法
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// 全域背景工作例外處理程序
    /// </summary>
    private static volatile Action<Exception>? _globalExceptionHandler;

    /// <summary>
    /// 取得或設定全域背景工作例外處理程序
    /// </summary>
    public static Action<Exception>? GlobalExceptionHandler
    {
        get => _globalExceptionHandler;
        set => _globalExceptionHandler = value;
    }

    /// <summary>
    /// 安全地啟動並忽略非同步任務的結果
    /// </summary>
    /// <param name="task">要執行的非同步任務</param>
    /// <param name="onException">當例外發生時的處理動作（選用）</param>
    public static void SafeFireAndForget(
        this Task task,
        Action<Exception>? onException = null)
    {
        _ = ExecuteSafeFireAndForgetInternalAsync(task, onException);
    }

    /// <summary>
    /// 安全地啟動非同步任務，若失敗則透過指定的廣播委派發送訊息
    /// </summary>
    /// <param name="task">Task</param>
    /// <param name="announceAction">廣播委派（例如 MainForm.AnnounceA11y）</param>
    /// <param name="errorMessageFormat">錯誤訊息格式（選用）</param>
    public static void SafeFireAndForget(
        this Task task,
        Action<string> announceAction,
        string? errorMessageFormat = null)
    {
        _ = ExecuteSafeFireAndForgetInternalAsync(task, (ex) =>
        {
            string message = string.IsNullOrEmpty(errorMessageFormat) ?
                ex.Message :
                string.Format(errorMessageFormat, ex.Message);

            announceAction(message);
        });
    }

    /// <summary>
    /// 安全地取消並處置 CancellationTokenSource
    /// </summary>
    /// <param name="cts">CancellationTokenSource</param>
    public static void CancelAndDispose(this CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            return;
        }

        try
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (AggregateException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// 執行非同步任務並安全地處理任何可能發生的例外
    /// </summary>
    /// <param name="task">Task</param>
    /// <param name="onException">Action&lt;Exception&gt;?</param>
    /// <returns>Task</returns>
    private static async Task ExecuteSafeFireAndForgetInternalAsync(
        Task task,
        Action<Exception>? onException)
    {
        try
        {
            // 等待任務完成。
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 非同步任務被取消（例如視窗關閉、Token 觸發），這是正常的，不應視為錯誤。
            return;
        }
        catch (Exception ex)
        {
            // 記錄至 Debug 視窗。
            Debug.WriteLine($"[背景任務例外] {ex.Message}");

            try
            {
                // 如果有提供額外的處理動作（如記錄至日誌或彈出視窗），則執行它。
                onException?.Invoke(ex);

                // 執行全域處理（使用快照確保原子性）。
                Action<Exception>? handler = _globalExceptionHandler;

                handler?.Invoke(ex);
            }
            catch (Exception secondaryEx)
            {
                Debug.WriteLine($"[背景任務例外] 例外處理程序發生錯誤：{secondaryEx.Message}");
            }
        }
    }
}