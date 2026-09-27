using System.Globalization;

namespace InputBox.Core.Services;

/// <summary>
/// 單一執行個體啟動時應採取的動作。
/// </summary>
internal enum SingleInstanceStartupAction
{
    /// <summary>
    /// 已取得單一執行個體 Mutex，繼續正常啟動。
    /// </summary>
    Proceed,

    /// <summary>
    /// 本執行個體是程式內重啟的接手者，應等待舊執行個體釋放 Mutex 後接手，不喚醒既有實例。
    /// </summary>
    WaitForHandoff,

    /// <summary>
    /// 已有其他執行個體持有 Mutex，應嘗試喚醒既有實例。
    /// </summary>
    ActivateExisting,
}

/// <summary>
/// 喚醒既有實例失敗後的處置。
/// </summary>
internal enum SingleInstanceFallbackDecision
{
    /// <summary>
    /// 允許以 fallback 方式繼續啟動新視窗，避免使用者看不到任何畫面。
    /// </summary>
    TryFallback,

    /// <summary>
    /// 已找到可喚醒視窗但前景切換被系統阻擋，結束本次啟動以維持單一實例。
    /// </summary>
    ExitForegroundBlocked,

    /// <summary>
    /// 程式內重啟交接進行中，接手的新執行個體會自行顯示視窗，結束本次啟動以免出現兩個視窗。
    /// </summary>
    ExitRestartHandoffPending,
}

/// <summary>
/// 程式內重啟的單一執行個體交接規則。
/// </summary>
/// <remarks>
/// <para>舊執行個體在持有 Mutex 的情況下，以 <see cref="HandoffArgumentPrefix"/> 參數啟動新執行個體，
/// 並於關閉所有視窗後才釋放 Mutex；新執行個體據此等待並接手 Mutex，而不是把 Mutex 視為「已有實例」而喚醒後退出。</para>
/// <para>如此可避免舊實例先釋放 Mutex 時，恰好另外啟動的執行個體搶先取得 Mutex，導致重啟出來的實例退出。</para>
/// </remarks>
internal static class SingleInstanceHandoff
{
    /// <summary>
    /// 標記「本執行個體由程式內重啟啟動」的命令列參數前綴，後接舊執行個體的程序識別碼。
    /// </summary>
    internal const string HandoffArgumentPrefix = "--restart-handoff=";

    /// <summary>
    /// 新執行個體等待舊執行個體釋放 Mutex 的上限。
    /// </summary>
    internal static readonly TimeSpan HandoffWaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 建立交接參數。
    /// </summary>
    /// <param name="processId">舊執行個體的程序識別碼。</param>
    /// <returns>交接參數字串。</returns>
    public static string CreateHandoffArgument(int processId)
    {
        return HandoffArgumentPrefix + processId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 從命令列引數中取得交接來源的程序識別碼。
    /// </summary>
    /// <param name="commandLineArgs">命令列引數（第一個元素為執行檔本身，會被略過）。</param>
    /// <param name="processId">找到有效交接參數時為舊執行個體的程序識別碼；否則為 0。</param>
    /// <returns>若包含有效的交接參數則回傳 true。</returns>
    public static bool TryGetHandoffProcessId(IReadOnlyList<string> commandLineArgs, out int processId)
    {
        ArgumentNullException.ThrowIfNull(commandLineArgs);

        processId = 0;

        for (int i = 1; i < commandLineArgs.Count; i++)
        {
            string arg = commandLineArgs[i];

            if (arg.StartsWith(HandoffArgumentPrefix, StringComparison.Ordinal) &&
                int.TryParse(
                    arg.AsSpan(HandoffArgumentPrefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parsed) &&
                parsed > 0)
            {
                processId = parsed;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 移除命令列引數中的交接參數，避免連續重啟時交接參數被重複轉送。
    /// </summary>
    /// <param name="commandLineArgs">命令列引數（第一個元素為執行檔本身，會原樣保留）。</param>
    /// <returns>不含交接參數的命令列引數。</returns>
    public static IReadOnlyList<string> RemoveHandoffArguments(IReadOnlyList<string> commandLineArgs)
    {
        ArgumentNullException.ThrowIfNull(commandLineArgs);

        List<string> result = new(commandLineArgs.Count);

        for (int i = 0; i < commandLineArgs.Count; i++)
        {
            if (i > 0 &&
                commandLineArgs[i].StartsWith(HandoffArgumentPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(commandLineArgs[i]);
        }

        return result;
    }

    /// <summary>
    /// 依 Mutex 取得結果與是否為交接啟動，決定啟動動作。
    /// </summary>
    /// <param name="createdNew">是否新建並取得單一執行個體 Mutex。</param>
    /// <param name="isHandoffLaunch">是否帶有有效的交接參數。</param>
    /// <returns>應採取的啟動動作。</returns>
    public static SingleInstanceStartupAction ResolveStartupAction(bool createdNew, bool isHandoffLaunch)
    {
        if (createdNew)
        {
            return SingleInstanceStartupAction.Proceed;
        }

        return isHandoffLaunch ?
            SingleInstanceStartupAction.WaitForHandoff :
            SingleInstanceStartupAction.ActivateExisting;
    }

    /// <summary>
    /// 喚醒既有實例失敗後，決定是否允許 fallback 啟動新視窗。
    /// </summary>
    /// <param name="fallbackPermitted">喚醒流程判定是否允許 fallback（例如找不到任何可喚醒視窗）。</param>
    /// <param name="restartHandoffPending">目前是否有程式內重啟交接進行中。</param>
    /// <returns>喚醒失敗後的處置。</returns>
    public static SingleInstanceFallbackDecision ResolveFallback(bool fallbackPermitted, bool restartHandoffPending)
    {
        if (!fallbackPermitted)
        {
            return SingleInstanceFallbackDecision.ExitForegroundBlocked;
        }

        // 交接期間舊實例的視窗已關閉、新實例尚未顯示視窗，若此時允許 fallback 會多開一個視窗。
        return restartHandoffPending ?
            SingleInstanceFallbackDecision.ExitRestartHandoffPending :
            SingleInstanceFallbackDecision.TryFallback;
    }
}
