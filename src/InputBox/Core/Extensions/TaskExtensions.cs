using System.Diagnostics;

namespace InputBox.Core.Extensions;

/// <summary>
/// Task 類別的擴充方法
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// 安全地啟動並忽略異步任務的結果
    /// </summary>
    /// <param name="task">要執行的非同步任務</param>
    /// <param name="onException">當例外發生時的處理動作（選用）</param>
    public static async void SafeFireAndForget(this Task task, Action<Exception>? onException = null)
    {
        try
        {
            // 等待任務完成。
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 記錄至 Debug 視窗。
            Debug.WriteLine($"[背景任務例外] {ex.Message}");

            // 如果有提供額外的處理動作（如記錄至日誌或彈出視窗），則執行它。
            onException?.Invoke(ex);
        }
    }
}