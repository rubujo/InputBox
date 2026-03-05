using System.Diagnostics;

namespace InputBox.Core.Extensions;

/// <summary>
/// Task 類別的擴充方法
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// 安全地啟動並忽略異
    /// </summary>
    /// <param name="task">Task</param>
    public static void SafeFireAndForget(this Task task)
    {
        task.ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                Debug.WriteLine($"[背景任務例外] {t.Exception.Flatten().InnerException?.Message}");
            }
        }, 
        TaskContinuationOptions.OnlyOnFaulted);
    }
}