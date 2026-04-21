using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace InputBox;

/// <summary>
/// 應用程式進入點與全域資源管理
/// </summary>
/// <remarks>
/// 螢幕方向（WCAG 1.3.4）：本應用程式為 Windows 桌面軟體，不強制鎖定螢幕方向。
/// WinForms 的 <see cref="AutoScaleMode.Dpi"/> 機制與 DPI 感知設定（Per-Monitor V2）
/// 可自動適應橫向與縱向兩種顯示方向，由 Windows 作業系統負責處理旋轉適配，
/// 無需應用程式層級的方向鎖定設定。
/// </remarks>
internal static class Program
{
    /// <summary>
    /// 單一執行個體 Mutex
    /// </summary>
    private static Mutex? _mutex;

    /// <summary>
    /// 目前執行個體是否持有主單例 Mutex
    /// </summary>
    private static int _ownsMutex = 0;

    /// <summary>
    /// 是否已經執行過全域清理
    /// </summary>
    private static int _isCleanedUp = 0;

    /// <summary>
    /// Fallback 啟動防護 Mutex（確保同一時間只有一個 fallback 視窗能繼續啟動）
    /// </summary>
    private static Mutex? _fallbackGuardMutex;

    /// <summary>
    /// 目前執行個體是否持有 fallback 防護 Mutex
    /// </summary>
    private static int _ownsFallbackGuardMutex = 0;

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 在 Wine / Proton 環境下，將宿主 locale 橋接為 .NET CultureInfo，
        // 確保衛星資源組件（zh-Hant、zh-Hans 等）能在啟動時正確載入。
        WineLocaleBootstrapper.Apply();

        try
        {
            // 使用 Mutex 確保單一執行個體。
            // 使用 Local\ 前綴確保僅在目前 User Session 中唯一，避免干擾其他使用者。
            // 加入 GUID 以增加唯一性，防止與其他應用程式衝突。
            _mutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\InputBox_40A57F4D-4C7E-45FD-9DC7-BE96DC026D66_SingleInstance",
                out bool createdNew);

            Interlocked.Exchange(ref _ownsMutex, createdNew ? 1 : 0);

            if (!createdNew)
            {
                LoggerService.LogInfo($"SingleInstance.MutexCheck pid={Environment.ProcessId} createdNew={createdNew}");

                bool broughtToFront = false,
                    fallbackPermitted = false;

                string activationDiagnostic = string.Empty;

                try
                {
                    // 如果已經有實例在執行，嘗試將其視窗帶到最前方。
                    // 這能解決 VS 偵錯時因殘留進程導致「看起來沒反應」的問題。
                    broughtToFront = BringExistingInstanceToFront(
                        out activationDiagnostic,
                        out fallbackPermitted);

                    // 降噪：僅記錄喚醒失敗路徑，避免每次成功喚醒都輸出高頻資訊。
                    if (!broughtToFront)
                    {
                        LoggerService.LogInfo($"SingleInstance.ActivationResult pid={Environment.ProcessId} broughtToFront={broughtToFront} fallbackPermitted={fallbackPermitted} detail={activationDiagnostic}");
                    }
                }
                finally
                {
                    // 立即釋放不具備所有權的 Mutex 引用（原子化歸零）。
                    ReleaseMutex();
                }

                if (broughtToFront)
                {
                    // 既有實例已成功喚醒，本次啟動可直接結束。
                    return;
                }

                // 收斂策略：若已找到可喚醒視窗但前景切換被系統阻擋，
                // 則不允許 fallback 啟動新視窗，避免破壞單實例預期。
                if (!fallbackPermitted)
                {
                    LoggerService.LogInfo($"SingleInstance.FallbackSuppressed pid={Environment.ProcessId} reason=foreground_blocked");

                    return;
                }

                // Fallback 次數防護：確保同一時間只有一個 fallback 實例能繼續啟動。
                // 若另一個 fallback 進程正在執行中（視窗尚未出現），則靜默中止本次啟動，
                // 防止快速多次點擊造成多個視窗同時開啟。
                if (!TryAcquireFallbackGuard())
                {
                    LoggerService.LogInfo($"SingleInstance.FallbackBlocked pid={Environment.ProcessId} reason=guard_active");

                    return;
                }

                // Fallback：若喚醒失敗，避免「直接吞掉啟動」造成使用者無畫面。
                // 此時允許本次啟動繼續，以確保至少有一個可見視窗。
                LoggerService.LogInfo($"SingleInstance.FallbackStartNew pid={Environment.ProcessId} reason=no_activatable_window");
            }
        }
        catch (Exception ex)
        {
            // 處理 Mutex 建立失敗的情況（例如權限不足或系統錯誤）。
            LoggerService.LogException(ex, "Mutex 建立失敗");

            GamepadMessageBox.Show(
                null,
                ex.Message,
                Strings.Err_Title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return;
        }

        // 強制將所有 UI 執行緒例外路由到 ThreadException 事件。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // 設定全域例外處理。
        Application.ThreadException += (sender, e) => HandleException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleException(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            HandleException(e.Exception);

            e.SetObserved();
        };

        // 註冊系統事件：在關機或登出時，強制停止所有硬體震動。
        SystemEvents.SessionEnding += SessionEndingHandler;

        try
        {
            // 載入 XInput DLL。
            NativeLibrary.SetDllImportResolver(typeof(XInput).Assembly, DllResolver.ResolveXInput);

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.SetColorMode(SystemColorMode.System);

            // 載入設定檔（若無檔案會自動建立）。
            AppSettings.Load();

            // 讀取前一個執行個體留下的一次性重啟啟用標記，
            // 讓新的 MainForm 能在首次顯示時主動把自己帶回前景。
            bool forceForegroundOnStartup = RestartActivationCoordinator.Shared.ConsumePendingActivationRequest();

            Application.Run(new MainForm(forceForegroundOnStartup));
        }
        catch (Exception ex)
        {
            // 捕捉 Main 函式本身的嚴重錯誤。
            HandleException(ex);
        }
        finally
        {
            // 執行統一清理邏輯。
            // 確保在任何結束路徑下皆能執行（包含正常退出與 finally 區塊），
            // 杜絕資源洩漏與 GDI Handle 殘留。
            PerformFinalCleanup();
        }
    }

    /// <summary>
    /// 系統結束處理常式
    /// </summary>
    /// <param name="sender">事件來源</param>
    /// <param name="e">事件參數</param>
    private static void SessionEndingHandler(
        object? sender,
        SessionEndingEventArgs e)
    {
        FeedbackService.EmergencyStopAllActiveControllers();
    }

    /// <summary>
    /// 統一執行所有全域資源清理工作（執行緒安全且冪等）
    /// </summary>
    private static void PerformFinalCleanup()
    {
        // 確保清理邏輯僅執行一次，防止多執行緒競態（如 UI 執行緒與例外處理執行緒同時清理）引發 ObjectDisposedException。
        if (Interlocked.Exchange(ref _isCleanedUp, 1) != 0)
        {
            return;
        }

        // 採取「最大化資源回收」策略，確保各清理步驟互不干擾。

        // 退訂系統事件（防止靜態根引用導致的記憶體洩漏）。
        try
        {
            SystemEvents.SessionEnding -= SessionEndingHandler;
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "退訂 SessionEnding 事件時發生例外");

            Debug.WriteLine($"[清理] 退訂 SessionEnding 事件時發生例外：{ex.GetType().Name}：{ex.Message}");
        }

        // 處置全域字體快取，杜絕 GDI Handle 洩漏。
        try
        {
            MainForm.DisposeCaches();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "處置字體快取時發生例外");

            Debug.WriteLine($"[清理] 處置字體快取時發生例外：{ex.GetType().Name}：{ex.Message}");
        }

        // 釋放觸控式鍵盤 COM 介面，防止靜態 COM 物件洩漏。
        try
        {
            TouchKeyboardService.Cleanup();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "釋放觸控式鍵盤 COM 介面時發生例外");

            Debug.WriteLine($"[清理] 釋放觸控式鍵盤 COM 介面時發生例外：{ex.GetType().Name}：{ex.Message}");
        }

        // 釋放單一執行個體 Mutex，以利立即重啟。
        try
        {
            ReleaseMutex();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "PerformFinalCleanup 釋放 Mutex 時發生例外");

            Debug.WriteLine($"[清理] 釋放 Mutex 時發生例外：{ex.GetType().Name}：{ex.Message}");
        }

        // 釋放 Fallback 啟動防護 Mutex（讓下一次啟動可正常進行 fallback）。
        try
        {
            ReleaseFallbackGuardMutex();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "PerformFinalCleanup 釋放 FallbackGuard Mutex 時發生例外");

            Debug.WriteLine($"[清理] 釋放 FallbackGuard Mutex 時發生例外：{ex.GetType().Name}：{ex.Message}");
        }

        // 強化 A11y 安全：緊急解除靜態系統事件訂閱（確保視窗關閉時完全脫離 UIA 鏈結），防止進程結束前發生記憶體洩漏。
        try
        {
            MainForm.EmergencyCleanupSystemEvents();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "A11y 系統事件清理時發生例外");

            Debug.WriteLine($"[清理] A11y 系統事件清理時發生例外：{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>
    /// 嘗試取得 Fallback 啟動防護 Mutex，確保同一時間只有一個 fallback 啟動能繼續執行。
    /// </summary>
    /// <returns>
    /// 若成功取得防護（本次為第一個 fallback）則回傳 <see langword="true"/>；
    /// 若另一個 fallback 進程仍在執行中則回傳 <see langword="false"/>。
    /// </returns>
    private static bool TryAcquireFallbackGuard()
    {
        try
        {
            _fallbackGuardMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\InputBox_40A57F4D-4C7E-45FD-9DC7-BE96DC026D66_FallbackGuard",
                out bool claimedNew);

            if (claimedNew)
            {
                Interlocked.Exchange(ref _ownsFallbackGuardMutex, 1);

                // 成功建立新 Mutex，本次為第一個 fallback。
                return true;
            }

            // Mutex 已存在（另一個 fallback 進程持有，或前一個 fallback 進程異常結束而遺留）。
            // 嘗試以非阻塞方式取得所有權（處理前次崩潰造成的 Abandoned 狀態）。
            try
            {
                bool acquired = _fallbackGuardMutex.WaitOne(0);

                if (acquired)
                {
                    Interlocked.Exchange(ref _ownsFallbackGuardMutex, 1);

                    // 前一個 fallback 已結束（或遺棄），本次取得所有權。
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                Interlocked.Exchange(ref _ownsFallbackGuardMutex, 1);

                // 前一個 fallback 進程崩潰後遺棄了 Mutex，本次取得所有權。
                return true;
            }

            // 另一個 fallback 進程仍在執行中，不允許繼續。
            Interlocked.Exchange(ref _ownsFallbackGuardMutex, 0);

            Interlocked.Exchange(ref _fallbackGuardMutex, null)?.Dispose();

            return false;
        }
        catch (Exception ex)
        {
            Mutex? guard = Interlocked.Exchange(ref _fallbackGuardMutex, null);

            Interlocked.Exchange(ref _ownsFallbackGuardMutex, 0);

            guard?.Dispose();

            LoggerService.LogException(ex, "TryAcquireFallbackGuard 取得防護 Mutex 失敗，允許繼續啟動");

            // Fail open：防護機制失效時允許繼續啟動，避免永久封鎖使用者操作。
            return true;
        }
    }

    /// <summary>
    /// 釋放 Fallback 啟動防護 Mutex（原子化操作）
    /// </summary>
    private static void ReleaseFallbackGuardMutex()
    {
        Mutex? m = Interlocked.Exchange(ref _fallbackGuardMutex, null);

        bool ownsFallbackGuardMutex = Interlocked.Exchange(ref _ownsFallbackGuardMutex, 0) != 0;

        if (m != null)
        {
            try
            {
                if (ownsFallbackGuardMutex)
                {
                    m.ReleaseMutex();
                }
            }
            catch (ObjectDisposedException)
            {
                // 已釋放則忽略。
            }
            catch (ApplicationException)
            {
                // 未持有同步物件時可忽略。
            }
            catch (UnauthorizedAccessException)
            {
                // 不具備所有權時可忽略。
            }
            catch (SynchronizationLockException)
            {
                // 不具備所有權時可忽略。
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "ReleaseFallbackGuardMutex 發生未預期錯誤");

                Debug.WriteLine($"釋放 FallbackGuard Mutex 時發生錯誤：{ex.Message}");
            }
            finally
            {
                m.Dispose();
            }
        }
    }

    /// <summary>
    /// 尋找並喚醒現有的應用程式視窗
    /// </summary>
    /// <param name="diagnostic">診斷資訊</param>
    /// <param name="fallbackPermitted">是否允許 fallback 啟動新實例</param>
    /// <returns>是否成功喚醒現有的應用程式視窗</returns>
    private static bool BringExistingInstanceToFront(
        out string diagnostic,
        out bool fallbackPermitted)
    {
        using Process current = Process.GetCurrentProcess();

        Process[] processes = Process.GetProcessesByName(current.ProcessName);

        int checkedCandidates = 0,
            zeroHandleCount = 0,
            activationAttempts = 0,
            invalidHandleCount = 0;

        bool foregroundBlocked = false;

        diagnostic = string.Empty;

        fallbackPermitted = false;

        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    // 跳過目前的執行個體。
                    if (process.Id == current.Id)
                    {
                        continue;
                    }

                    checkedCandidates++;

                    nint handle = process.MainWindowHandle;

                    if (handle != 0)
                    {
                        if (!User32.IsWindow(handle))
                        {
                            invalidHandleCount++;

                            LoggerService.LogInfo($"SingleInstance.InvalidWindowHandle pid={process.Id} handle={handle}");

                            continue;
                        }

                        activationAttempts++;

                        // 若視窗被最小化，則還原它。
                        bool restored = User32.ShowWindow(handle, User32.ShowWindowCommand.Restore),
                            // 將視窗帶到最前方。
                            foregrounded = User32.SetForegroundWindow(handle);

                        if (foregrounded)
                        {
                            diagnostic = $"status=activated,targetPid={process.Id},checked={checkedCandidates},zeroHandle={zeroHandleCount},invalidHandle={invalidHandleCount},attempts={activationAttempts},restore={restored},foreground={foregrounded}";

                            return true;
                        }

                        LoggerService.LogInfo($"SingleInstance.ForegroundFailed pid={process.Id} handle={handle} restore={restored} foreground={foregrounded}");

                        foregroundBlocked = true;

                        continue;
                    }

                    zeroHandleCount++;

                    LoggerService.LogInfo($"SingleInstance.CandidateNoMainWindow pid={process.Id}");
                }
                catch (Exception ex)
                {
                    // 忽略單一進程取得資訊失敗（可能正在關閉），繼續搜尋下一個。
                    LoggerService.LogException(ex, "SingleInstance.EnumerateCandidateFailed");

                    Debug.WriteLine($"尋找既有視窗時忽略錯誤：{ex.Message}");
                }
            }

            fallbackPermitted = !foregroundBlocked && activationAttempts == 0;

            diagnostic = $"status=activation_failed,checked={checkedCandidates},zeroHandle={zeroHandleCount},invalidHandle={invalidHandleCount},attempts={activationAttempts},foregroundBlocked={foregroundBlocked}";

            return false;
        }
        finally
        {
            // 關鍵修正：確保陣列中「所有」取得的 Process 物件皆被釋放。
            // 包含因 break 跳過的剩餘物件，杜絕核心物件控制代碼（Handle）洩漏。
            foreach (Process p in processes)
            {
                p.Dispose();
            }
        }
    }

    /// <summary>
    /// 釋放單一執行個體 Mutex（原子化操作）
    /// </summary>
    public static void ReleaseMutex()
    {
        // 使用原子交換確保僅有一個執行緒能執行釋放邏輯，防止 NullReferenceException。
        Mutex? m = Interlocked.Exchange(ref _mutex, null);

        bool ownsMutex = Interlocked.Exchange(ref _ownsMutex, 0) != 0;

        if (m != null)
        {
            try
            {
                // 如果我們擁有 Mutex，則釋放它。
                // 若進程崩潰導致 Mutex 狀態異常或不具備所有權時呼叫，會拋出例外。
                if (ownsMutex)
                {
                    m.ReleaseMutex();
                }
            }
            catch (ObjectDisposedException)
            {
                // 已釋放則忽略。
            }
            catch (ApplicationException)
            {
                // 未持有同步物件時可忽略。
            }
            catch (UnauthorizedAccessException)
            {
                // 不具備所有權時呼叫 ReleaseMutex 會拋出此例外（部分系統行為），可忽略。
            }
            catch (SynchronizationLockException)
            {
                // 不具備所有權時呼叫 ReleaseMutex 會拋出此例外，可忽略。
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "ReleaseMutex 發生未預期錯誤");

                Debug.WriteLine($"釋放 Mutex 時發生錯誤：{ex.Message}");
            }
            finally
            {
                m.Dispose();
            }
        }
    }

    /// <summary>
    /// 統一處理未捕捉的例外
    /// </summary>
    /// <param name="ex">要處理的例外；為 null 時直接略過。</param>
    static void HandleException(Exception? ex)
    {
        if (ex == null)
        {
            return;
        }

        // 記錄錯誤到檔案系統。
        LoggerService.LogException(ex, "全域未捕捉例外（Unhandled Exception）");

        // 記錄錯誤到 Debug 控制台。
        Debug.WriteLine($"[嚴重] 未捕捉例外：{ex}");

        // 緊急停止所有控制器震動，防止崩潰後控制器持續震動。
        FeedbackService.EmergencyStopAllActiveControllers();

        // 執行統一清理路徑。
        // 包含釋放 Mutex 以利立即重啟、處置全域字體快取以杜絕 GDI洩漏、以及強化 A11y 安全清理。
        PerformFinalCleanup();

        // 告知使用者錯誤。
        GamepadMessageBox.Show(
            null,
            ex.Message,
            Strings.Err_Title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        // 確保程式完全退出。
        // 發生嚴重錯誤後強制結束進程。
        Environment.Exit(1);
    }
}