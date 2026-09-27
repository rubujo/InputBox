using System.Diagnostics;

namespace InputBox.Core.Services;

/// <summary>
/// 負責程式內重啟時啟動新的執行個體；取代 <see cref="Application.Restart"/> 以便在舊視窗關閉前取得新程序識別碼，
/// 讓前景授權只交給該程序，而不必開放給所有程序；並以交接參數讓新執行個體等待接手單一執行個體 Mutex。
/// </summary>
internal static class RestartProcessLauncher
{
    /// <summary>
    /// 依目前執行檔與命令列引數建立重啟用的啟動資訊。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="Application.Restart"/> 相同：以同一個執行檔啟動，並只轉送第一個元素（執行檔本身）之後的引數；
    /// 改用 <see cref="ProcessStartInfo.ArgumentList"/> 逐一加入，避免引數內含引號時被錯誤拼接。
    /// </remarks>
    /// <param name="executablePath">要啟動的執行檔完整路徑。</param>
    /// <param name="commandLineArgs">目前程序的命令列引數（含第一個元素的執行檔路徑）。</param>
    /// <param name="handoffProcessId">
    /// 舊執行個體的程序識別碼；指定時會附加 <see cref="SingleInstanceHandoff.HandoffArgumentPrefix"/> 交接參數，
    /// 讓新執行個體等待並接手單一執行個體 Mutex。既有的交接參數一律先移除，避免連續重啟時重複轉送。
    /// </param>
    /// <returns>重啟用的啟動資訊。</returns>
    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        IReadOnlyList<string> commandLineArgs,
        int? handoffProcessId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(commandLineArgs);

        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = false
        };

        IReadOnlyList<string> forwardedArgs = SingleInstanceHandoff.RemoveHandoffArguments(commandLineArgs);

        for (int i = 1; i < forwardedArgs.Count; i++)
        {
            startInfo.ArgumentList.Add(forwardedArgs[i]);
        }

        if (handoffProcessId.HasValue)
        {
            startInfo.ArgumentList.Add(SingleInstanceHandoff.CreateHandoffArgument(handoffProcessId.Value));
        }

        return startInfo;
    }

    /// <summary>
    /// 嘗試啟動新的執行個體並取得其程序識別碼。
    /// </summary>
    /// <param name="startInfo">由 <see cref="CreateStartInfo"/> 建立的啟動資訊。</param>
    /// <param name="processId">成功時為新程序的識別碼；失敗時為 0。</param>
    /// <returns>若已成功啟動新程序則回傳 true。</returns>
    public static bool TryStart(
        ProcessStartInfo startInfo,
        out int processId)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        processId = 0;

        try
        {
            using Process? process = Process.Start(startInfo);

            if (process == null)
            {
                return false;
            }

            processId = process.Id;

            return true;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "程式內重啟時無法啟動新的執行個體");

            return false;
        }
    }
}
