using InputBox.Core.Configuration;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace InputBox;

internal static class Program
{
    /// <summary>
    /// 單一執行個體 Mutex
    /// </summary>
    private static Mutex? _mutex;

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 使用 Mutex 確保單一執行個體。
        // 使用 Local\ 前綴確保僅在目前 User Session 中唯一，避免干擾其他使用者。
        // 加入 GUID 以增加唯一性，防止與其他應用程式衝突。
        _mutex = new Mutex(true, @"Local\InputBox_40A57F4D-4C7E-45FD-9DC7-BE96DC026D66_SingleInstance", out bool createdNew);

        if (!createdNew)
        {
            // 如果已經有實例在執行，直接退出。
            _mutex.Dispose();
            _mutex = null;

            return;
        }

        // 強制將所有 UI 執行緒例外路由到 ThreadException 事件。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // 設定全域例外處理。
        Application.ThreadException += (sender, e) => HandleException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleException(e.ExceptionObject as Exception);

        // 註冊系統事件：在關機或登出時，強制停止所有硬體震動。
        static void sessionEndingHandler(object s, SessionEndingEventArgs e)
        {
            FeedbackService.EmergencyStopAllActiveControllers();
        }

        SystemEvents.SessionEnding += sessionEndingHandler;

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
            SystemEvents.SessionEnding -= sessionEndingHandler;

            ReleaseMutex();
        }
    }

    /// <summary>
    /// 釋放單一執行個體 Mutex（用於重啟情境）
    /// </summary>
    public static void ReleaseMutex()
    {
        if (_mutex != null)
        {
            try
            {
                // 如果我們擁有 Mutex，則釋放它。
                _mutex.ReleaseMutex();
            }
            catch (ObjectDisposedException)
            {
                // 已釋放則忽略。
            }
            catch (UnauthorizedAccessException)
            {
                // 不具備所有權時呼叫 ReleaseMutex 會拋出此例外，可忽略。
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"釋放 Mutex 時發生錯誤：{ex.Message}");
            }
            finally
            {
                _mutex.Dispose();
                _mutex = null;
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

        // 記錄錯誤。
        Debug.WriteLine($"[嚴重] 未捕捉例外：{ex}");

        // 緊急停止所有控制器震動，防止崩潰後手把持續震動。
        FeedbackService.EmergencyStopAllActiveControllers();

        MessageBox.Show(
            ex.Message,
            Strings.Err_Title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        // 確保程式完全退出。
        Environment.Exit(1);
    }
}