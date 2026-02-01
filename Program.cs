using InputBox.Libraries.Configuration;
using InputBox.Libraries.Input;
using InputBox.Libraries.Interop;
using InputBox.Resources;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputBox;

internal static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 設定全域例外處理。
        Application.ThreadException += (sender, e) => HandleException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleException(e.ExceptionObject as Exception);

        try
        {
            // 載入 XInput DLL。
            NativeLibrary.SetDllImportResolver(typeof(GamepadController).Assembly, DllResolver.ResolveXInput);

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

        MessageBox.Show(
            ex.Message,
            Strings.Err_Title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        // 確保程式完全退出。
        Environment.Exit(1);
    }
}