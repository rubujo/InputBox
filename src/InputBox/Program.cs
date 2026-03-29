using InputBox.Core.Configuration;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace InputBox;

/// <summary>
/// 應用程式進入點與全域資源管理。
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
    /// 是否已經執行過全域清理
    /// </summary>
    private static int _isCleanedUp = 0;

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        try
        {
            // 使用 Mutex 確保單一執行個體。
            // 使用 Local\ 前綴確保僅在目前 User Session 中唯一，避免干擾其他使用者。
            // 加入 GUID 以增加唯一性，防止與其他應用程式衝突。
            _mutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\InputBox_40A57F4D-4C7E-45FD-9DC7-BE96DC026D66_SingleInstance",
                out bool createdNew);

            if (!createdNew)
            {
                try
                {
                    // 如果已經有實例在執行，嘗試將其視窗帶到最前方。
                    // 這能解決 VS 偵錯時因殘留進程導致「看起來沒反應」的問題。
                    BringExistingInstanceToFront();
                }
                finally
                {
                    // 立即釋放不具備所有權的 Mutex 引用（原子化歸零）。
                    // 隨後直接退出。
                    ReleaseMutex();
                }

                return;
            }
        }
        catch (Exception ex)
        {
            // 處理 Mutex 建立失敗的情況（例如權限不足或系統錯誤）。
            MessageBox.Show(
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

            Application.Run(new MainForm());
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
    private static void SessionEndingHandler(object? sender, SessionEndingEventArgs e)
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
            Debug.WriteLine($"[清理] 退訂 SessionEnding 事件時發生例外：{ex.GetType().Name}: {ex.Message}");
        }

        // 處置全域字體快取，杜絕 GDI Handle 洩漏。
        try
        {
            MainForm.DisposeCaches();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[清理] 處置字體快取時發生例外：{ex.GetType().Name}: {ex.Message}");
        }

        // 釋放觸控式鍵盤 COM 介面，防止靜態 COM 物件洩漏。
        try
        {
            TouchKeyboardService.Cleanup();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[清理] 釋放觸控式鍵盤 COM 介面時發生例外：{ex.GetType().Name}: {ex.Message}");
        }

        // 釋放單一執行個體 Mutex，以利立即重啟。
        try
        {
            ReleaseMutex();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[清理] 釋放 Mutex 時發生例外：{ex.GetType().Name}: {ex.Message}");
        }

        // 強化 A11y 安全：緊急解除靜態系統事件訂閱（確保視窗關閉時完全脫離 UIA 鏈結），防止進程結束前發生記憶體洩漏。
        try
        {
            MainForm.EmergencyCleanupSystemEvents();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[清理] A11y 系統事件清理時發生例外：{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 尋找並喚醒現有的應用程式視窗。
    /// </summary>
    private static void BringExistingInstanceToFront()
    {
        using Process current = Process.GetCurrentProcess();

        Process[] processes = Process.GetProcessesByName(current.ProcessName);

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

                    nint handle = process.MainWindowHandle;

                    if (handle != 0)
                    {
                        // 若視窗被最小化，則還原它。
                        User32.ShowWindow(handle, User32.ShowWindowCommand.Restore);

                        // 將視窗帶到最前方。
                        User32.SetForegroundWindow(handle);

                        // 找到後即可退出列舉。
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // 忽略單一進程取得資訊失敗（可能正在關閉），繼續搜尋下一個。
                    Debug.WriteLine($"尋找既有視窗時忽略錯誤：{ex.Message}");
                }
            }
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

        if (m != null)
        {
            try
            {
                // 如果我們擁有 Mutex，則釋放它。
                // 若進程崩潰導致 Mutex 狀態異常或不具備所有權時呼叫，會拋出例外。
                m.ReleaseMutex();
            }
            catch (ObjectDisposedException)
            {
                // 已釋放則忽略。
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
    /// <param name="ex">Exception</param>
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
        MessageBox.Show(
            ex.Message,
            Strings.Err_Title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        // 確保程式完全退出。
        // 發生嚴重錯誤後強制結束進程。
        Environment.Exit(1);
    }
}